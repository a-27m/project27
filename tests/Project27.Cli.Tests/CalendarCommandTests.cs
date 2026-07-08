using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class CalendarCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public CalendarCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    private JsonElement Calendar(string name)
        => Cli.Ok("calendar", "show", name, "--json", "--file", _file).Json();

    [Fact]
    public void Add_derived_calendar_and_override_a_day()
    {
        Cli.Ok("calendar", "add", "Ops", "--base", "Standard", "--file", _file);
        Cli.Ok("calendar", "set-day", "Ops", "sat", "08:00-12:00", "--file", _file);

        var ops = Calendar("Ops");
        Assert.Equal("Standard", ops.GetProperty("base").GetString());
        var saturday = ops.GetProperty("week").EnumerateArray().Single(d => d.GetProperty("day").GetString() == "sat");
        Assert.Equal("08:00-12:00", saturday.GetProperty("hours").GetString());
        var monday = ops.GetProperty("week").EnumerateArray().Single(d => d.GetProperty("day").GetString() == "mon");
        Assert.Equal("inherit", monday.GetProperty("hours").GetString());

        Cli.Ok("calendar", "set-day", "Ops", "sat", "inherit", "--file", _file);
        Assert.Equal(
            "inherit",
            Calendar("Ops").GetProperty("week").EnumerateArray()
                .Single(d => d.GetProperty("day").GetString() == "sat").GetProperty("hours").GetString());
    }

    [Fact]
    public void Holiday_exception_pushes_work_out()
    {
        // Tue 2026-01-06 off: a 2d task starting Mon finishes Wed.
        Cli.Ok("calendar", "add-exception", "Standard", "Holiday", "--from", "2026-01-06", "--file", _file);
        Cli.Ok("task", "add", "Work", "-d", "2d", "--file", _file);
        var task = Cli.Ok("task", "show", "1", "--json", "--file", _file).Json();
        Assert.Equal("2026-01-07T17:00:00", task.GetProperty("finish").GetString());

        Cli.Ok("calendar", "remove-exception", "Standard", "Holiday", "--file", _file);
        task = Cli.Ok("task", "show", "1", "--json", "--file", _file).Json();
        Assert.Equal("2026-01-06T17:00:00", task.GetProperty("finish").GetString());
    }

    [Fact]
    public void Recurring_exception_roundtrips_spec_syntax()
    {
        Cli.Ok(
            "calendar", "add-exception", "Standard", "Xmas", "--file", _file,
            "--from", "2026-12-25", "--recur", "yearly-date:12-25", "--times", "5");
        var exception = Calendar("Standard").GetProperty("exceptions")[0];
        Assert.Equal("yearly-date:12-25", exception.GetProperty("recur").GetString());
        Assert.Equal(5, exception.GetProperty("times").GetInt32());
        Assert.Equal("off", exception.GetProperty("hours").GetString());
    }

    [Fact]
    public void Task_calendar_assignment_changes_scheduling()
    {
        Cli.Ok("calendar", "add", "AllHours", "--preset", "24h", "--file", _file);
        Cli.Ok("task", "add", "Work", "-d", "1d", "--file", _file);
        Cli.Ok("task", "set", "1", "--calendar", "AllHours", "--file", _file);
        var task = Cli.Ok("task", "show", "1", "--json", "--file", _file).Json();
        Assert.Equal("AllHours", task.GetProperty("calendar").GetString());
        // 480 working minutes on a 24h calendar: same-day 08:00 -> 16:00.
        Assert.Equal("2026-01-05T16:00:00", task.GetProperty("finish").GetString());
    }

    [Fact]
    public void Work_week_overrides_a_date_range()
    {
        Cli.Ok(
            "calendar", "add-workweek", "Standard", "Crunch", "--file", _file,
            "--from", "2026-02-02", "--to", "2026-02-08", "--sat", "08:00-12:00,13:00-17:00");
        var week = Calendar("Standard").GetProperty("workWeeks")[0];
        Assert.Equal("Crunch", week.GetProperty("name").GetString());
        var saturday = week.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("day").GetString() == "sat");
        Assert.Equal("08:00-12:00,13:00-17:00", saturday.GetProperty("hours").GetString());

        Cli.Ok("calendar", "remove-workweek", "Standard", "Crunch", "--file", _file);
        Assert.Equal(0, Calendar("Standard").GetProperty("workWeeks").GetArrayLength());
    }

    [Fact]
    public void Project_calendar_cannot_be_removed()
    {
        Cli.Fail("calendar", "remove", "Standard", "--file", _file);

        Cli.Ok("calendar", "add", "Spare", "--file", _file);
        Cli.Ok("calendar", "remove", "Spare", "--file", _file);
        Assert.Equal(1, Cli.Ok("calendar", "list", "--json", "--file", _file).Json().GetArrayLength());
    }

    [Fact]
    public void Exception_hours_reject_inherit()
    {
        Assert.Contains(
            "inherit",
            Cli.Fail(
                "calendar", "add-exception", "Standard", "Bad", "--file", _file,
                "--from", "2026-01-06", "--hours", "inherit").Stderr,
            StringComparison.Ordinal);
    }
}
