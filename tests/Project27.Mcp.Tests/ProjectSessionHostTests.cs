using Project27.Mcp.Session;
using Xunit;

namespace Project27.Mcp.Tests;

public sealed class ProjectSessionHostTests : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"p27-mcp-host-test-{Guid.NewGuid():N}");

    public ProjectSessionHostTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetProjectAsync_throws_a_friendly_error_before_a_project_is_open()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        var exception = await Assert.ThrowsAsync<ProjectSessionException>(() => host.GetProjectAsync(Token));
        Assert.Contains("create_project", exception.Message);
        Assert.Contains("open_project", exception.Message);
    }

    [Fact]
    public async Task CreateProjectAsync_creates_a_file_named_after_the_project_when_no_path_is_given()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        var summary = await host.CreateProjectAsync("My Plan", null, null, Token);
        Assert.Equal("My Plan", summary.Name);
        Assert.True(File.Exists(Path.Combine(_directory, "My Plan.p27")));
    }

    [Fact]
    public async Task CreateProjectAsync_honors_an_explicit_relative_path()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        await host.CreateProjectAsync("My Plan", null, "custom.p27", Token);
        Assert.True(File.Exists(Path.Combine(_directory, "custom.p27")));
    }

    [Fact]
    public async Task CreateProjectAsync_twice_throws_because_the_session_already_has_a_project()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        await host.CreateProjectAsync("First", null, null, Token);
        await Assert.ThrowsAsync<ProjectSessionException>(() => host.CreateProjectAsync("Second", null, null, Token));
    }

    [Fact]
    public async Task CreateProjectAsync_rejected_by_the_already_open_guard_leaves_no_file_behind()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        await host.CreateProjectAsync("First", null, null, Token);
        await Assert.ThrowsAsync<ProjectSessionException>(() => host.CreateProjectAsync("Second", null, null, Token));
        Assert.False(File.Exists(Path.Combine(_directory, "Second.p27")));
    }

    [Fact]
    public async Task OpenProjectAsync_attaches_to_a_file_created_by_a_different_host_instance()
    {
        await using (var creator = new ProjectSessionHost(new LocalConnection(_directory)))
        {
            await creator.CreateProjectAsync("Existing Plan", null, "existing.p27", Token);
        }

        await using var opener = new ProjectSessionHost(new LocalConnection(_directory));
        var summary = await opener.OpenProjectAsync("existing.p27", Token);
        Assert.Equal("Existing Plan", summary.Name);
    }

    [Fact]
    public async Task GetProjectAsync_works_after_create_and_reflects_writes()
    {
        await using var host = new ProjectSessionHost(new LocalConnection(_directory));
        await host.CreateProjectAsync("Live Plan", null, null, Token);
        var view = await host.ListTasksAsync(null, "entry", null, null, null, Token);
        Assert.Empty(view.Groups.SelectMany(g => g.Rows));
    }
}
