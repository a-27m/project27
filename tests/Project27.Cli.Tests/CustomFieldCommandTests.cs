using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class CustomFieldCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public CustomFieldCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "4d", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Define_set_and_view_a_text_field()
    {
        Cli.Ok("customfield", "define", "text1", "--alias", "Phase", "--file", _file);
        Cli.Ok("task", "set", "1", "--field", "Phase=Rollout", "--file", _file);

        var view = Cli.Ok("view", "--fields", "name,Phase", "--json", "--file", _file).Json();
        var row = view.GetProperty("groups")[0].GetProperty("rows")[0];
        Assert.Equal("Rollout", row.GetProperty("values").GetProperty("text1").GetString());

        Cli.Ok("task", "set", "1", "--field", "Phase=none", "--file", _file);
        var cleared = Cli.Ok("view", "--fields", "Phase", "--json", "--file", _file).Json();
        Assert.Equal(
            JsonValueKind.Null,
            cleared.GetProperty("groups")[0].GetProperty("rows")[0].GetProperty("values").GetProperty("text1").ValueKind);
    }

    [Fact]
    public void Formula_fields_and_indicators_flow_through_views()
    {
        Cli.Ok(
            "customfield", "define", "number1", "--alias", "Risk",
            "--formula", "IIf([totalSlack] < 1d, 100, 0)",
            "--indicator", "when >= 100 then red-flag",
            "--file", _file);

        var view = Cli.Ok("view", "--fields", "name,Risk,Risk.icon", "--json", "--file", _file).Json();
        var values = view.GetProperty("groups")[0].GetProperty("rows")[0].GetProperty("values");
        Assert.Equal(100m, values.GetProperty("number1").GetDecimal());
        Assert.Equal("red-flag", values.GetProperty("Risk.icon").GetString());

        Assert.Contains(
            "computed by a formula",
            Cli.Fail("task", "set", "1", "--field", "Risk=5", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void List_and_remove_round_trip()
    {
        Cli.Ok("customfield", "define", "flag1", "--alias", "Approved", "--file", _file);
        var list = Cli.Ok("customfield", "list", "--json", "--file", _file).Json();
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Approved", list[0].GetProperty("alias").GetString());

        Cli.Ok("customfield", "remove", "Approved", "--file", _file);
        Assert.Contains("no custom fields", Cli.Ok("customfield", "list", "--file", _file).Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_fields_filter_and_group()
    {
        Cli.Ok("task", "add", "Docs", "-d", "1d", "--file", _file);
        Cli.Ok("customfield", "define", "text1", "--alias", "Team", "--file", _file);
        Cli.Ok("task", "set", "1", "--field", "Team=Core", "--file", _file);
        Cli.Ok("task", "set", "2", "--field", "Team=Docs", "--file", _file);

        var output = Cli.Ok("view", "--fields", "name", "--filter", "Team = Core", "--file", _file).Stdout;
        Assert.Contains("Build", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Docs", output, StringComparison.Ordinal);

        var grouped = Cli.Ok("view", "--fields", "name", "--group-by", "Team", "--file", _file).Stdout;
        Assert.Contains("Team: Core", grouped, StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_definitions_fail_cleanly()
    {
        Assert.Contains("not a custom field slot", Cli.Fail("customfield", "define", "text99", "--file", _file).Stderr, StringComparison.Ordinal);
        Assert.Contains("Invalid formula", Cli.Fail("customfield", "define", "number1", "--formula", "1 +", "--file", _file).Stderr, StringComparison.Ordinal);
        Assert.Contains("invalid indicator", Cli.Fail("customfield", "define", "number1", "--indicator", "banana", "--file", _file).Stderr, StringComparison.Ordinal);
        Assert.Contains("collides", Cli.Fail("customfield", "define", "text1", "--alias", "name", "--file", _file).Stderr, StringComparison.Ordinal);
    }
}
