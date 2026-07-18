using Project27.Core.Time;
using Project27.Core.Usage;

namespace Project27.Core.Scheduling;

public sealed record LevelingDelayApplied(ProjectTask Task, decimal DelayMinutes);

public sealed record Overallocation(Resource Resource, DateOnly Day, decimal DemandMinutes, decimal CapacityMinutes);

public sealed record LevelingResult(
    IReadOnlyList<LevelingDelayApplied> Delays,
    IReadOnlyList<ProjectTask> SplitTasks,
    IReadOnlyList<Overallocation> RemainingOverallocations);

/// <summary>Victim order for resource leveling (deviations.md #28).</summary>
public enum LevelingOrder
{
    /// <summary>Row number only: the highest row is delayed first.</summary>
    IdOnly,

    /// <summary>Already-delayed, most slack, latest start, highest row; priorities ignored.</summary>
    Standard,

    /// <summary>Lowest priority first, then the standard order. The default.</summary>
    PriorityStandard,
}

/// <summary>Delay step size for resource leveling (deviations.md #28).</summary>
public enum LevelingGranularity
{
    /// <summary>Delays push the victim past the conflicted day. The default.</summary>
    Day,

    /// <summary>Delays step by the conflicted day's excess demand in working minutes.</summary>
    Minute,
}

/// <summary>Options for <see cref="Project.Level"/> (deviations.md #28/#29).</summary>
public sealed record LevelingOptions
{
    public static LevelingOptions Default { get; } = new();

    public LevelingOrder Order { get; init; } = LevelingOrder.PriorityStandard;

    public LevelingGranularity Granularity { get; init; } = LevelingGranularity.Day;

    /// <summary>
    /// Allow leveling started tasks by splitting their remaining work past the
    /// conflicted day (whole-day steps; completed work never moves). Off by
    /// default: started tasks are then never leveled. Splits created this way are
    /// ordinary task splits — <see cref="Project.ClearLeveling"/> does not undo them.
    /// </summary>
    public bool SplitInProgress { get; init; }
}

/// <summary>
/// Priority-based resource leveling (docs/spec/10-advanced-scheduling.md): resolves
/// the earliest overallocated resource-day by delaying — or, for started tasks with
/// <see cref="LevelingOptions.SplitInProgress"/>, splitting the remaining work of —
/// the least important contributor until demand fits capacity.
/// </summary>
internal static class ResourceLeveler
{
    // Sub-minute demand spill from contour rounding must not count as overallocation.
    private const decimal Epsilon = 0.5m;

    public static LevelingResult Level(Project project, LevelingOptions options)
    {
        ClearDelays(project);
        project.Recalculate();

        var applied = new Dictionary<ProjectTask, decimal>();
        var split = new List<ProjectTask>();
        var guard = (project.Tasks.Count * 50) + 200;
        while (guard-- > 0)
        {
            var overallocations = FindOverallocations(project);
            if (overallocations.Count == 0)
            {
                break;
            }

            // The earliest conflict may be genuinely unresolvable (protected or
            // completed contributors); skip it and still level the later ones.
            var progressed = false;
            foreach (var conflict in overallocations)
            {
                if (TryResolve(project, conflict, options, applied, split))
                {
                    progressed = true;
                    break;
                }
            }

            if (!progressed)
            {
                break; // nothing left that may be moved; report what remains
            }

            project.Recalculate();
        }

        return new LevelingResult(
            [.. applied.OrderBy(pair => pair.Key.RowNumber).Select(pair => new LevelingDelayApplied(pair.Key, pair.Value))],
            [.. split.OrderBy(task => task.RowNumber)],
            FindOverallocations(project));
    }

    public static void ClearDelays(Project project)
    {
        foreach (var task in project.Tasks)
        {
            task.LevelingDelayMinutes = 0m;
        }
    }

    /// <summary>All overallocated resource-days, earliest day first.</summary>
    internal static IReadOnlyList<Overallocation> FindOverallocations(Project project)
    {
        var demand = new Dictionary<(Resource Resource, DateOnly Day), decimal>();
        foreach (var resource in project.Resources)
        {
            if (resource.Type != ResourceType.Work)
            {
                continue;
            }

            foreach (var assignment in resource.Assignments)
            {
                var task = assignment.Task;
                if (!task.IsActive || task.IsSummary)
                {
                    continue;
                }

                foreach (var bucket in Timephased.ForAssignment(assignment))
                {
                    if (bucket.WorkMinutes > 0)
                    {
                        demand[(resource, bucket.Date)] = demand.GetValueOrDefault((resource, bucket.Date)) + bucket.WorkMinutes;
                    }
                }
            }
        }

        var result = new List<Overallocation>();
        foreach (var ((resource, day), minutes) in demand)
        {
            var calendar = resource.Calendar ?? project.Calendar;
            var capacity = resource.MaxUnits * calendar.GetDaySchedule(day).WorkingMinutes;
            if (minutes > capacity + Epsilon)
            {
                result.Add(new Overallocation(resource, day, minutes, capacity));
            }
        }

        return [.. result.OrderBy(o => o.Day).ThenBy(o => o.Resource.UniqueId)];
    }

    /// <summary>Moves the least important movable contributor of the conflicted day; false when none can move.</summary>
    private static bool TryResolve(
        Project project,
        Overallocation conflict,
        LevelingOptions options,
        Dictionary<ProjectTask, decimal> applied,
        List<ProjectTask> split)
    {
        foreach (var victim in Candidates(conflict, options))
        {
            var calendar = victim.Calendar ?? project.Calendar;
            var resumeAt = calendar.NextWorkingTime(conflict.Day.AddDays(1).ToDateTime(TimeOnly.MinValue));

            if (victim.PercentComplete == 0 && victim.ActualStart is null)
            {
                var delta = options.Granularity == LevelingGranularity.Day
                    ? calendar.WorkBetween(victim.Start!.Value, resumeAt)
                    : Math.Max(1m, conflict.DemandMinutes - conflict.CapacityMinutes);
                if (delta <= 0)
                {
                    continue;
                }

                victim.LevelingDelayMinutes += delta;
                applied[victim] = applied.GetValueOrDefault(victim) + delta;
                return true;
            }

            // Started task: actuals pin its start, so push the remaining work past
            // the conflicted day instead (deviation #29).
            if (SplitSurgery.PushWork(victim, calendar, conflict.Day.ToDateTime(TimeOnly.MinValue), resumeAt))
            {
                if (!split.Contains(victim))
                {
                    split.Add(victim);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Contributors of the conflicted day that may be moved, worst candidate first
    /// per the configured order. Priority-1000, manual, milestone, must-on, and
    /// completed tasks are untouchable; started tasks are candidates only with
    /// <see cref="LevelingOptions.SplitInProgress"/>.
    /// </summary>
    private static IEnumerable<ProjectTask> Candidates(Overallocation conflict, LevelingOptions options)
    {
        var candidates = conflict.Resource.Assignments
            .Where(a => Timephased.ForAssignment(a).Any(b => b.Date == conflict.Day && b.WorkMinutes > 0))
            .Select(a => a.Task)
            .Distinct()
            .Where(task => task is
            {
                IsSummary: false,
                IsActive: true,
                Mode: TaskMode.Auto,
                IsMilestone: false,
                Start: not null,
            }
                && task.PercentComplete < 100
                && (options.SplitInProgress || (task.PercentComplete == 0 && task.ActualStart is null))
                && task.Priority < 1000
                && task.Constraint is not (ConstraintType.MustStartOn or ConstraintType.MustFinishOn));

        // Preferring an already-delayed task keeps the choice stable across
        // iterations; without it, "most slack" ping-pongs between peers forever.
        return options.Order switch
        {
            LevelingOrder.IdOnly => candidates.OrderByDescending(task => task.RowNumber),
            LevelingOrder.Standard => candidates
                .OrderByDescending(task => task.LevelingDelayMinutes > 0)
                .ThenByDescending(task => task.TotalSlackMinutes ?? 0m)
                .ThenByDescending(task => task.Start)
                .ThenByDescending(task => task.RowNumber),
            _ => candidates
                .OrderBy(task => task.Priority)
                .ThenByDescending(task => task.LevelingDelayMinutes > 0)
                .ThenByDescending(task => task.TotalSlackMinutes ?? 0m)
                .ThenByDescending(task => task.Start)
                .ThenByDescending(task => task.RowNumber),
        };
    }
}
