namespace Project27.Core.Time;

/// <summary>Days-of-week set for weekly recurrences.</summary>
[Flags]
public enum DayOfWeekSet
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
}

public static class DayOfWeekSetExtensions
{
    public static bool Contains(this DayOfWeekSet set, DayOfWeek day) => (set & day.AsSet()) != 0;

    public static DayOfWeekSet AsSet(this DayOfWeek day) => (DayOfWeekSet)(1 << (int)day);
}

/// <summary>Which occurrence of a weekday within a month.</summary>
public enum WeekOrdinal
{
    First = 1,
    Second = 2,
    Third = 3,
    Fourth = 4,
    Last = 5,
}

/// <summary>
/// A recurrence pattern for calendar exceptions. Occurrence enumeration is bounded by
/// the caller-supplied window; count limits are applied by <see cref="CalendarException"/>.
/// </summary>
public abstract record Recurrence
{
    private protected Recurrence()
    {
    }

    internal abstract IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until);

    private protected static DateOnly NthWeekdayOfMonth(int year, int month, WeekOrdinal ordinal, DayOfWeek weekday)
    {
        if (ordinal == WeekOrdinal.Last)
        {
            var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var back = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
            return last.AddDays(-back);
        }

        var first = new DateOnly(year, month, 1);
        var forward = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(forward + 7 * ((int)ordinal - 1));
    }

    private protected static DateOnly ClampedDate(int year, int month, int day)
        => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}

/// <summary>Every N days.</summary>
public sealed record DailyRecurrence : Recurrence
{
    public DailyRecurrence(int everyDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everyDays, 1);
        EveryDays = everyDays;
    }

    public int EveryDays { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        for (var date = from; date <= until; date = date.AddDays(EveryDays))
        {
            yield return date;
        }
    }
}

/// <summary>Selected weekdays, every N weeks. Weeks are anchored on Monday (deviations.md #2).</summary>
public sealed record WeeklyRecurrence : Recurrence
{
    public WeeklyRecurrence(int everyWeeks, DayOfWeekSet days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everyWeeks, 1);
        if (days == DayOfWeekSet.None)
        {
            throw new ArgumentException("A weekly recurrence needs at least one weekday.", nameof(days));
        }

        EveryWeeks = everyWeeks;
        Days = days;
    }

    public int EveryWeeks { get; }

    public DayOfWeekSet Days { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        var anchor = StartOfWeek(from);
        for (var date = from; date <= until; date = date.AddDays(1))
        {
            if (!Days.Contains(date.DayOfWeek))
            {
                continue;
            }

            var weeks = (StartOfWeek(date).DayNumber - anchor.DayNumber) / 7;
            if (weeks % EveryWeeks == 0)
            {
                yield return date;
            }
        }
    }

    private static DateOnly StartOfWeek(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
}

/// <summary>Day N of the month, every N months. Day 31 clamps to shorter months (deviations.md #3).</summary>
public sealed record MonthlyDayRecurrence : Recurrence
{
    public MonthlyDayRecurrence(int day, int everyMonths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 31);
        ArgumentOutOfRangeException.ThrowIfLessThan(everyMonths, 1);
        Day = day;
        EveryMonths = everyMonths;
    }

    public int Day { get; }

    public int EveryMonths { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        for (var cursor = new DateOnly(from.Year, from.Month, 1); cursor <= until; cursor = cursor.AddMonths(EveryMonths))
        {
            var date = ClampedDate(cursor.Year, cursor.Month, Day);
            if (date >= from && date <= until)
            {
                yield return date;
            }
        }
    }
}

/// <summary>The Nth weekday of the month, every N months (e.g. last Friday every 2 months).</summary>
public sealed record MonthlyWeekdayRecurrence : Recurrence
{
    public MonthlyWeekdayRecurrence(WeekOrdinal ordinal, DayOfWeek weekday, int everyMonths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(everyMonths, 1);
        Ordinal = ordinal;
        Weekday = weekday;
        EveryMonths = everyMonths;
    }

    public WeekOrdinal Ordinal { get; }

    public DayOfWeek Weekday { get; }

    public int EveryMonths { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        for (var cursor = new DateOnly(from.Year, from.Month, 1); cursor <= until; cursor = cursor.AddMonths(EveryMonths))
        {
            var date = NthWeekdayOfMonth(cursor.Year, cursor.Month, Ordinal, Weekday);
            if (date >= from && date <= until)
            {
                yield return date;
            }
        }
    }
}

/// <summary>A fixed month/day every year (Feb 29 clamps to Feb 28 off leap years).</summary>
public sealed record YearlyDateRecurrence : Recurrence
{
    public YearlyDateRecurrence(int month, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 31);
        Month = month;
        Day = day;
    }

    public int Month { get; }

    public int Day { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        for (var year = from.Year; year <= until.Year; year++)
        {
            var date = ClampedDate(year, Month, Day);
            if (date >= from && date <= until)
            {
                yield return date;
            }
        }
    }
}

/// <summary>The Nth weekday of a fixed month every year (e.g. fourth Thursday of November).</summary>
public sealed record YearlyWeekdayRecurrence : Recurrence
{
    public YearlyWeekdayRecurrence(WeekOrdinal ordinal, DayOfWeek weekday, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        Ordinal = ordinal;
        Weekday = weekday;
        Month = month;
    }

    public WeekOrdinal Ordinal { get; }

    public DayOfWeek Weekday { get; }

    public int Month { get; }

    internal override IEnumerable<DateOnly> Occurrences(DateOnly from, DateOnly until)
    {
        for (var year = from.Year; year <= until.Year; year++)
        {
            var date = NthWeekdayOfMonth(year, Month, Ordinal, Weekday);
            if (date >= from && date <= until)
            {
                yield return date;
            }
        }
    }
}
