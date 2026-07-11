using Xunit;

namespace Project27.Cli.Tests;

public sealed class ReportCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public ReportCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Report_list_names_the_set()
    {
        var list = Cli.Ok("report", "list", "--json", "--file", _file).Json();
        Assert.Equal(6, list.GetArrayLength());
        Assert.Contains(list.EnumerateArray(), r => r.GetProperty("name").GetString() == "overview");
    }

    [Fact]
    public void Report_writes_a_self_contained_html_file()
    {
        var output = _dir.File("out.html");
        Cli.Ok("report", "overview", "--out", output, "--file", _file);
        var html = File.ReadAllText(output);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Build", html, StringComparison.Ordinal);
        Assert.Contains("Plan", html, StringComparison.Ordinal);
    }
}
