namespace Project27.Core.Time;

/// <summary>
/// Units in which a <see cref="Duration"/> is expressed. Working units are converted to
/// minutes through <see cref="TimeSettings"/>; elapsed units measure clock time and
/// ignore calendars entirely (day = 24 h, week = 168 h, month = 30 × 24 h).
/// </summary>
public enum DurationUnit
{
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
    ElapsedMinutes,
    ElapsedHours,
    ElapsedDays,
    ElapsedWeeks,
    ElapsedMonths,
}
