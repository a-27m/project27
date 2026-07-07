using System.Globalization;

namespace Project27.Core.Time;

/// <summary>
/// A working interval within a single day, expressed in minutes from midnight.
/// Start-inclusive, end-exclusive; end may be 1440 (midnight of the next day).
/// </summary>
public readonly record struct TimeInterval
{
    public const int MinutesPerDay = 24 * 60;

    public TimeInterval(int startMinute, int endMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startMinute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endMinute, MinutesPerDay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endMinute, startMinute);
        StartMinute = startMinute;
        EndMinute = endMinute;
    }

    public int StartMinute { get; }

    public int EndMinute { get; }

    public int WorkingMinutes => EndMinute - StartMinute;

    /// <summary>Creates an interval from times of day; an end of 00:00 means end of day (24:00).</summary>
    public static TimeInterval FromTimes(TimeOnly start, TimeOnly end)
    {
        var endMinute = end == TimeOnly.MinValue ? MinutesPerDay : (int)(end.Ticks / TimeSpan.TicksPerMinute);
        return new TimeInterval((int)(start.Ticks / TimeSpan.TicksPerMinute), endMinute);
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{StartMinute / 60:00}:{StartMinute % 60:00}-{EndMinute / 60:00}:{EndMinute % 60:00}");
}
