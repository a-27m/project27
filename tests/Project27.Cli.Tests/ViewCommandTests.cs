using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class ViewCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public ViewCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("task", "add", "Design", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", _file);
        Cli.Ok("task", "add", "Docs", "-d", "1d", "--file", _file);
        Cli.Ok("link", "add", "1", "2", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Default_view_is_the_entry_table()
    {
        var output = Cli.Ok("view", "--file", _file).Stdout;
        Assert.Contains("Predecessors", output, StringComparison.Ordinal);
        Assert.Contains("Build", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_sort_and_fields_compose()
    {
        var output = Cli.Ok(
            "view", "--fields", "id,name,duration", "--filter", "duration >= 2d",
            "--sort", "duration desc", "--file", _file).Stdout;
        var lines = output.Trim().Split('\n');
        Assert.Contains("Build", lines[2], StringComparison.Ordinal);
        Assert.Contains("Design", lines[3], StringComparison.Ordinal);
        Assert.DoesNotContain("Docs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Group_by_emits_headings_and_json_raw_values()
    {
        var json = Cli.Ok("view", "--fields", "name,duration", "--group-by", "critical", "--json", "--file", _file).Json();
        var groups = json.GetProperty("groups");
        Assert.Equal(2, groups.GetArrayLength());
        Assert.Contains("Critical", groups[0].GetProperty("heading").GetString(), StringComparison.Ordinal);
        var firstRow = groups[0].GetProperty("rows")[0];
        Assert.Equal(480m, firstRow.GetProperty("values").GetProperty("duration").GetDecimal()); // raw minutes
    }

    [Fact]
    public void Bad_filters_and_tables_fail_cleanly()
    {
        Assert.Contains("Unknown field", Cli.Fail("view", "--filter", "bogus = 1", "--file", _file).Stderr, StringComparison.Ordinal);
        Assert.Contains("unknown table", Cli.Fail("view", "--table", "nope", "--file", _file).Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_list_prints_the_catalog()
    {
        var fields = Cli.Ok("field", "list", "--json", "--file", _file).Json();
        Assert.True(fields.GetArrayLength() > 40);
        Assert.Contains(
            fields.EnumerateArray(),
            f => f.GetProperty("key").GetString() == "totalSlack" && f.GetProperty("kind").GetString() == "Duration");
    }
}
