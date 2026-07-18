using Project27.Core.Time;

namespace Project27.Core;

/// <summary>
/// Working only where the underlying schedule works inside one of the given spans.
/// Keeps split-task assignment work inside the task's scheduled segments so
/// resource-calendar shaping and usage distribution skip the gaps (deviations.md #16).
/// </summary>
public sealed class ScheduleMask : IWorkSchedule
{
    private readonly IWorkSchedule _inner;
    private readonly IReadOnlyList<TaskSegment> _spans;

    public ScheduleMask(IWorkSchedule inner, IReadOnlyList<TaskSegment> spans)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(spans);
        _inner = inner;
        _spans = spans;
    }

    public string Name => $"{_inner.Name} (masked)";

    public DaySchedule GetDaySchedule(DateOnly day)
    {
        var basis = _inner.GetDaySchedule(day);
        if (!basis.IsWorking)
        {
            return DaySchedule.NonWorking;
        }

        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var clipped = new List<TimeInterval>();
        foreach (var span in _spans)
        {
            // The span ∩ day window, in whole minutes of the day (the engine is minute-based).
            var lower = Math.Max(0d, (span.Start - dayStart).TotalMinutes);
            var upper = Math.Min(1440d, (span.Finish - dayStart).TotalMinutes);
            if (upper <= lower)
            {
                continue;
            }

            var lo = (int)Math.Ceiling(lower);
            var hi = (int)Math.Floor(upper);
            foreach (var interval in basis.Intervals)
            {
                var start = Math.Max(interval.StartMinute, lo);
                var end = Math.Min(interval.EndMinute, hi);
                if (start < end)
                {
                    clipped.Add(new TimeInterval(start, end));
                }
            }
        }

        return clipped.Count == 0
            ? DaySchedule.NonWorking
            : DaySchedule.Working([.. clipped.OrderBy(i => i.StartMinute)]);
    }
}
