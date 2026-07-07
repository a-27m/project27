using System.Diagnostics;

namespace Project27.Core.Time;

/// <summary>
/// Working-time arithmetic. All operations are time-zone-naive and minute-interval
/// based but preserve sub-minute precision of inputs (tick granularity).
/// </summary>
public sealed partial class WorkCalendar
{
    // Consecutive non-working days scanned before concluding the calendar is dead.
    private const int MaxNonWorkingScanDays = 3653;

    /// <summary>True if <paramref name="time"/> lies inside a working interval (start-inclusive, end-exclusive).</summary>
    public bool IsWorkingTime(DateTime time)
    {
        var schedule = GetDaySchedule(DateOnly.FromDateTime(time));
        var ticksOfDay = time.TimeOfDay.Ticks;
        foreach (var interval in schedule.Intervals)
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
    public DateTime NextWorkingTime(DateTime time)
    {
        foreach (var (start, _) in IntervalsOnward(time, until: null))
        {
            return start > time ? start : time;
        }

        throw NoWorkingTime();
    }

    /// <summary>
    /// Latest working instant at or before <paramref name="time"/>. Interval ends count
    /// as working here (a task may finish exactly at 17:00).
    /// </summary>
    public DateTime PreviousWorkingTime(DateTime time)
    {
        foreach (var (_, end) in IntervalsBackward(time, until: null))
        {
            return end < time ? end : time;
        }

        throw NoWorkingTime();
    }

    /// <summary>
    /// Adds working minutes (signed; negative walks backward). A forward result may land
    /// exactly on an interval end, a backward result exactly on an interval start.
    /// </summary>
    public DateTime AddWork(DateTime start, decimal minutes)
    {
        var ticks = (long)Math.Round(minutes * TimeSpan.TicksPerMinute);
        if (ticks > 0)
        {
            foreach (var (intervalStart, intervalEnd) in IntervalsOnward(start, until: null))
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
            foreach (var (intervalStart, intervalEnd) in IntervalsBackward(start, until: null))
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

        throw new UnreachableException("Interval enumeration is unbounded and throws on dead calendars.");
    }

    /// <summary>Signed working minutes between two instants.</summary>
    public decimal WorkBetween(DateTime from, DateTime to)
    {
        if (to < from)
        {
            return -WorkBetween(to, from);
        }

        var totalTicks = 0L;
        foreach (var (start, end) in IntervalsOnward(from, until: to))
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
    private IEnumerable<(DateTime Start, DateTime End)> IntervalsOnward(DateTime from, DateTime? until)
    {
        var date = DateOnly.FromDateTime(from);
        var lastDate = until is { } u ? DateOnly.FromDateTime(u) : DateOnly.MaxValue;
        var idleDays = 0;
        while (date <= lastDate)
        {
            var schedule = GetDaySchedule(date);
            if (schedule.IsWorking)
            {
                idleDays = 0;
                var midnight = date.ToDateTime(System.TimeOnly.MinValue);
                foreach (var interval in schedule.Intervals)
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
                throw NoWorkingTime();
            }

            if (date == DateOnly.MaxValue)
            {
                yield break;
            }

            date = date.AddDays(1);
        }
    }

    /// <summary>Working intervals from <paramref name="from"/> backward, skipping intervals that start at or after it.</summary>
    private IEnumerable<(DateTime Start, DateTime End)> IntervalsBackward(DateTime from, DateTime? until)
    {
        var date = DateOnly.FromDateTime(from);
        var lastDate = until is { } u ? DateOnly.FromDateTime(u) : DateOnly.MinValue;
        var idleDays = 0;
        while (date >= lastDate)
        {
            var schedule = GetDaySchedule(date);
            if (schedule.IsWorking)
            {
                idleDays = 0;
                var midnight = date.ToDateTime(System.TimeOnly.MinValue);
                var intervals = schedule.Intervals;
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
                throw NoWorkingTime();
            }

            if (date == DateOnly.MinValue)
            {
                yield break;
            }

            date = date.AddDays(-1);
        }
    }

    private InvalidOperationException NoWorkingTime()
        => new($"Calendar '{Name}' has no working time within {MaxNonWorkingScanDays} days of the requested date.");
}
