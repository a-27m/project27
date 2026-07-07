using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests.Time;

public sealed class CalendarResolutionTests
{
    private static readonly DaySchedule StandardDay = DaySchedule.Working(
        new TimeInterval(8 * 60, 12 * 60),
        new TimeInterval(13 * 60, 17 * 60));

    // 2026-01-05 is a Monday.
    private static DateOnly Day(int day) => new(2026, 1, day);

    [Fact]
    public void Standard_calendar_works_weekdays_only()
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(StandardDay, calendar.GetDaySchedule(Day(5)));
        Assert.Equal(480, calendar.GetDaySchedule(Day(9)).WorkingMinutes);
        Assert.False(calendar.GetDaySchedule(Day(3)).IsWorking); // Saturday
        Assert.False(calendar.GetDaySchedule(Day(4)).IsWorking); // Sunday
    }

    [Fact]
    public void Exception_overrides_default_week()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException("Holiday", Day(6)));

        Assert.False(calendar.GetDaySchedule(Day(6)).IsWorking);
        Assert.True(calendar.GetDaySchedule(Day(7)).IsWorking);
    }

    [Fact]
    public void Range_exception_covers_all_days_including_weekend_override()
    {
        var calendar = WorkCalendar.CreateStandard();
        var halfDay = DaySchedule.Working(new TimeInterval(9 * 60, 12 * 60));
        calendar.AddException(new CalendarException("Inventory", Day(8), Day(10), halfDay));

        Assert.Equal(halfDay, calendar.GetDaySchedule(Day(8)));  // Thursday
        Assert.Equal(halfDay, calendar.GetDaySchedule(Day(10))); // Saturday becomes working
        Assert.True(calendar.GetDaySchedule(Day(7)).IsWorking);
        Assert.False(calendar.GetDaySchedule(Day(11)).IsWorking);
    }

    [Fact]
    public void Base_calendar_exception_shows_through_derived_calendar()
    {
        var standard = WorkCalendar.CreateStandard();
        var resource = new WorkCalendar("Alice", baseCalendar: standard);
        standard.AddException(new CalendarException("Company holiday", Day(6)));

        Assert.False(resource.GetDaySchedule(Day(6)).IsWorking);
        Assert.Equal(StandardDay, resource.GetDaySchedule(Day(5)));
    }

    [Fact]
    public void Derived_exception_beats_base_exception()
    {
        var standard = WorkCalendar.CreateStandard();
        standard.AddException(new CalendarException("Company holiday", Day(6)));
        var onCall = DaySchedule.Working(new TimeInterval(9 * 60, 11 * 60));
        var resource = new WorkCalendar("Bob", baseCalendar: standard);
        resource.AddException(new CalendarException("On call", Day(6), schedule: onCall));

        Assert.Equal(onCall, resource.GetDaySchedule(Day(6)));
    }

    [Fact]
    public void Derived_default_week_edit_overrides_base_but_not_base_exceptions()
    {
        var standard = WorkCalendar.CreateStandard();
        var resource = new WorkCalendar("Carol", baseCalendar: standard);
        resource.SetDay(DayOfWeek.Friday, DaySchedule.NonWorking); // four-day week

        Assert.False(resource.GetDaySchedule(Day(9)).IsWorking);   // her Friday off
        Assert.True(standard.GetDaySchedule(Day(9)).IsWorking);    // base unaffected

        standard.AddException(new CalendarException("Company holiday", Day(5)));
        Assert.False(resource.GetDaySchedule(Day(5)).IsWorking);   // base holiday shows through
    }

    [Fact]
    public void Work_week_applies_only_inside_its_range()
    {
        var calendar = WorkCalendar.CreateStandard();
        var summerFriday = WeeklyPattern.InheritAll.With(DayOfWeek.Friday, DaySchedule.NonWorking);
        calendar.AddWorkWeek(new WorkWeek("Short week", Day(12), Day(16), summerFriday));

        Assert.False(calendar.GetDaySchedule(Day(16)).IsWorking); // Friday in range
        Assert.True(calendar.GetDaySchedule(Day(9)).IsWorking);   // Friday before range
        Assert.Equal(StandardDay, calendar.GetDaySchedule(Day(13))); // undefined day falls through
    }

    [Fact]
    public void Weekly_recurrence_with_occurrence_count()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException(
            "Biweekly maintenance",
            Day(2), // Friday
            schedule: DaySchedule.NonWorking,
            recurrence: new WeeklyRecurrence(2, DayOfWeekSet.Friday),
            occurrences: 3));

        Assert.False(calendar.GetDaySchedule(Day(2)).IsWorking);
        Assert.True(calendar.GetDaySchedule(Day(9)).IsWorking);            // off week
        Assert.False(calendar.GetDaySchedule(Day(16)).IsWorking);
        Assert.False(calendar.GetDaySchedule(Day(30)).IsWorking);          // third occurrence
        Assert.True(calendar.GetDaySchedule(new DateOnly(2026, 2, 13)).IsWorking); // count exhausted
    }

    [Fact]
    public void Monthly_day_recurrence_clamps_to_month_end()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException(
            "Closing day",
            Day(1),
            new DateOnly(2026, 12, 31),
            recurrence: new MonthlyDayRecurrence(31, 1)));

        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 1, 31)).IsWorking);
        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 2, 28)).IsWorking); // clamped
        Assert.True(calendar.GetDaySchedule(new DateOnly(2026, 2, 27)).IsWorking);
        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 3, 31)).IsWorking);
    }

    [Fact]
    public void Yearly_weekday_recurrence_finds_nth_weekday()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException(
            "Thanksgiving",
            Day(1),
            new DateOnly(2030, 12, 31),
            recurrence: new YearlyWeekdayRecurrence(WeekOrdinal.Fourth, DayOfWeek.Thursday, 11)));

        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 11, 26)).IsWorking);
        Assert.False(calendar.GetDaySchedule(new DateOnly(2027, 11, 25)).IsWorking);
        Assert.True(calendar.GetDaySchedule(new DateOnly(2026, 11, 19)).IsWorking);
    }

    [Fact]
    public void Monthly_last_weekday_recurrence()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException(
            "Retro",
            Day(1),
            new DateOnly(2026, 6, 30),
            recurrence: new MonthlyWeekdayRecurrence(WeekOrdinal.Last, DayOfWeek.Friday, 1)));

        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 1, 30)).IsWorking);
        Assert.False(calendar.GetDaySchedule(new DateOnly(2026, 2, 27)).IsWorking);
        Assert.True(calendar.GetDaySchedule(new DateOnly(2026, 1, 23)).IsWorking);
    }

    [Fact]
    public void Mutations_invalidate_cached_day_resolution()
    {
        var calendar = WorkCalendar.CreateStandard();
        Assert.True(calendar.GetDaySchedule(Day(6)).IsWorking);

        calendar.AddException(new CalendarException("Holiday", Day(6)));
        Assert.False(calendar.GetDaySchedule(Day(6)).IsWorking);
    }

    [Fact]
    public void Base_calendar_mutation_invalidates_derived_cache()
    {
        var standard = WorkCalendar.CreateStandard();
        var resource = new WorkCalendar("Dave", baseCalendar: standard);
        Assert.True(resource.GetDaySchedule(Day(6)).IsWorking);

        standard.AddException(new CalendarException("Holiday", Day(6)));
        Assert.False(resource.GetDaySchedule(Day(6)).IsWorking);
    }

    [Fact]
    public void Base_calendar_cycles_are_rejected()
    {
        var a = WorkCalendar.CreateStandard("A");
        var b = new WorkCalendar("B", baseCalendar: a);

        Assert.Throws<InvalidOperationException>(() => a.SetBaseCalendar(b));
    }

    [Fact]
    public void Overlapping_intervals_are_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => DaySchedule.Working(new TimeInterval(8 * 60, 12 * 60), new TimeInterval(11 * 60, 17 * 60)));
    }

    [Fact]
    public void Touching_intervals_are_allowed()
    {
        var schedule = DaySchedule.Working(new TimeInterval(8 * 60, 12 * 60), new TimeInterval(12 * 60, 17 * 60));

        Assert.Equal(9 * 60, schedule.WorkingMinutes);
    }
}
