using System.Diagnostics;

namespace Project27.Core.Time;

public sealed partial class WorkCalendar : IWorkSchedule;

/// <summary>
/// Working-time arithmetic over any <see cref="IWorkSchedule"/>. All operations are
/// time-zone-naive and minute-interval based but preserve sub-minute precision of
/// inputs (tick granularity).
/// </summary>
public static class WorkScheduleArithmetic
{
    // Consecutive non-working days scanned before concluding the schedule is dead.
    private const int MaxNonWorkingScanDays = 3653;

    /// <summary>True if <paramref name="time"/> lies inside a working interval (start-inclusive, end-exclusive).</summary>
    public static bool IsWorkingTime(this IWorkSchedule schedule, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var day = schedule.GetDaySchedule(DateOnly.FromDateTime(time));
        var ticksOfDay = time.TimeOfDay.Ticks;
        foreach (var interval in day.Intervals)
        {
            if (ticksOfDay >= interval.StartMinute * TimeSpan.TicksPerMinute
                && ticksOfDay < interval.EndMinute * TimeSpan.TicksPerMinute)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Earliest working instant at or after <paramref name="time"/>.</summary>
    public static DateTime NextWorkingTime(this IWorkSchedule schedule, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        foreach (var (start, _) in IntervalsOnward(schedule, time, until: null))
        {
            return start > time ? start : time;
        }

        throw NoWorkingTime(schedule);
    }

    /// <summary>
    /// Latest working instant at or before <paramref name="time"/>. Interval ends count
    /// as working here (a task may finish exactly at 17:00).
    /// </summary>
    public static DateTime PreviousWorkingTime(this IWorkSchedule schedule, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        foreach (var (_, end) in IntervalsBackward(schedule, time, until: null))
        {
            return end < time ? end : time;
        }

        throw NoWorkingTime(schedule);
    }

    /// <summary>
    /// Adds working minutes (signed; negative walks backward). A forward result may land
    /// exactly on an interval end, a backward result exactly on an interval start.
    /// </summary>
    public static DateTime AddWork(this IWorkSchedule schedule, DateTime start, decimal minutes)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var ticks = (long)Math.Round(minutes * TimeSpan.TicksPerMinute);
        if (ticks > 0)
        {
            foreach (var (intervalStart, intervalEnd) in IntervalsOnward(schedule, start, until: null))
            {
                var begin = intervalStart > start ? intervalStart : start;
                var available = (intervalEnd - begin).Ticks;
                if (available >= ticks)
                {
                    return begin.AddTicks(ticks);
                }

                ticks -= available;
            }
        }
        else if (ticks < 0)
        {
            ticks = -ticks;
            foreach (var (intervalStart, intervalEnd) in IntervalsBackward(schedule, start, until: null))
            {
                var end = intervalEnd < start ? intervalEnd : start;
                var available = (end - intervalStart).Ticks;
                if (available >= ticks)
                {
                    return end.AddTicks(-ticks);
                }

                ticks -= available;
            }
        }
        else
        {
            return start;
        }

        throw new UnreachableException("Interval enumeration is unbounded and throws on dead schedules.");
    }

    /// <summary>Signed working minutes between two instants.</summary>
    public static decimal WorkBetween(this IWorkSchedule schedule, DateTime from, DateTime to)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (to < from)
        {
            return -WorkBetween(schedule, to, from);
        }

        var totalTicks = 0L;
        foreach (var (start, end) in IntervalsOnward(schedule, from, until: to))
        {
            if (start >= to)
            {
                break;
            }

            var begin = start > from ? start : from;
            var stop = end < to ? end : to;
            if (stop > begin)
            {
                totalTicks += (stop - begin).Ticks;
            }
        }

        return (decimal)totalTicks / TimeSpan.TicksPerMinute;
    }

    /// <summary>
    /// Working intervals from <paramref name="from"/> forward, skipping intervals that end
    /// at or before it. Bounded by <paramref name="until"/>'s day if given, otherwise
    /// throws after <see cref="MaxNonWorkingScanDays"/> consecutive non-working days.
    /// </summary>
    private static IEnumerable<(DateTime Start, DateTime End)> IntervalsOnward(IWorkSchedule schedule, DateTime from, DateTime? until)
    {
        var date = DateOnly.FromDateTime(from);
        var lastDate = until is { } u ? DateOnly.FromDateTime(u) : DateOnly.MaxValue;
        var idleDays = 0;
        while (date <= lastDate)
        {
            var day = schedule.GetDaySchedule(date);
            if (day.IsWorking)
            {
                idleDays = 0;
                var midnight = date.ToDateTime(System.TimeOnly.MinValue);
                foreach (var interval in day.Intervals)
                {
                    var end = midnight.AddMinutes(interval.EndMinute);
                    if (end > from)
                    {
                        yield return (midnight.AddMinutes(interval.StartMinute), end);
                    }
                }
            }
            else if (until is null && ++idleDays > MaxNonWorkingScanDays)
            {
                throw NoWorkingTime(schedule);
            }

            if (date == DateOnly.MaxValue)
            {
                yield break;
            }

            date = date.AddDays(1);
        }
    }

    /// <summary>Working intervals from <paramref name="from"/> backward, skipping intervals that start at or after it.</summary>
    private static IEnumerable<(DateTime Start, DateTime End)> IntervalsBackward(IWorkSchedule schedule, DateTime from, DateTime? until)
    {
        var date = DateOnly.FromDateTime(from);
        var lastDate = until is { } u ? DateOnly.FromDateTime(u) : DateOnly.MinValue;
        var idleDays = 0;
        while (date >= lastDate)
        {
            var day = schedule.GetDaySchedule(date);
            if (day.IsWorking)
            {
                idleDays = 0;
                var midnight = date.ToDateTime(System.TimeOnly.MinValue);
                var intervals = day.Intervals;
                for (var i = intervals.Length - 1; i >= 0; i--)
                {
                    var start = midnight.AddMinutes(intervals[i].StartMinute);
                    if (start < from)
                    {
                        yield return (start, midnight.AddMinutes(intervals[i].EndMinute));
                    }
                }
            }
            else if (until is null && ++idleDays > MaxNonWorkingScanDays)
            {
                throw NoWorkingTime(schedule);
            }

            if (date == DateOnly.MinValue)
            {
                yield break;
            }

            date = date.AddDays(-1);
        }
    }

    private static InvalidOperationException NoWorkingTime(IWorkSchedule schedule)
        => new($"Schedule '{schedule.Name}' has no working time within {MaxNonWorkingScanDays} days of the requested date.");
}
