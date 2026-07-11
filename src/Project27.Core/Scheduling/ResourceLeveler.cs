using Project27.Core.Time;
using Project27.Core.Usage;

namespace Project27.Core.Scheduling;

public sealed record LevelingDelayApplied(ProjectTask Task, decimal DelayMinutes);

public sealed record Overallocation(Resource Resource, DateOnly Day, decimal DemandMinutes, decimal CapacityMinutes);

public sealed record LevelingResult(
    IReadOnlyList<LevelingDelayApplied> Delays,
    IReadOnlyList<Overallocation> RemainingOverallocations);

/// <summary>
/// Priority-based resource leveling (docs/spec/10-advanced-scheduling.md): delays
/// the least important contributor of the earliest overallocated resource-day
/// until demand fits capacity, one working day at a time.
/// </summary>
internal static class ResourceLeveler
{
    // Sub-minute demand spill from contour rounding must not count as overallocation.
    private const decimal Epsilon = 0.5m;

    public static LevelingResult Level(Project project)
    {
        ClearDelays(project);
        project.Recalculate();

        var applied = new Dictionary<ProjectTask, decimal>();
        var guard = (project.Tasks.Count * 50) + 200;
        while (guard-- > 0)
        {
            var overallocations = FindOverallocations(project);
            if (overallocations.Count == 0)
            {
                break;
            }

            var conflict = overallocations[0];

            var victim = PickVictim(project, conflict);
            if (victim is null)
            {
                break; // nothing left that may be delayed; report what remains
            }

            var calendar = victim.Calendar ?? project.Calendar;
            var resumeAt = calendar.NextWorkingTime(conflict.Day.AddDays(1).ToDateTime(TimeOnly.MinValue));
            var delta = calendar.WorkBetween(victim.Start!.Value, resumeAt);
            if (delta <= 0)
            {
                break;
            }

            victim.LevelingDelayMinutes += delta;
            applied[victim] = applied.GetValueOrDefault(victim) + delta;
            project.Recalculate();
        }

        return new LevelingResult(
            [.. applied.OrderBy(pair => pair.Key.RowNumber).Select(pair => new LevelingDelayApplied(pair.Key, pair.Value))],
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

    /// <summary>
    /// Contributors of the conflicted day that may be delayed, worst candidate first:
    /// lowest priority, then most slack, then latest start, then highest row.
    /// Priority-1000, started, manual, milestone, and must-on tasks are untouchable.
    /// </summary>
    private static ProjectTask? PickVictim(Project project, Overallocation conflict)
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
                PercentComplete: 0,
                IsMilestone: false,
                ActualStart: null,
                Start: not null,
            }
                && task.Priority < 1000
                && task.Constraint is not (ConstraintType.MustStartOn or ConstraintType.MustFinishOn));

        // Preferring an already-delayed task keeps the choice stable across
        // iterations; without it, "most slack" ping-pongs between peers forever.
        return candidates
            .OrderBy(task => task.Priority)
            .ThenByDescending(task => task.LevelingDelayMinutes > 0)
            .ThenByDescending(task => task.TotalSlackMinutes ?? 0m)
            .ThenByDescending(task => task.Start)
            .ThenByDescending(task => task.RowNumber)
            .FirstOrDefault();
    }
}
