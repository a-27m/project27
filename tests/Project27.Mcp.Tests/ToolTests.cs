using Project27.Core;
using Project27.Mcp.Session;
using Project27.Mcp.Tools;
using Project27.Storage;
using Xunit;

namespace Project27.Mcp.Tests;

public sealed class ToolTests : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"p27-mcp-tool-test-{Guid.NewGuid():N}.p27");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private LocalProjectSession OpenFreshProject()
    {
        var project = new Project("Tool Test", new DateTime(2026, 1, 5, 8, 0, 0));
        project.AddResource("Alice");
        project.Recalculate();
        SqliteProjectStore.Create(_path, project);
        return LocalProjectSession.Open(_path);
    }

    [Fact]
    public async Task TaskWrite_add_then_set_then_remove_round_trips()
    {
        await using var session = OpenFreshProject();
        var tasks = new TaskTools(session);
        var reads = new ReadTools(session);

        var addResult = await tasks.TaskWrite(op: "add", name: "Design", duration: "3d", cancellationToken: Token);
        Assert.Contains("createdUids", addResult);

        var view = await reads.ListTasks(table: "entry", cancellationToken: Token);
        Assert.Contains("Design", view);

        var setResult = await tasks.TaskWrite(op: "set", uid: 1, name: "Design (revised)", cancellationToken: Token);
        Assert.Contains("createdUids", setResult);

        var afterSet = await reads.GetTask(1, Token);
        Assert.Contains("Design (revised)", afterSet);

        await tasks.TaskWrite(op: "remove", uid: 1, cancellationToken: Token);
        var afterRemove = await reads.ListTasks(table: "entry", cancellationToken: Token);
        Assert.DoesNotContain("Design (revised)", afterRemove);
    }

    [Fact]
    public async Task TaskWrite_add_without_name_throws()
    {
        await using var session = OpenFreshProject();
        var tasks = new TaskTools(session);
        await Assert.ThrowsAsync<ArgumentException>(() => tasks.TaskWrite(op: "add", cancellationToken: Token));
    }

    [Fact]
    public async Task AssignmentWrite_assign_then_unassign()
    {
        await using var session = OpenFreshProject();
        var tasks = new TaskTools(session);
        var assignments = new AssignmentTools(session);
        var reads = new ReadTools(session);

        await tasks.TaskWrite(op: "add", name: "Design", duration: "3d", cancellationToken: Token);
        await assignments.AssignmentWrite(op: "assign", uid: 1, resource: "Alice", cancellationToken: Token);

        var task = await reads.GetTask(1, Token);
        Assert.Contains("Alice", task);

        await assignments.AssignmentWrite(op: "unassign", uid: 1, resource: "Alice", cancellationToken: Token);
        var afterUnassign = await reads.GetTask(1, Token);
        Assert.DoesNotContain("Alice", afterUnassign);
    }

    [Fact]
    public async Task ScheduleWrite_level_and_clear_leveling_round_trip()
    {
        await using var session = OpenFreshProject();
        var schedule = new ScheduleTools(session);
        var levelResult = await schedule.ScheduleWrite(op: "level", cancellationToken: Token);
        Assert.Contains("createdUids", levelResult);
        var clearResult = await schedule.ScheduleWrite(op: "clearLeveling", cancellationToken: Token);
        Assert.Contains("createdUids", clearResult);
    }

    [Fact]
    public async Task GetReport_returns_html_for_known_name_and_throws_for_unknown()
    {
        await using var session = OpenFreshProject();
        var reads = new ReadTools(session);
        var html = await reads.GetReport("overview", Token);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reads.GetReport("not-a-report", Token));
    }
}
