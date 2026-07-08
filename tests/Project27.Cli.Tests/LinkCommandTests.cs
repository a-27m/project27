using Xunit;
namespace Project27.Cli.Tests;

public sealed class LinkCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public LinkCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("task", "add", "A", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "B", "-d", "2d", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Add_set_and_remove_roundtrip()
    {
        Cli.Ok("link", "add", "1", "2", "--file", _file);
        var link = Cli.Ok("link", "list", "--json", "--file", _file).Json()[0];
        Assert.Equal("finishToStart", link.GetProperty("type").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, link.GetProperty("lag").ValueKind);

        Cli.Ok("link", "set", "1", "2", "--type", "ss", "--lag", "50%", "--file", _file);
        link = Cli.Ok("link", "list", "--json", "--file", _file).Json()[0];
        Assert.Equal("startToStart", link.GetProperty("type").GetString());
        Assert.Equal("50%", link.GetProperty("lag").GetString());

        Cli.Ok("link", "remove", "1", "2", "--file", _file);
        Assert.Contains("no links", Cli.Ok("link", "list", "--file", _file).Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Elapsed_and_lead_lags_render_in_days()
    {
        Cli.Ok("link", "add", "1", "2", "--lag", "1ed", "--file", _file);
        var link = Cli.Ok("link", "list", "--json", "--file", _file).Json()[0];
        Assert.Equal("1ed", link.GetProperty("lag").GetString());

        Cli.Ok("link", "set", "1", "2", "--lag", "-4h", "--file", _file);
        link = Cli.Ok("link", "list", "--json", "--file", _file).Json()[0];
        Assert.Equal("-0.5d", link.GetProperty("lag").GetString());
    }

    [Fact]
    public void Duplicate_and_cyclic_links_fail_cleanly()
    {
        Cli.Ok("link", "add", "1", "2", "--file", _file);
        Cli.Fail("link", "add", "1", "2", "--file", _file);
        Cli.Fail("link", "add", "2", "1", "--file", _file);
    }

    [Fact]
    public void Removing_a_missing_link_reports_it()
    {
        Assert.Contains(
            "no link between 1 and 2",
            Cli.Fail("link", "remove", "1", "2", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }
}
