using Project27.Core;
using Project27.Core.Commands;
using Project27.Core.Time;
using Project27.Mcp.Session;
using Project27.Storage;
using Xunit;

namespace Project27.Mcp.Tests;

public sealed class LocalProjectSessionTests : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"p27-mcp-test-{Guid.NewGuid():N}.p27");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private LocalProjectSession OpenFreshProject()
    {
        var project = new Project("MCP Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var summary = project.AddTask("Summary");
        project.AddTask("Design", Duration.Parse("3d"), summary);
        project.Recalculate();
        SqliteProjectStore.Create(_path, project);
        return LocalProjectSession.Open(_path);
    }

    [Fact]
    public async Task GetProjectAsync_reports_name_and_totals()
    {
        await using var session = OpenFreshProject();
        var summary = await session.GetProjectAsync(Token);
        Assert.Equal("MCP Test", summary.Name);
        Assert.Equal(new DateTime(2026, 1, 5, 8, 0, 0), summary.Start);
    }

    [Fact]
    public async Task ListTasksAsync_returns_the_entry_table_in_outline_order()
    {
        await using var session = OpenFreshProject();
        var view = await session.ListTasksAsync(null, "entry", null, null, null, Token);
        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Summary", rows[0].Values["name"]);
        Assert.Equal("Design", rows[1].Values["name"]);
    }

    [Fact]
    public async Task ApplyAsync_adds_a_task_and_persists_it_across_sessions()
    {
        await using (var session = OpenFreshProject())
        {
            var result = await session.ApplyAsync([new AddTaskCommand { Name = "Build", Duration = "2d" }], Token);
            Assert.Single(result.CreatedUids);
            Assert.NotNull(result.CreatedUids[0]);
        }

        await using var reopened = LocalProjectSession.Open(_path);
        var view = await reopened.ListTasksAsync(null, "entry", null, null, null, Token);
        Assert.Contains(view.Groups.SelectMany(g => g.Rows), r => Equals(r.Values["name"], "Build"));
    }

    [Fact]
    public async Task ApplyAsync_wraps_engine_failures_in_CommandException()
    {
        await using var session = OpenFreshProject();
        await Assert.ThrowsAsync<CommandException>(
            () => session.ApplyAsync([new SetTaskCommand { Uid = 999, Name = "Nope" }], Token));
    }

    [Fact]
    public async Task GetTaskDriversAsync_throws_for_unknown_uid()
    {
        await using var session = OpenFreshProject();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => session.GetTaskDriversAsync(999, Token));
    }

    [Fact]
    public async Task GetReportAsync_renders_html()
    {
        await using var session = OpenFreshProject();
        var html = await session.GetReportAsync("overview", Token);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
    }
}
