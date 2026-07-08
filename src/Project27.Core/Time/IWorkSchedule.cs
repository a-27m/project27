namespace Project27.Core.Time;

/// <summary>
/// A source of per-day working schedules. <see cref="WorkCalendar"/> is the primary
/// implementation; <see cref="ScheduleIntersection"/> combines two sources for
/// assignment scheduling (task calendar × resource calendar). All working-time
/// arithmetic (<see cref="WorkScheduleArithmetic"/>) operates on this interface.
/// </summary>
public interface IWorkSchedule
{
    public string Name { get; }

    public DaySchedule GetDaySchedule(DateOnly day);
}

/// <summary>Working where — and only where — both underlying schedules are working.</summary>
public sealed class ScheduleIntersection : IWorkSchedule
{
    private readonly IWorkSchedule _first;
    private readonly IWorkSchedule _second;

    public ScheduleIntersection(IWorkSchedule first, IWorkSchedule second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        _first = first;
        _second = second;
    }

    public string Name => $"{_first.Name} + {_second.Name}";

    public DaySchedule GetDaySchedule(DateOnly day)
    {
        var a = _first.GetDaySchedule(day);
        if (!a.IsWorking)
        {
            return DaySchedule.NonWorking;
        }

        var b = _second.GetDaySchedule(day);
        if (!b.IsWorking)
        {
            return DaySchedule.NonWorking;
        }

        var overlaps = new List<TimeInterval>();
        foreach (var first in a.Intervals)
        {
            foreach (var second in b.Intervals)
            {
                var start = Math.Max(first.StartMinute, second.StartMinute);
                var end = Math.Min(first.EndMinute, second.EndMinute);
                if (start < end)
                {
                    overlaps.Add(new TimeInterval(start, end));
                }
            }
        }

        return overlaps.Count == 0
            ? DaySchedule.NonWorking
            : DaySchedule.Working([.. overlaps.OrderBy(i => i.StartMinute)]);
    }
}
