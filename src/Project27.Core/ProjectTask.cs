using Project27.Core.Time;

namespace Project27.Core;

/// <summary>A scheduled segment of a (possibly split) task.</summary>
public readonly record struct TaskSegment(DateTime Start, DateTime Finish);

/// <summary>
/// A task in the project outline. Summary tasks (tasks with children) are always
/// rolled up from their children. Scheduled dates are outputs of
/// <see cref="Project.Recalculate"/> and null before the first calculation.
/// </summary>
public sealed class ProjectTask
{
    private readonly List<ProjectTask> _children = [];
    private readonly List<TaskDependency> _predecessors = [];
    private readonly List<TaskDependency> _successors = [];
    private List<(decimal WorkMinutes, decimal GapMinutes)>? _splitParts;
    private Duration _duration = new(1, DurationUnit.Days, isEstimated: true);
    private decimal _durationMinutes;
    private bool? _milestoneOverride;
    private int _priority = 500;

    internal ProjectTask(Project project, string name, int uniqueId, Guid? id = null)
    {
        Project = project;
        Name = name;
        UniqueId = uniqueId;
        Id = id ?? Guid.NewGuid();
        _durationMinutes = _duration.ToMinutes(project.TimeSettings);
    }

    internal bool? MilestoneOverrideRaw
    {
        get => _milestoneOverride;
        set => _milestoneOverride = value;
    }

    internal void RestoreSplitParts(List<(decimal WorkMinutes, decimal GapMinutes)>? parts)
        => _splitParts = parts;

    public Project Project { get; }

    public Guid Id { get; }

    /// <summary>Stable numeric id (MSPDI UID). Never reused within a project.</summary>
    public int UniqueId { get; }

    /// <summary>Pre-order row number (MSP "ID"); reassigned when the outline changes.</summary>
    public int RowNumber
    {
        get
        {
            Project.EnsureOutline();
            return RowNumberRaw;
        }
        internal set => RowNumberRaw = value;
    }

    internal int RowNumberRaw { get; set; }

    public string Name { get; set; }

    public ProjectTask? Parent { get; internal set; }

    internal List<ProjectTask> ChildrenList => _children;

    public IReadOnlyList<ProjectTask> Children => _children;

    public bool IsSummary => _children.Count > 0;

    internal bool IsRoot => Parent is null;

    public int OutlineLevel
    {
        get
        {
            Project.EnsureOutline();
            return OutlineLevelRaw;
        }
        internal set => OutlineLevelRaw = value;
    }

    internal int OutlineLevelRaw { get; set; }

    public string OutlineNumber
    {
        get
        {
            Project.EnsureOutline();
            return OutlineNumberRaw;
        }
        internal set => OutlineNumberRaw = value;
    }

    internal string OutlineNumberRaw { get; set; } = string.Empty;

    /// <summary>Custom WBS code; null falls back to <see cref="OutlineNumber"/>.</summary>
    public string? CustomWbs { get; set; }

    public string Wbs => CustomWbs ?? OutlineNumber;

    public TaskMode Mode { get; set; } = TaskMode.Auto;

    /// <summary>Inactive tasks are scheduled for display but drive nothing (no rollup, no successors).</summary>
    public bool IsActive { get; set; } = true;

    public bool IsRecurring { get; internal set; }

    /// <summary>User-facing duration. Settable on leaves only; computed for summaries.</summary>
    public Duration Duration
    {
        get => _duration;
        set
        {
            if (IsSummary)
            {
                throw new InvalidOperationException($"Duration of summary task '{Name}' is rolled up from its children.");
            }

            _duration = value;
            _durationMinutes = value.ToMinutes(Project.TimeSettings);
            _splitParts = null;
        }
    }

    /// <summary>Duration in working minutes; for summaries this is the rolled-up span.</summary>
    public decimal DurationMinutes
    {
        get => _durationMinutes;
        internal set => _durationMinutes = value;
    }

    public bool IsEstimated => _duration.IsEstimated;

    /// <summary>Milestones default to zero-duration tasks; the flag can be forced either way.</summary>
    public bool IsMilestone
    {
        get => _milestoneOverride ?? (!IsSummary && _durationMinutes == 0);
        set => _milestoneOverride = value;
    }

    public ConstraintType Constraint { get; private set; } = ConstraintType.AsSoonAsPossible;

    public DateTime? ConstraintDate { get; private set; }

    public void SetConstraint(ConstraintType type, DateTime? date = null)
    {
        var needsDate = type is not (ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible);
        if (needsDate && date is null)
        {
            throw new ArgumentException($"Constraint {type} requires a date.", nameof(date));
        }

        Constraint = type;
        ConstraintDate = needsDate ? date : null;
    }

    public DateTime? Deadline { get; set; }

    /// <summary>Task calendar; null uses the project calendar.</summary>
    public WorkCalendar? Calendar { get; set; }

    /// <summary>Leveling priority, 0–1000 (default 500).</summary>
    public int Priority
    {
        get => _priority;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _priority = value;
        }
    }

    // Manual-mode inputs.
    public DateTime? ManualStart { get; set; }

    public DateTime? ManualFinish { get; set; }

    public IReadOnlyList<TaskDependency> Predecessors => _predecessors;

    public IReadOnlyList<TaskDependency> Successors => _successors;

    internal List<TaskDependency> PredecessorsList => _predecessors;

    internal List<TaskDependency> SuccessorsList => _successors;

    // Scheduled outputs.
    public DateTime? Start { get; internal set; }

    public DateTime? Finish { get; internal set; }

    public DateTime? EarlyStart { get; internal set; }

    public DateTime? EarlyFinish { get; internal set; }

    public DateTime? LateStart { get; internal set; }

    public DateTime? LateFinish { get; internal set; }

    public decimal? TotalSlackMinutes { get; internal set; }

    public decimal? FreeSlackMinutes { get; internal set; }

    public bool IsCritical { get; internal set; }

    /// <summary>Scheduled segments; a single segment unless the task is split.</summary>
    public IReadOnlyList<TaskSegment> Segments { get; internal set; } = [];

    internal IReadOnlyList<(decimal WorkMinutes, decimal GapMinutes)> SplitParts
        => _splitParts ?? [(_durationMinutes, 0m)];

    public bool IsSplit => _splitParts is { Count: > 1 };

    /// <summary>
    /// Splits the task at <paramref name="offsetFromStart"/> of worked duration,
    /// inserting a working-time <paramref name="gap"/>.
    /// </summary>
    public void SplitAt(Duration offsetFromStart, Duration gap)
    {
        if (IsSummary)
        {
            throw new InvalidOperationException("Summary tasks cannot be split.");
        }

        var settings = Project.TimeSettings;
        var offset = offsetFromStart.ToMinutes(settings);
        var gapMinutes = gap.ToMinutes(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gapMinutes, nameof(gap));
        if (offset <= 0 || offset >= _durationMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetFromStart), "Split offset must fall strictly inside the task duration.");
        }

        var parts = _splitParts ?? [(_durationMinutes, 0m)];
        var newParts = new List<(decimal WorkMinutes, decimal GapMinutes)>();
        var consumed = 0m;
        var applied = false;
        foreach (var (work, gapAfter) in parts)
        {
            if (!applied && offset > consumed && offset < consumed + work)
            {
                var first = offset - consumed;
                newParts.Add((first, gapMinutes));
                newParts.Add((work - first, gapAfter));
                applied = true;
            }
            else
            {
                newParts.Add((work, gapAfter));
            }

            consumed += work;
        }

        if (!applied)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetFromStart), "Split offset falls on an existing split boundary.");
        }

        _splitParts = newParts;
    }

    public void ClearSplits() => _splitParts = null;

    /// <summary>Enumerates this task and all descendants (pre-order).</summary>
    public IEnumerable<ProjectTask> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var task in child.SelfAndDescendants())
            {
                yield return task;
            }
        }
    }

    internal IEnumerable<ProjectTask> Leaves()
        => IsSummary ? _children.SelectMany(c => c.Leaves()) : [this];

    public bool IsAncestorOf(ProjectTask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (var p = other.Parent; p is not null; p = p.Parent)
        {
            if (ReferenceEquals(p, this))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => $"#{RowNumber} {Name}";
}
