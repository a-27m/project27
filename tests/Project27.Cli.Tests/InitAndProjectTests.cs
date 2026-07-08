using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class InitAndProjectTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string ProjectFile => _dir.File("alpha.p27");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Init_creates_file_and_reports_path()
    {
        var result = Cli.Ok("init", "Alpha", "--start", "2026-01-05", "--file", ProjectFile);
        Assert.True(File.Exists(ProjectFile));
        Assert.Contains("created", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_refuses_to_overwrite()
    {
        Cli.Ok("init", "Alpha", "--file", ProjectFile);
        var result = Cli.Fail("init", "Alpha", "--file", ProjectFile);
        Assert.Contains("already exists", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_show_reports_defaults()
    {
        Cli.Ok("init", "Alpha", "--start", "2026-01-05", "--file", ProjectFile);
        var json = Cli.Ok("project", "show", "--json", "--file", ProjectFile).Json();
        Assert.Equal("Alpha", json.GetProperty("name").GetString());
        Assert.Equal("2026-01-05T08:00:00", json.GetProperty("start").GetString());
        Assert.Equal("projectStart", json.GetProperty("scheduleFrom").GetString());
        Assert.Equal("Standard", json.GetProperty("calendar").GetString());
        Assert.Equal(480, json.GetProperty("minutesPerDay").GetInt32());
        Assert.Equal("monday", json.GetProperty("weekStartsOn").GetString());
        Assert.Equal("08:00", json.GetProperty("dayStart").GetString());
    }

    [Fact]
    public void Project_set_updates_settings_and_persists()
    {
        Cli.Ok("init", "Alpha", "--start", "2026-01-05", "--file", ProjectFile);
        Cli.Ok(
            "project", "set", "--file", ProjectFile,
            "--name", "Beta", "--minutes-per-day", "420", "--day-start", "07:00", "--critical-slack", "1d");
        var json = Cli.Ok("project", "show", "--json", "--file", ProjectFile).Json();
        Assert.Equal("Beta", json.GetProperty("name").GetString());
        Assert.Equal(420, json.GetProperty("minutesPerDay").GetInt32());
        Assert.Equal("07:00", json.GetProperty("dayStart").GetString());
        Assert.Equal("1d", json.GetProperty("criticalSlack").GetString());
    }

    [Fact]
    public void Project_set_rejects_unknown_schedule_from()
    {
        Cli.Ok("init", "Alpha", "--file", ProjectFile);
        Assert.Contains(
            "--schedule-from",
            Cli.Fail("project", "set", "--schedule-from", "middle", "--file", ProjectFile).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_recalc_reports_span()
    {
        Cli.Ok("init", "Alpha", "--start", "2026-01-05", "--file", ProjectFile);
        Cli.Ok("task", "add", "Work", "-d", "3d", "--file", ProjectFile);
        var result = Cli.Ok("schedule", "recalc", "--file", ProjectFile);
        Assert.Contains("2026-01-05 08:00 -> 2026-01-07 17:00", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_is_a_clean_error()
    {
        var result = Cli.Fail("task", "list", "--file", _dir.File("nope.p27"));
        Assert.Contains("not found", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_file_resolution_requires_exactly_one_candidate()
    {
        Assert.Throws<CliException>(() => CliContext.ResolveFile(null, _dir.Path));

        Cli.Ok("init", "One", "--file", _dir.File("one.p27"));
        Assert.Equal(_dir.File("one.p27"), CliContext.ResolveFile(null, _dir.Path));

        Cli.Ok("init", "Two", "--file", _dir.File("two.p27"));
        Assert.Throws<CliException>(() => CliContext.ResolveFile(null, _dir.Path));

        Assert.Equal("explicit.p27", CliContext.ResolveFile("explicit.p27", _dir.Path));
    }

    [Fact]
    public void Json_errors_still_go_to_stderr_as_text()
    {
        var result = Cli.Fail("task", "list", "--json", "--file", _dir.File("nope.p27"));
        Assert.Equal("", result.Stdout);
        Assert.StartsWith("error:", result.Stderr, StringComparison.Ordinal); // plain text, not JSON
    }
}
