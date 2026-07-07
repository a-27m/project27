namespace Project27.Core.Time;

/// <summary>
/// Per-weekday schedules. A <c>null</c> entry means "inherit": fall through to the base
/// calendar (or non-working at the root). Immutable; use <see cref="With"/> to derive.
/// </summary>
public sealed class WeeklyPattern
{
    private readonly DaySchedule?[] _days;

    private WeeklyPattern(DaySchedule?[] days) => _days = days;

    /// <summary>A pattern that defines no day at all — every day inherits.</summary>
    public static WeeklyPattern InheritAll { get; } = new(new DaySchedule?[7]);

    public static WeeklyPattern Create(
        DaySchedule? sunday = null,
        DaySchedule? monday = null,
        DaySchedule? tuesday = null,
        DaySchedule? wednesday = null,
        DaySchedule? thursday = null,
        DaySchedule? friday = null,
        DaySchedule? saturday = null)
        => new([sunday, monday, tuesday, wednesday, thursday, friday, saturday]);

    public DaySchedule? this[DayOfWeek day] => _days[(int)day];

    public WeeklyPattern With(DayOfWeek day, DaySchedule? schedule)
    {
        var copy = (DaySchedule?[])_days.Clone();
        copy[(int)day] = schedule;
        return new WeeklyPattern(copy);
    }
}
