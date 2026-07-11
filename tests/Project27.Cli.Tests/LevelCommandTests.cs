using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class LevelCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public LevelCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("resource", "add", "Dev", "--rate", "50/h", "--file", _file);
        Cli.Ok("task", "add", "A", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "B", "-d", "2d", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("assign", "add", "2", "Dev", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Level_run_serializes_the_conflict_and_clear_undoes_it()
    {
        var result = Cli.Ok("level", "run", "--json", "--file", _file).Json();
        var delays = result.GetProperty("delays");
        Assert.Equal(1, delays.GetArrayLength());
        Assert.Equal("B", delays[0].GetProperty("name").GetString());
        Assert.Equal(0, result.GetProperty("remainingOverallocations").GetArrayLength());

        var b = Cli.Ok("task", "show", "2", "--json", "--file", _file).Json();
        Assert.Equal("2026-01-07T08:00:00", b.GetProperty("start").GetString());

        Cli.Ok("level", "clear", "--file", _file);
        var reset = Cli.Ok("task", "show", "2", "--json", "--file", _file).Json();
        Assert.Equal("2026-01-05T08:00:00", reset.GetProperty("start").GetString());
    }

    [Fact]
    public void Leveling_delay_shows_in_views()
    {
        Cli.Ok("level", "run", "--file", _file);
        var view = Cli.Ok("view", "--fields", "name,levelingDelay", "--json", "--file", _file).Json();
        var row = view.GetProperty("groups")[0].GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("values").GetProperty("levelingDelay").GetDecimal() > 0);
        Assert.Equal(2, row.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Drivers_explain_the_leveled_task()
    {
        Cli.Ok("level", "run", "--file", _file);
        var output = Cli.Ok("task", "drivers", "2", "--file", _file).Stdout;
        Assert.Contains("Leveling delay", output, StringComparison.Ordinal);
        Assert.Contains("* ", output, StringComparison.Ordinal);

        var first = Cli.Ok("task", "drivers", "1", "--json", "--file", _file).Json();
        Assert.Contains(
            first.EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "ProjectStart" && d.GetProperty("binding").GetBoolean());
    }

    [Fact]
    public void Resource_import_copies_from_another_file()
    {
        var poolFile = _dir.File("pool.p27");
        Cli.Ok("init", "Pool", "--file", poolFile);
        Cli.Ok("resource", "add", "QA", "--rate", "40/h", "--max-units", "200%", "--file", poolFile);
        Cli.Ok("resource", "add", "Dev", "--rate", "99/h", "--file", poolFile); // clashes

        var result = Cli.Ok("resource", "import", "--from", poolFile, "--json", "--file", _file).Json();
        Assert.Equal(1, result.GetProperty("imported").GetInt32());
        Assert.Equal("Dev", result.GetProperty("skipped")[0].GetString());

        var qa = Cli.Ok("resource", "show", "QA", "--json", "--file", _file).Json();
        Assert.Equal("200%", qa.GetProperty("maxUnits").GetString());
        Assert.Equal("40/h", qa.GetProperty("rate").GetString());
    }
}
