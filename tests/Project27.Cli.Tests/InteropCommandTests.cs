using Xunit;

namespace Project27.Cli.Tests;

public sealed class InteropCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public InteropCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("task", "add", "Design", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", _file);
        Cli.Ok("link", "add", "1", "2", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Csv_export_writes_the_requested_view()
    {
        var output = _dir.File("tasks.csv");
        Cli.Ok("export", "csv", "--out", output, "--fields", "id,name,start,finish", "--file", _file);
        var lines = File.ReadAllLines(output);
        Assert.Equal("ID,Name,Start,Finish", lines[0]);
        Assert.Contains("Build", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Mspdi_round_trips_through_the_cli()
    {
        var xml = _dir.File("plan.xml");
        Cli.Ok("export", "mspdi", "--out", xml, "--file", _file);
        Assert.Contains("http://schemas.microsoft.com/project", File.ReadAllText(xml), StringComparison.Ordinal);

        var imported = _dir.File("imported.p27");
        Cli.Ok("import", "mspdi", xml, "--file", imported);
        var list = Cli.Ok("task", "list", "--json", "--file", imported).Json();
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("2026-01-07T08:00:00", list[1].GetProperty("start").GetString()); // FS link kept
    }

    [Fact]
    public void Import_rejects_missing_files_cleanly()
    {
        Assert.Contains("not found", Cli.Fail("import", "mspdi", _dir.File("nope.xml"), "--file", _file).Stderr, StringComparison.Ordinal);
    }
}
