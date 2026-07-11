using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class UsageCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public UsageCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("resource", "add", "Dev", "--rate", "60/h", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Daily_grid_shows_hours_per_working_day()
    {
        var output = Cli.Ok("usage", "-g", "day", "--file", _file).Stdout;
        Assert.Contains("01-05", output, StringComparison.Ordinal);
        Assert.Contains("8h", output, StringComparison.Ordinal);
        Assert.Contains("24h", output, StringComparison.Ordinal); // total
    }

    [Fact]
    public void Weekly_json_carries_raw_buckets()
    {
        var rows = Cli.Ok("usage", "--json", "--file", _file).Json();
        var build = rows.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Build");
        var bucket = build.GetProperty("buckets")[0];
        Assert.Equal("2026-01-05", bucket.GetProperty("date").GetString());
        Assert.Equal(1440m, bucket.GetProperty("workMinutes").GetDecimal());
        Assert.Equal(1440m, build.GetProperty("totalCost").GetDecimal()); // 24h × 60
    }

    [Fact]
    public void Assignment_breakdown_and_cost_mode()
    {
        var output = Cli.Ok("usage", "-g", "day", "--assignments", "--cost", "--file", _file).Stdout;
        Assert.Contains("Dev", output, StringComparison.Ordinal);
        Assert.Contains("480", output, StringComparison.Ordinal); // 8h × 60 per day
    }

    [Fact]
    public void Too_many_buckets_is_a_clean_error()
    {
        var result = Cli.Fail("usage", "-g", "day", "--from", "2026-01-01", "--to", "2026-12-31", "--file", _file);
        Assert.Contains("buckets", result.Stderr, StringComparison.Ordinal);
    }
}
