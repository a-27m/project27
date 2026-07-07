using System.Globalization;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests.Time;

public sealed class WorkingTimeTests
{
    // Week of 2026-01-05: Monday Jan 5 … Friday Jan 9; Standard hours 8-12, 13-17.
    private static DateTime At(string text)
        => DateTime.Parse(text, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-01-05 09:00", true)]
    [InlineData("2026-01-05 08:00", true)]   // start-inclusive
    [InlineData("2026-01-05 17:00", false)]  // end-exclusive
    [InlineData("2026-01-05 12:30", false)]  // lunch
    [InlineData("2026-01-03 10:00", false)]  // Saturday
    public void IsWorkingTime_honors_interval_boundaries(string time, bool expected)
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(expected, calendar.IsWorkingTime(At(time)));
    }

    [Theory]
    [InlineData("2026-01-05 09:00", "2026-01-05 09:00")] // already working
    [InlineData("2026-01-05 12:30", "2026-01-05 13:00")] // lunch → afternoon
    [InlineData("2026-01-05 17:00", "2026-01-06 08:00")] // day end → next morning
    [InlineData("2026-01-03 10:00", "2026-01-05 08:00")] // weekend → Monday
    public void NextWorkingTime_moves_forward_to_work(string from, string expected)
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(At(expected), calendar.NextWorkingTime(At(from)));
    }

    [Theory]
    [InlineData("2026-01-05 16:00", "2026-01-05 16:00")] // already working
    [InlineData("2026-01-05 17:30", "2026-01-05 17:00")] // after hours → day end
    [InlineData("2026-01-05 12:30", "2026-01-05 12:00")] // lunch → morning end
    [InlineData("2026-01-05 08:00", "2026-01-02 17:00")] // day start snaps to previous finish
    [InlineData("2026-01-04 10:00", "2026-01-02 17:00")] // Sunday → Friday finish
    public void PreviousWorkingTime_moves_backward_to_work(string from, string expected)
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(At(expected), calendar.PreviousWorkingTime(At(from)));
    }

    [Theory]
    [InlineData("2026-01-05 08:00", 480, "2026-01-05 17:00")]  // exactly one day → finish time
    [InlineData("2026-01-05 08:00", 481, "2026-01-06 08:01")]  // spills into next day
    [InlineData("2026-01-05 11:00", 120, "2026-01-05 14:00")]  // across lunch
    [InlineData("2026-01-09 16:00", 120, "2026-01-12 09:00")]  // across weekend
    [InlineData("2026-01-03 10:00", 60, "2026-01-05 09:00")]   // start on weekend
    [InlineData("2026-01-05 08:00", 0, "2026-01-05 08:00")]    // zero is identity
    [InlineData("2026-01-05 08:00", -60, "2026-01-02 16:00")]  // backward across weekend
    [InlineData("2026-01-06 08:00", -480, "2026-01-05 08:00")] // backward exactly one day → start time
    [InlineData("2026-01-05 14:00", -120, "2026-01-05 11:00")] // backward across lunch
    public void AddWork_walks_working_time(string start, int minutes, string expected)
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(At(expected), calendar.AddWork(At(start), minutes));
    }

    [Fact]
    public void AddWork_skips_holiday_exceptions()
    {
        var calendar = WorkCalendar.CreateStandard();
        calendar.AddException(new CalendarException("Holiday", new DateOnly(2026, 1, 6)));

        Assert.Equal(At("2026-01-07 09:00"), calendar.AddWork(At("2026-01-05 16:00"), 120));
    }

    [Theory]
    [InlineData("2026-01-05 08:00", "2026-01-06 17:00", 960)]  // two full days
    [InlineData("2026-01-05 11:00", "2026-01-05 14:00", 120)]  // across lunch
    [InlineData("2026-01-03 00:00", "2026-01-04 23:59", 0)]    // weekend only
    [InlineData("2026-01-09 16:00", "2026-01-12 09:00", 120)]  // across weekend
    public void WorkBetween_counts_working_minutes(string from, string to, int expected)
    {
        var calendar = WorkCalendar.CreateStandard();

        Assert.Equal(expected, calendar.WorkBetween(At(from), At(to)));
        Assert.Equal(-expected, calendar.WorkBetween(At(to), At(from)));
    }

    [Fact]
    public void Night_shift_spans_midnight()
    {
        var calendar = WorkCalendar.CreateNightShift();

        // Monday 23:00 + 8 h: 60 min Monday, 180 min (00-03), 240 min (04-08) → Tuesday 08:00.
        Assert.Equal(At("2026-01-06 08:00"), calendar.AddWork(At("2026-01-05 23:00"), 480));
        Assert.True(calendar.IsWorkingTime(At("2026-01-06 01:00")));
        Assert.False(calendar.IsWorkingTime(At("2026-01-06 03:30"))); // break
        Assert.False(calendar.IsWorkingTime(At("2026-01-05 10:00"))); // day time
    }

    [Fact]
    public void TwentyFourHours_calendar_is_clock_time()
    {
        var calendar = WorkCalendar.Create24Hours();

        Assert.Equal(At("2026-01-06 10:30"), calendar.AddWork(At("2026-01-05 10:30"), 1440));
        Assert.Equal(1440m, calendar.WorkBetween(At("2026-01-03 00:00"), At("2026-01-04 00:00")));
    }

    [Fact]
    public void Fractional_minutes_are_preserved()
    {
        var calendar = WorkCalendar.CreateStandard();

        var result = calendar.AddWork(At("2026-01-05 08:00"), 0.5m);

        Assert.Equal(At("2026-01-05 08:00").AddSeconds(30), result);
    }

    [Fact]
    public void Dead_calendar_work_between_is_zero_but_seek_throws()
    {
        var dead = new WorkCalendar("Empty");

        Assert.Equal(0m, dead.WorkBetween(At("2026-01-05 08:00"), At("2026-03-05 08:00")));
        Assert.Throws<InvalidOperationException>(() => dead.NextWorkingTime(At("2026-01-05 08:00")));
        Assert.Throws<InvalidOperationException>(() => dead.AddWork(At("2026-01-05 08:00"), 60));
    }
}
