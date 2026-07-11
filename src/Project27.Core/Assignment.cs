using Project27.Core.Time;

namespace Project27.Core;

/// <summary>
/// Joins one leaf task and one resource. For work resources, Units / Work / the task
/// Duration form the scheduling triangle (docs/spec/04-resources-costs.md); edits go
/// through <see cref="SetUnits"/> / <see cref="SetWork"/> / <see cref="SetContour"/>
/// so the triangle stays consistent. Start / Finish / Cost are outputs of
/// <see cref="Project.Recalculate"/>.
/// </summary>
public sealed class Assignment
{
    private decimal _delayMinutes;

    internal Assignment(ProjectTask task, Resource resource, Guid? id = null)
    {
        Task = task;
        Resource = resource;
        Id = id ?? Guid.NewGuid();
        Units = 1m;
    }

    public Guid Id { get; }

    public ProjectTask Task { get; }

    public Resource Resource { get; }

    /// <summary>Work resources: assignment units (1.0 = 100%). Material: quantity consumed.</summary>
    public decimal Units { get; internal set; }

    /// <summary>Person-time in working minutes. Work resources only.</summary>
    public decimal WorkMinutes { get; internal set; }

    public WorkContour Contour { get; internal set; }

    /// <summary>Working-time offset from the task start, on the assignment schedule.</summary>
    public decimal DelayMinutes
    {
        get => _delayMinutes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _delayMinutes = value;
        }
    }

    /// <summary>Which of the resource's five cost rate tables prices this assignment.</summary>
    public CostRateTableId RateTable { get; set; }

    private decimal _costInput;

    /// <summary>The expense amount for cost-resource assignments.</summary>
    public decimal CostInput
    {
        get => _costInput;
        set
        {
            if (Resource.Type != ResourceType.Cost)
            {
                throw new InvalidOperationException($"'{Resource.Name}' is a {Resource.Type} resource; its cost is computed from rates, not entered.");
            }

            _costInput = value;
        }
    }

    private Dictionary<int, AssignmentBaseline>? _baselines;

    /// <summary>The captured work/cost in a baseline slot; null when never baselined.</summary>
    public AssignmentBaseline? Baseline(int slot = 0)
        => _baselines is not null && _baselines.TryGetValue(slot, out var baseline) ? baseline : null;

    internal void SetBaselineSlot(int slot, AssignmentBaseline baseline)
        => (_baselines ??= [])[slot] = baseline;

    internal void ClearBaselineSlot(int slot) => _baselines?.Remove(slot);

    internal IReadOnlyDictionary<int, AssignmentBaseline> BaselineSlots
        => _baselines ?? (IReadOnlyDictionary<int, AssignmentBaseline>)System.Collections.Immutable.ImmutableDictionary<int, AssignmentBaseline>.Empty;

    // Outputs of Recalculate.
    public DateTime? Start { get; internal set; }

    public DateTime? Finish { get; internal set; }

    public decimal Cost { get; internal set; }

    public void SetUnits(decimal units)
    {
        if (Resource.Type == ResourceType.Work)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(units);
            Units = units;
            EffortTriangle.OnAssignmentEdited(this, TriangleEdit.Units);
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfNegative(units);
            Units = units;
        }
    }

    public void SetWork(Duration work)
    {
        EnsureWorkResource();
        var minutes = work.ToMinutes(Task.Project.TimeSettings);
        ArgumentOutOfRangeException.ThrowIfNegative(minutes, nameof(work));
        WorkMinutes = minutes;
        EffortTriangle.OnAssignmentEdited(this, TriangleEdit.Work);
    }

    public void SetContour(WorkContour contour)
    {
        EnsureWorkResource();
        Contour = contour;
        EffortTriangle.OnAssignmentEdited(this, TriangleEdit.Contour);
    }

    internal void RestoreCostInput(decimal cost) => _costInput = cost;

    private void EnsureWorkResource()
    {
        if (Resource.Type != ResourceType.Work)
        {
            throw new InvalidOperationException($"'{Resource.Name}' is a {Resource.Type} resource and carries no work.");
        }
    }

    public override string ToString() => $"{Resource.Name} → {Task.Name}";
}

internal enum TriangleEdit
{
    Units,
    Work,
    Contour,
}

/// <summary>
/// The Work = Duration × Units × avg(Contour) triangle. Which corner recalculates
/// follows MS Project's canonical table (docs/spec/04-resources-costs.md).
/// </summary>
internal static class EffortTriangle
{
    /// <summary>Working duration this assignment needs, in assignment-schedule minutes.</summary>
    internal static decimal AssignmentDurationMinutes(Assignment assignment)
        => assignment.Units <= 0
            ? 0m
            : assignment.WorkMinutes / (assignment.Units * assignment.Contour.AverageUtilization());

    /// <summary>Work implied by the task duration for the given units and contour.</summary>
    internal static decimal ImpliedWork(ProjectTask task, decimal units, WorkContour contour)
        => task.DurationMinutes * units * contour.AverageUtilization();

    internal static void OnDurationEdited(ProjectTask task)
    {
        var duration = task.DurationMinutes;
        foreach (var assignment in WorkAssignments(task))
        {
            if (task.Type == TaskType.FixedWork)
            {
                var span = duration * assignment.Contour.AverageUtilization();
                if (span > 0)
                {
                    assignment.Units = assignment.WorkMinutes / span;
                }
            }
            else
            {
                assignment.WorkMinutes = ImpliedWork(task, assignment.Units, assignment.Contour);
            }
        }
    }

    internal static void OnAssignmentEdited(Assignment edited, TriangleEdit kind)
    {
        var task = edited.Task;
        if (task.Type == TaskType.FixedDuration)
        {
            var span = task.DurationMinutes * edited.Contour.AverageUtilization();
            if (kind == TriangleEdit.Work && span > 0)
            {
                edited.Units = edited.WorkMinutes / span;
            }
            else if (kind == TriangleEdit.Units)
            {
                edited.WorkMinutes = span * edited.Units;
            }

            // Contour change keeps both work and duration for fixed-duration tasks.
            return;
        }

        RecalcDurationFromAssignments(task);
    }

    /// <summary>Sets the leaf duration to the longest assignment's working duration.</summary>
    internal static void RecalcDurationFromAssignments(ProjectTask task)
    {
        decimal? max = null;
        foreach (var assignment in WorkAssignments(task))
        {
            if (assignment.WorkMinutes > 0 && assignment.Units > 0)
            {
                var minutes = AssignmentDurationMinutes(assignment);
                max = max is { } m && m >= minutes ? m : minutes;
            }
        }

        if (max is { } duration && duration != task.DurationMinutes)
        {
            task.SetDurationFromMinutes(duration);
        }
    }

    /// <summary>Redistributes total work over work assignments proportionally to units (effort-driven).</summary>
    internal static void Redistribute(ProjectTask task, decimal totalWork)
    {
        var assignments = WorkAssignments(task).ToList();
        var totalUnits = assignments.Sum(a => a.Units);
        if (totalUnits <= 0)
        {
            return;
        }

        foreach (var assignment in assignments)
        {
            assignment.WorkMinutes = totalWork * assignment.Units / totalUnits;
        }

        if (task.Type == TaskType.FixedDuration)
        {
            foreach (var assignment in assignments)
            {
                var span = task.DurationMinutes * assignment.Contour.AverageUtilization();
                if (span > 0)
                {
                    assignment.Units = assignment.WorkMinutes / span;
                }
            }
        }
        else
        {
            RecalcDurationFromAssignments(task);
        }
    }

    internal static IEnumerable<Assignment> WorkAssignments(ProjectTask task)
        => task.AssignmentsList.Where(a => a.Resource.Type == ResourceType.Work);
}
