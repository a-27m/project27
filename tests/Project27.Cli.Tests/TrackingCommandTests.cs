using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class TrackingCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public TrackingCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("resource", "add", "Dev", "--rate", "50/h", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "4d", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    private JsonElement Task1()
        => Cli.Ok("task", "show", "1", "--json", "--file", _file).Json();

    [Fact]
    public void Baseline_set_show_and_clear()
    {
        Cli.Ok("baseline", "set", "--file", _file);
        var baseline = Task1().GetProperty("baseline");
        Assert.Equal("2026-01-05T08:00:00", baseline.GetProperty("start").GetString());
        Assert.Equal(1600m, baseline.GetProperty("cost").GetDecimal());

        Cli.Ok("baseline", "clear", "--file", _file);
        Assert.Equal(JsonValueKind.Null, Task1().GetProperty("baseline").ValueKind);
    }

    [Fact]
    public void Percent_complete_flows_through_set_and_list()
    {
        Cli.Ok("task", "set", "1", "--percent-complete", "50", "--file", _file);
        var task = Task1();
        Assert.Equal(50, task.GetProperty("percentComplete").GetInt32());
        Assert.Equal("2026-01-05T08:00:00", task.GetProperty("actualStart").GetString());

        Assert.Contains("50%", Cli.Ok("task", "list", "--file", _file).Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Actual_finish_completes_and_can_be_reopened()
    {
        Cli.Ok("task", "set", "1", "--actual-start", "2026-01-05", "--actual-finish", "2026-01-09", "--file", _file);
        var task = Task1();
        Assert.Equal(100, task.GetProperty("percentComplete").GetInt32());
        Assert.Equal("2026-01-09T17:00:00", task.GetProperty("finish").GetString());

        Cli.Ok("task", "set", "1", "--percent-complete", "75", "--file", _file);
        Assert.Equal(JsonValueKind.Null, Task1().GetProperty("actualFinish").ValueKind);
    }

    [Fact]
    public void Evm_table_reports_at_the_status_date()
    {
        Cli.Ok("baseline", "set", "--file", _file);
        Cli.Ok("task", "set", "1", "--percent-complete", "25", "--file", _file);
        Cli.Ok("project", "set", "--status-date", "2026-01-07 08:00", "--file", _file);

        var evm = Cli.Ok("task", "evm", "1", "--json", "--file", _file).Json()[0];
        Assert.Equal(1600m, evm.GetProperty("bac").GetDecimal());
        Assert.Equal(800m, evm.GetProperty("bcws").GetDecimal());
        Assert.Equal(400m, evm.GetProperty("bcwp").GetDecimal());
        Assert.Equal(0.5m, evm.GetProperty("spi").GetDecimal());

        var table = Cli.Ok("task", "evm", "--file", _file).Stdout;
        Assert.Contains("BCWS", table, StringComparison.Ordinal);
        Assert.Contains("Plan", table, StringComparison.Ordinal); // project rollup row
    }

    [Fact]
    public void Reschedule_pushes_remaining_work_past_the_status_date()
    {
        Cli.Ok("task", "set", "1", "--percent-complete", "25", "--file", _file);
        Cli.Ok("project", "set", "--status-date", "2026-01-08 08:00", "--file", _file);
        Cli.Ok("schedule", "reschedule", "--file", _file);

        var task = Task1();
        Assert.Equal(2, task.GetProperty("segments").GetArrayLength());
        Assert.Equal("2026-01-12T17:00:00", task.GetProperty("finish").GetString());
    }

    [Fact]
    public void Remaining_duration_extends_the_plan()
    {
        Cli.Ok("task", "set", "1", "--percent-complete", "50", "--file", _file);
        Cli.Ok("task", "set", "1", "--remaining-duration", "6d", "--file", _file);
        var task = Task1();
        Assert.Equal("8d", task.GetProperty("duration").GetString());
        Assert.Equal(25, task.GetProperty("percentComplete").GetInt32());
    }
}
