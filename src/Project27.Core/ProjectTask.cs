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
    private readonly List<Assignment> _assignments = [];
    private bool _effortDriven;
    private Dictionary<int, TaskBaseline>? _baselines;
    private int _percentComplete;
    private DateTime? _actualFinish;
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
            EffortTriangle.OnDurationEdited(this);
        }
    }

    /// <summary>Rewrites the duration from the triangle without re-triggering it.</summary>
    internal void SetDurationFromMinutes(decimal minutes)
    {
        var unit = _duration.IsElapsed ? DurationUnit.Days : _duration.Unit;
        _duration = Duration.FromMinutes(minutes, unit, Project.TimeSettings, _duration.IsEstimated);
        _durationMinutes = minutes;
        _splitParts = null;
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

    /// <summary>Cosmetic display attributes; null when every field is at its default.</summary>
    public TaskFormatting? Formatting { get; set; }

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

    /// <summary>Which corner of the Work = Duration × Units triangle stays fixed on edits.</summary>
    public TaskType Type { get; set; } = TaskType.FixedUnits;

    /// <summary>
    /// When set, adding/removing work resources keeps total work constant.
    /// Fixed-work tasks are inherently effort-driven. Default off (as in MSP 2010+).
    /// </summary>
    public bool IsEffortDriven
    {
        get => Type == TaskType.FixedWork || _effortDriven;
        set
        {
            if (!value && Type == TaskType.FixedWork)
            {
                throw new InvalidOperationException("Fixed-work tasks are always effort-driven.");
            }

            _effortDriven = value;
        }
    }

    /// <summary>When set, assignments are scheduled on the task calendar alone.</summary>
    public bool IgnoresResourceCalendars { get; set; }

    /// <summary>Task-level cost independent of resources; rolls up alongside assignment costs.</summary>
    public decimal FixedCost { get; set; }

    public CostAccrual FixedCostAccrual { get; set; } = CostAccrual.Prorated;

    public IReadOnlyList<Assignment> Assignments => _assignments;

    internal List<Assignment> AssignmentsList => _assignments;

    /// <summary>Total work in minutes: Σ work-resource assignments; summaries roll up active children.</summary>
    public decimal WorkMinutes
        => IsSummary
            ? _children.Where(c => c.IsActive).Sum(c => c.WorkMinutes)
            : _assignments.Where(a => a.Resource.Type == ResourceType.Work).Sum(a => a.WorkMinutes);

    /// <summary>Fixed cost + assignment costs; summaries roll up active children. Assignment costs are outputs of Recalculate.</summary>
    public decimal Cost
        => FixedCost + (IsSummary
            ? _children.Where(c => c.IsActive).Sum(c => c.Cost)
            : _assignments.Sum(a => a.Cost));

    // ------------------------------------------------------------- tracking

    /// <summary>
    /// Duration-based completion, 0–100. Leaf input; summaries roll up completed
    /// working minutes over active leaves (not directly editable — deviation #22).
    /// Dropping below 100 clears the actual finish.
    /// </summary>
    public int PercentComplete
    {
        get
        {
            if (!IsSummary)
            {
                return _percentComplete;
            }

            decimal total = 0m, completed = 0m, percentSum = 0m;
            var leafCount = 0;
            foreach (var leaf in Leaves())
            {
                if (!leaf.IsActive)
                {
                    continue;
                }

                total += leaf.DurationMinutes;
                completed += leaf.CompletedMinutes;
                percentSum += leaf.PercentComplete;
                leafCount++;
            }

            // All-milestone summaries have no duration; average the flags instead.
            return total == 0
                ? leafCount == 0 ? 0 : (int)Math.Round(percentSum / leafCount)
                : (int)Math.Round(completed / total * 100m);
        }
        set
        {
            if (IsSummary)
            {
                throw new InvalidOperationException($"Percent complete of summary task '{Name}' rolls up from its children.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            _percentComplete = value;
            if (value < 100)
            {
                _actualFinish = null;
            }
        }
    }

    /// <summary>Working minutes already done: duration × percent complete; summaries roll up.</summary>
    public decimal CompletedMinutes
        => IsSummary
            ? ChildrenList.Where(c => c.IsActive).Sum(c => c.CompletedMinutes)
            : DurationMinutes * _percentComplete / 100m;

    /// <summary>Pins the scheduled start, overriding dependencies and constraints. Summaries roll up.</summary>
    public DateTime? ActualStart
    {
        get => IsSummary
            ? ChildrenList.Where(c => c.IsActive).Select(c => c.ActualStart).Where(d => d is not null).DefaultIfEmpty(null).Min()
            : ActualStartRaw;
        set
        {
            if (IsSummary)
            {
                throw new InvalidOperationException($"Actual start of summary task '{Name}' rolls up from its children.");
            }

            ActualStartRaw = value;
        }
    }

    internal DateTime? ActualStartRaw { get; set; }

    /// <summary>Marks the task complete and pins the finish. Summaries roll up (all children done).</summary>
    public DateTime? ActualFinish
    {
        get
        {
            if (!IsSummary)
            {
                return _actualFinish;
            }

            DateTime? latest = null;
            foreach (var child in ChildrenList)
            {
                if (!child.IsActive)
                {
                    continue;
                }

                var childFinish = child.ActualFinish;
                if (childFinish is null)
                {
                    return null;
                }

                latest = latest is null || childFinish > latest ? childFinish : latest;
            }

            return latest;
        }
        set
        {
            if (IsSummary)
            {
                throw new InvalidOperationException($"Actual finish of summary task '{Name}' rolls up from its children.");
            }

            _actualFinish = value;
            if (value is not null)
            {
                _percentComplete = 100;
            }
        }
    }

    internal void RestoreTracking(int percentComplete, DateTime? actualStart, DateTime? actualFinish)
    {
        _percentComplete = percentComplete;
        ActualStartRaw = actualStart;
        _actualFinish = actualFinish;
    }

    /// <summary>
    /// Actual work in working minutes: explicit assignment actuals where entered, else
    /// derived from percent complete; summaries roll up active children (deviations.md #20).
    /// </summary>
    public decimal ActualWorkMinutes => IsSummary
        ? ChildrenList.Where(c => c.IsActive).Sum(c => c.ActualWorkMinutes)
        : _assignments.Where(a => a.Resource.Type == ResourceType.Work).Sum(a => a.EffectiveActualWorkMinutes);

    /// <summary>Remaining work in working minutes: assignment work minus actual work; summaries roll up.</summary>
    public decimal RemainingWorkMinutes => IsSummary
        ? ChildrenList.Where(c => c.IsActive).Sum(c => c.RemainingWorkMinutes)
        : _assignments.Where(a => a.Resource.Type == ResourceType.Work).Sum(a => a.RemainingWorkMinutes);

    /// <summary>
    /// Actual cost: fixed cost by percent complete plus assignment actuals (explicit
    /// where entered, else derived); summaries roll up active children. Assignment
    /// costs are outputs of Recalculate (deviations.md #20).
    /// </summary>
    public decimal ActualCost
        => (FixedCost * PercentComplete / 100m) + (IsSummary
            ? ChildrenList.Where(c => c.IsActive).Sum(c => c.ActualCost)
            : _assignments.Sum(a => a.EffectiveActualCost));

    /// <summary>Remaining working minutes: duration × (1 − percent complete).</summary>
    public decimal RemainingMinutes => IsSummary
        ? ChildrenList.Where(c => c.IsActive).Sum(c => c.RemainingMinutes)
        : DurationMinutes - CompletedMinutes;

    /// <summary>
    /// Sets the remaining duration: total becomes completed + remaining and the
    /// percentage is re-derived, keeping the completed span fixed.
    /// </summary>
    public void SetRemainingDuration(Duration remaining)
    {
        if (IsSummary)
        {
            throw new InvalidOperationException($"Remaining duration of summary task '{Name}' rolls up from its children.");
        }

        var remainingMinutes = remaining.ToMinutes(Project.TimeSettings);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingMinutes, nameof(remaining));
        var completed = CompletedMinutes;
        var total = completed + remainingMinutes;
        Duration = Duration.FromMinutes(total, _duration.IsElapsed ? DurationUnit.Days : _duration.Unit, Project.TimeSettings, _duration.IsEstimated);
        _percentComplete = total == 0 ? _percentComplete : (int)Math.Round(completed / total * 100m);
        if (_percentComplete < 100)
        {
            _actualFinish = null;
        }
    }

    // --------------------------------------------------------- custom fields

    private Dictionary<string, object?>? _customValues;

    /// <summary>Raw stored value of a custom field slot; null when unset (or the field is a formula).</summary>
    public object? GetCustomValue(string slotId)
        => _customValues is not null && _customValues.TryGetValue(slotId, out var value) ? value : null;

    /// <summary>Stores a typed value (string/decimal/DateTime/bool per the field's kind); null clears.</summary>
    public void SetCustomValue(Fields.CustomFieldDefinition field, object? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Formula is not null)
        {
            throw new InvalidOperationException($"'{field.Caption}' is computed by a formula; its values cannot be set.");
        }

        if (value is null)
        {
            ClearCustomValue(field.Id);
            return;
        }

        var valid = field.Kind switch
        {
            Fields.FieldKind.Text => value is string,
            Fields.FieldKind.Flag => value is bool,
            Fields.FieldKind.Date => value is DateTime,
            _ => value is decimal or int,
        };
        if (!valid)
        {
            throw new ArgumentException($"'{value}' is not a {field.Kind} value for '{field.Caption}'.", nameof(value));
        }

        (_customValues ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase))[field.Id]
            = value is int whole ? (decimal)whole : value;
    }

    internal void ClearCustomValue(string slotId) => _customValues?.Remove(slotId);

    internal IReadOnlyDictionary<string, object?> CustomValues
        => _customValues ?? (IReadOnlyDictionary<string, object?>)System.Collections.Immutable.ImmutableDictionary<string, object?>.Empty;

    /// <summary>The captured plan in a baseline slot; null when never baselined.</summary>
    public TaskBaseline? Baseline(int slot = 0)
        => _baselines is not null && _baselines.TryGetValue(slot, out var baseline) ? baseline : null;

    internal void SetBaselineSlot(int slot, TaskBaseline baseline)
        => (_baselines ??= [])[slot] = baseline;

    internal void ClearBaselineSlot(int slot) => _baselines?.Remove(slot);

    internal IReadOnlyDictionary<int, TaskBaseline> BaselineSlots
        => _baselines ?? (IReadOnlyDictionary<int, TaskBaseline>)System.Collections.Immutable.ImmutableDictionary<int, TaskBaseline>.Empty;

    private decimal _levelingDelayMinutes;

    /// <summary>
    /// Working minutes the leveler postpones this task by, applied after
    /// dependencies and constraints (docs/spec/10-advanced-scheduling.md).
    /// Written by <see cref="Project.Level"/> / cleared by <see cref="Project.ClearLeveling"/>.
    /// </summary>
    public decimal LevelingDelayMinutes
    {
        get => _levelingDelayMinutes;
        internal set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _levelingDelayMinutes = value;
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
