using Project27.Core.Time;

namespace Project27.Core.Usage;

/// <summary>One day's slice of an assignment's or task's work and cost.</summary>
public readonly record struct TimephasedBucket(DateOnly Date, decimal WorkMinutes, decimal Cost);

/// <summary>
/// Time-phased distribution of assignment work and cost
/// (docs/spec/09-views-fields.md §9c). Work spreads over the assignment's span on
/// its schedule following the contour's decile pattern; per-use, material, and
/// expense costs follow the resource's accrual. Computed from schedule outputs —
/// recalculate first.
/// </summary>
public static class Timephased
{
    /// <summary>Per-decile utilisation patterns (same tables as AverageUtilization).</summary>
    private static readonly Dictionary<WorkContour, int[]> Deciles = new()
    {
        [WorkContour.Flat] = [100, 100, 100, 100, 100, 100, 100, 100, 100, 100],
        [WorkContour.BackLoaded] = [10, 15, 25, 50, 50, 75, 75, 100, 100, 100],
        [WorkContour.FrontLoaded] = [100, 100, 100, 75, 75, 50, 50, 25, 15, 10],
        [WorkContour.DoublePeak] = [25, 50, 100, 50, 25, 25, 50, 100, 50, 25],
        [WorkContour.EarlyPeak] = [25, 50, 100, 100, 50, 50, 25, 25, 15, 10],
        [WorkContour.LatePeak] = [10, 15, 25, 25, 50, 50, 100, 100, 50, 25],
        [WorkContour.Bell] = [10, 20, 40, 80, 100, 100, 80, 40, 20, 10],
        [WorkContour.Turtle] = [25, 50, 75, 100, 100, 100, 100, 75, 50, 25],
    };

    /// <summary>Daily work/cost buckets of one assignment; empty when unscheduled.</summary>
    public static IReadOnlyList<TimephasedBucket> ForAssignment(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var task = assignment.Task;
        if (assignment.Start is not { } start || assignment.Finish is not { } finish)
        {
            return [];
        }

        var project = task.Project;
        var schedule = AssignmentSchedule(assignment);
        var buckets = new List<TimephasedBucket>();

        if (assignment.Resource.Type == ResourceType.Work && assignment.WorkMinutes > 0 && finish > start)
        {
            var span = schedule.WorkBetween(start, finish);
            if (span > 0)
            {
                var shares = Deciles[assignment.Contour];
                decimal total = shares.Sum();
                var cumulative = 0m; // working minutes of the span consumed so far
                // Rounded cumulative work/cost telescope, so buckets sum exactly to the totals.
                var rateCost = assignment.Resource
                    .RateTable(assignment.RateTable)
                    .RateAt(start).StandardRate
                    .CostForMinutes(assignment.WorkMinutes, project.TimeSettings);
                var (workSoFar, costSoFar) = (0m, 0m);

                for (var day = DateOnly.FromDateTime(start); day <= DateOnly.FromDateTime(finish); day = day.AddDays(1))
                {
                    var dayStart = Later(day.ToDateTime(TimeOnly.MinValue), start);
                    var dayEnd = Earlier(day.AddDays(1).ToDateTime(TimeOnly.MinValue), finish);
                    if (dayEnd <= dayStart)
                    {
                        continue;
                    }

                    var minutesToday = schedule.WorkBetween(dayStart, dayEnd);
                    if (minutesToday <= 0)
                    {
                        continue;
                    }

                    cumulative += minutesToday;
                    var share = CumulativeShare(shares, total, cumulative / span);
                    var workCumulative = Math.Round(assignment.WorkMinutes * share, 8);
                    var costCumulative = Math.Round(rateCost * share, 8);
                    var work = workCumulative - workSoFar;
                    if (work > 0)
                    {
                        buckets.Add(new TimephasedBucket(day, work, costCumulative - costSoFar));
                        (workSoFar, costSoFar) = (workCumulative, costCumulative);
                    }
                }
            }
        }

        AddLumpCosts(assignment, buckets, start, finish);
        return buckets;
    }

    /// <summary>Daily buckets of a task: its own assignments, or the rolled-up active children for summaries.</summary>
    public static IReadOnlyList<TimephasedBucket> ForTask(ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var streams = task.IsSummary
            ? task.Children.Where(c => c.IsActive).Select(ForTask)
            : task.Assignments.Select(ForAssignment);
        return Merge(streams);
    }

    /// <summary>Sums bucket streams by date.</summary>
    public static IReadOnlyList<TimephasedBucket> Merge(IEnumerable<IReadOnlyList<TimephasedBucket>> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var byDate = new SortedDictionary<DateOnly, (decimal Work, decimal Cost)>();
        foreach (var stream in streams)
        {
            foreach (var bucket in stream)
            {
                var current = byDate.TryGetValue(bucket.Date, out var existing) ? existing : (0m, 0m);
                byDate[bucket.Date] = (current.Item1 + bucket.WorkMinutes, current.Item2 + bucket.Cost);
            }
        }

        return [.. byDate.Select(pair => new TimephasedBucket(pair.Key, pair.Value.Work, pair.Value.Cost))];
    }

    /// <summary>Aggregates daily buckets into weeks starting on the project's week start.</summary>
    public static IReadOnlyList<TimephasedBucket> ByWeek(IReadOnlyList<TimephasedBucket> daily, DayOfWeek weekStartsOn)
    {
        ArgumentNullException.ThrowIfNull(daily);
        var byWeek = new SortedDictionary<DateOnly, (decimal Work, decimal Cost)>();
        foreach (var bucket in daily)
        {
            var offset = ((int)bucket.Date.DayOfWeek - (int)weekStartsOn + 7) % 7;
            var week = bucket.Date.AddDays(-offset);
            var current = byWeek.TryGetValue(week, out var existing) ? existing : (0m, 0m);
            byWeek[week] = (current.Item1 + bucket.WorkMinutes, current.Item2 + bucket.Cost);
        }

        return [.. byWeek.Select(pair => new TimephasedBucket(pair.Key, pair.Value.Work, pair.Value.Cost))];
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Cumulative share of total work at a span fraction (piecewise linear over deciles).</summary>
    private static decimal CumulativeShare(int[] shares, decimal total, decimal fraction)
    {
        var clamped = Math.Clamp(fraction, 0m, 1m);
        var position = clamped * 10m;
        var wholeDeciles = (int)Math.Floor(position);
        var sum = 0m;
        for (var i = 0; i < wholeDeciles && i < 10; i++)
        {
            sum += shares[i];
        }

        if (wholeDeciles < 10)
        {
            sum += shares[wholeDeciles] * (position - wholeDeciles);
        }

        return sum / total;
    }

    /// <summary>Material quantity cost, expense amounts, and per-use fees, placed by the resource's accrual.</summary>
    private static void AddLumpCosts(Assignment assignment, List<TimephasedBucket> buckets, DateTime start, DateTime finish)
    {
        var resource = assignment.Resource;
        var lump = resource.Type switch
        {
            ResourceType.Cost => assignment.CostInput,
            ResourceType.Material => assignment.Cost,
            _ => resource.RateTable(assignment.RateTable).RateAt(start).CostPerUse,
        };
        if (lump == 0m)
        {
            return;
        }

        var accrual = resource.Type == ResourceType.Cost ? CostAccrual.Prorated : resource.Accrual;
        var firstDay = DateOnly.FromDateTime(start);
        var lastDay = DateOnly.FromDateTime(finish);
        switch (accrual)
        {
            case CostAccrual.Start:
                AddCost(buckets, firstDay, lump);
                break;
            case CostAccrual.End:
                AddCost(buckets, lastDay, lump);
                break;
            default:
            {
                var days = lastDay.DayNumber - firstDay.DayNumber + 1;
                var perDay = lump / days;
                for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
                {
                    AddCost(buckets, day, perDay);
                }

                break;
            }
        }
    }

    private static void AddCost(List<TimephasedBucket> buckets, DateOnly day, decimal cost)
    {
        var index = buckets.FindIndex(b => b.Date == day);
        if (index >= 0)
        {
            buckets[index] = buckets[index] with { Cost = buckets[index].Cost + cost };
            return;
        }

        var insertAt = buckets.FindIndex(b => b.Date > day);
        buckets.Insert(insertAt < 0 ? buckets.Count : insertAt, new TimephasedBucket(day, 0m, cost));
    }

    /// <summary>Mirrors the scheduler's assignment-schedule rule (task × resource calendars).</summary>
    private static IWorkSchedule AssignmentSchedule(Assignment assignment)
    {
        var task = assignment.Task;
        var resourceCalendar = !task.IgnoresResourceCalendars && assignment.Resource.Type == ResourceType.Work
            ? assignment.Resource.Calendar
            : null;
        if (resourceCalendar is null)
        {
            return task.Calendar ?? task.Project.Calendar;
        }

        return task.Calendar is { } taskCalendar
            ? new ScheduleIntersection(taskCalendar, resourceCalendar)
            : resourceCalendar;
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
