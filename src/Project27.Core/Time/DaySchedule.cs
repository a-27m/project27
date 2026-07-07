using System.Collections.Immutable;

namespace Project27.Core.Time;

/// <summary>
/// The working times of one day: an ordered set of non-overlapping intervals
/// (they may touch). The default value is an explicitly non-working day.
/// </summary>
public readonly struct DaySchedule : IEquatable<DaySchedule>
{
    private readonly ImmutableArray<TimeInterval> _intervals;

    private DaySchedule(ImmutableArray<TimeInterval> intervals) => _intervals = intervals;

    /// <summary>An explicitly non-working day (distinct from "inherit", which is a null <c>DaySchedule?</c>).</summary>
    public static DaySchedule NonWorking => default;

    public static DaySchedule Working(params ReadOnlySpan<TimeInterval> intervals)
    {
        if (intervals.IsEmpty)
        {
            throw new ArgumentException("A working day needs at least one interval; use DaySchedule.NonWorking for days off.", nameof(intervals));
        }

        var sorted = intervals.ToArray();
        Array.Sort(sorted, static (a, b) => a.StartMinute.CompareTo(b.StartMinute));
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].StartMinute < sorted[i - 1].EndMinute)
            {
                throw new ArgumentException($"Working intervals overlap: {sorted[i - 1]} and {sorted[i]}.", nameof(intervals));
            }
        }

        return new DaySchedule(ImmutableArray.Create(sorted));
    }

    public bool IsWorking => !Intervals.IsEmpty;

    public ImmutableArray<TimeInterval> Intervals => _intervals.IsDefault ? [] : _intervals;

    public int WorkingMinutes
    {
        get
        {
            var total = 0;
            foreach (var interval in Intervals)
            {
                total += interval.WorkingMinutes;
            }

            return total;
        }
    }

    public bool Equals(DaySchedule other) => Intervals.SequenceEqual(other.Intervals);

    public override bool Equals(object? obj) => obj is DaySchedule other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var interval in Intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(DaySchedule left, DaySchedule right) => left.Equals(right);

    public static bool operator !=(DaySchedule left, DaySchedule right) => !left.Equals(right);

    public override string ToString() => IsWorking ? string.Join(", ", Intervals) : "non-working";
}
