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
    /// <summary>
    /// Daily work/cost buckets of one assignment; empty when unscheduled. Work-day
    /// costs are priced by the rate band in force at the start of each bucket's day
    /// (deviations.md #17), so buckets sum exactly to the assignment cost.
    /// </summary>
    public static IReadOnlyList<TimephasedBucket> ForAssignment(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Start is not { } start || assignment.Finish is not { } finish)
        {
            return [];
        }

        var settings = assignment.Task.Project.TimeSettings;
        var schedule = AssignmentSchedule(assignment);
        var table = assignment.Resource.RateTable(assignment.RateTable);
        var buckets = new List<TimephasedBucket>();

        if (assignment.Resource.Type == ResourceType.Work && assignment.WorkMinutes > 0 && finish > start)
        {
            foreach (var (day, work) in WorkSlices(assignment, schedule, start, finish))
            {
                var cost = table.RateAt(day.ToDateTime(TimeOnly.MinValue)).StandardRate.CostForMinutes(work, settings);
                buckets.Add(new TimephasedBucket(day, work, cost));
            }
        }
        else if (assignment.Resource.Type == ResourceType.Material && assignment.MaterialRateUnit is not null && finish > start)
        {
            foreach (var (day, quantity) in MaterialSlices(assignment, schedule, start, finish))
            {
                var cost = quantity * table.RateAt(day.ToDateTime(TimeOnly.MinValue)).StandardRate.Amount;
                if (cost != 0m)
                {
                    buckets.Add(new TimephasedBucket(day, 0m, cost));
                }
            }
        }

        AddLumpCosts(assignment, buckets, start, finish);
        return buckets;
    }

    /// <summary>
    /// Work-resource assignment cost: each day's contoured work priced by the band in
    /// force that day, plus per-use at the start band (deviations.md #17). Falls back
    /// to flat start-band pricing while the assignment is unscheduled.
    /// </summary>
    internal static decimal WorkCost(Assignment assignment)
    {
        var project = assignment.Task.Project;
        var table = assignment.Resource.RateTable(assignment.RateTable);
        var anchor = assignment.Start ?? project.StartDate;
        var perUse = table.RateAt(anchor).CostPerUse;
        if (assignment.WorkMinutes <= 0)
        {
            return perUse;
        }

        if (assignment.Start is not { } start || assignment.Finish is not { } finish || finish <= start)
        {
            return table.RateAt(anchor).StandardRate.CostForMinutes(assignment.WorkMinutes, project.TimeSettings) + perUse;
        }

        var schedule = AssignmentSchedule(assignment);
        var cost = 0m;
        foreach (var (day, work) in WorkSlices(assignment, schedule, start, finish))
        {
            cost += table.RateAt(day.ToDateTime(TimeOnly.MinValue)).StandardRate.CostForMinutes(work, project.TimeSettings);
        }

        return cost + perUse;
    }

    /// <summary>
    /// Material assignment cost. Fixed quantities are priced whole at the start band;
    /// variable consumption (deviations.md #13) accrues per day and is priced by the
    /// band in force each day. Per-use fees price at the start band.
    /// </summary>
    internal static decimal MaterialCost(Assignment assignment)
    {
        var project = assignment.Task.Project;
        var table = assignment.Resource.RateTable(assignment.RateTable);
        var anchor = assignment.Start ?? project.StartDate;
        var perUse = table.RateAt(anchor).CostPerUse;
        if (assignment.MaterialRateUnit is null
            || assignment.Start is not { } start || assignment.Finish is not { } finish || finish <= start)
        {
            return (assignment.MaterialQuantity * table.RateAt(anchor).StandardRate.Amount) + perUse;
        }

        var schedule = AssignmentSchedule(assignment);
        var cost = 0m;
        foreach (var (day, quantity) in MaterialSlices(assignment, schedule, start, finish))
        {
            cost += quantity * table.RateAt(day.ToDateTime(TimeOnly.MinValue)).StandardRate.Amount;
        }

        return cost + perUse;
    }

    /// <summary>
    /// Per-day contoured work of one assignment over its span. Rounded cumulative
    /// telescoping: slices sum exactly to the assignment work.
    /// </summary>
    internal static IEnumerable<(DateOnly Day, decimal WorkMinutes)> WorkSlices(
        Assignment assignment, IWorkSchedule schedule, DateTime start, DateTime finish)
    {
        var span = schedule.WorkBetween(start, finish);
        if (span <= 0)
        {
            yield break;
        }

        var shares = assignment.Contour.Deciles();
        decimal total = shares.Sum();
        var cumulative = 0m; // working minutes of the span consumed so far
        var workSoFar = 0m;
        foreach (var (day, minutesToday) in DaySlices(schedule, start, finish))
        {
            cumulative += minutesToday;
            var share = CumulativeShare(shares, total, cumulative / span);
            var workCumulative = Math.Round(assignment.WorkMinutes * share, 8);
            var work = workCumulative - workSoFar;
            if (work > 0)
            {
                yield return (day, work);
                workSoFar = workCumulative;
            }
        }
    }

    /// <summary>Per-day variable material consumption, linear over the span's working time; slices sum exactly to the total quantity.</summary>
    internal static IEnumerable<(DateOnly Day, decimal Quantity)> MaterialSlices(
        Assignment assignment, IWorkSchedule schedule, DateTime start, DateTime finish)
    {
        var span = schedule.WorkBetween(start, finish);
        if (span <= 0)
        {
            yield break;
        }

        var total = assignment.MaterialQuantity;
        var cumulative = 0m;
        var quantitySoFar = 0m;
        foreach (var (day, minutesToday) in DaySlices(schedule, start, finish))
        {
            cumulative += minutesToday;
            var quantityCumulative = Math.Round(total * cumulative / span, 8);
            var quantity = quantityCumulative - quantitySoFar;
            if (quantity > 0)
            {
                yield return (day, quantity);
                quantitySoFar = quantityCumulative;
            }
        }
    }

    /// <summary>Working minutes of each day the span touches, on the given schedule.</summary>
    private static IEnumerable<(DateOnly Day, decimal Minutes)> DaySlices(IWorkSchedule schedule, DateTime start, DateTime finish)
    {
        for (var day = DateOnly.FromDateTime(start); day <= DateOnly.FromDateTime(finish); day = day.AddDays(1))
        {
            var dayStart = Later(day.ToDateTime(TimeOnly.MinValue), start);
            var dayEnd = Earlier(day.AddDays(1).ToDateTime(TimeOnly.MinValue), finish);
            if (dayEnd <= dayStart)
            {
                continue;
            }

            var minutesToday = schedule.WorkBetween(dayStart, dayEnd);
            if (minutesToday > 0)
            {
                yield return (day, minutesToday);
            }
        }
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
    private static decimal CumulativeShare(IReadOnlyList<int> shares, decimal total, decimal fraction)
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

    /// <summary>
    /// Fixed material quantity cost, expense amounts, and per-use fees, placed by the
    /// resource's accrual. Variable material consumption is already spread per day, so
    /// only its per-use fee lands here.
    /// </summary>
    private static void AddLumpCosts(Assignment assignment, List<TimephasedBucket> buckets, DateTime start, DateTime finish)
    {
        var resource = assignment.Resource;
        var lump = resource.Type switch
        {
            ResourceType.Cost => assignment.CostInput,
            ResourceType.Material when assignment.MaterialRateUnit is null => assignment.Cost,
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

    /// <summary>
    /// Mirrors the scheduler's assignment-schedule rule (task × resource calendars),
    /// masked to the task's scheduled segments for split tasks so work never lands in
    /// a gap (deviations.md #16).
    /// </summary>
    internal static IWorkSchedule AssignmentSchedule(Assignment assignment)
    {
        var task = assignment.Task;
        var resourceCalendar = !task.IgnoresResourceCalendars && assignment.Resource.Type == ResourceType.Work
            ? assignment.Resource.Calendar
            : null;
        IWorkSchedule schedule;
        if (resourceCalendar is null)
        {
            schedule = task.Calendar ?? task.Project.Calendar;
        }
        else
        {
            schedule = task.Calendar is { } taskCalendar
                ? new ScheduleIntersection(taskCalendar, resourceCalendar)
                : resourceCalendar;
        }

        return task.IsSplit && task.Segments.Count > 1 ? new ScheduleMask(schedule, task.Segments) : schedule;
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
