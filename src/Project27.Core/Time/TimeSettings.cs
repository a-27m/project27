namespace Project27.Core.Time;

/// <summary>
/// Project-level settings that convert between duration units and working minutes,
/// plus defaults applied when users enter dates without a time component.
/// </summary>
public sealed class TimeSettings
{
    private int _minutesPerDay = 8 * 60;
    private int _minutesPerWeek = 40 * 60;
    private decimal _daysPerMonth = 20m;

    /// <summary>Working minutes that one "day" of duration represents. Default 480 (8 h).</summary>
    public int MinutesPerDay
    {
        get => _minutesPerDay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _minutesPerDay = value;
        }
    }

    /// <summary>Working minutes that one "week" of duration represents. Default 2400 (40 h).</summary>
    public int MinutesPerWeek
    {
        get => _minutesPerWeek;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _minutesPerWeek = value;
        }
    }

    /// <summary>Working days that one "month" of duration represents. Default 20.</summary>
    public decimal DaysPerMonth
    {
        get => _daysPerMonth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _daysPerMonth = value;
        }
    }

    /// <summary>First day of the week for display and week-based grouping. Default Monday (see deviations.md #1).</summary>
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Monday;

    /// <summary>Time assumed when a start date is entered without a time. Default 08:00.</summary>
    public TimeOnly DefaultStartTime { get; set; } = new(8, 0);

    /// <summary>Time assumed when a finish date is entered without a time. Default 17:00.</summary>
    public TimeOnly DefaultEndTime { get; set; } = new(17, 0);
}
