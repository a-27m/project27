extern alias server;

using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Project27.Cli.Tests;

/// <summary>
/// `--server` mode end to end: the CLI talks to an in-process server through the
/// <see cref="RemoteClient.HandlerFactory"/> seam. Runs sequentially (the seam is a
/// static hook) — xunit parallelizes across classes, and only this class uses it.
/// </summary>
public sealed class ServerModeTests : IDisposable
{
    private readonly string _directory;
    private readonly WebApplicationFactory<server::Program> _factory;

    public ServerModeTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "p27-cli-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _factory = new WebApplicationFactory<server::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Storage:Path", Path.Combine(_directory, "server.db"));
            builder.UseSetting("Auth:DevAuth", "true");
        });
        RemoteClient.HandlerFactory = _factory.Server.CreateHandler;
    }

    public void Dispose()
    {
        RemoteClient.HandlerFactory = null;
        _factory.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    private static string[] AsAlice(params string[] args)
        => [.. args, "--server", "http://localhost", "--dev-user", "alice"];

    [Fact]
    public void Create_edit_and_list_a_server_project()
    {
        Cli.Ok(AsAlice("project", "create", "Alpha", "--start", "2026-01-05"));
        Cli.Ok(AsAlice("task", "add", "Design", "-d", "2d", "--project", "Alpha"));
        Cli.Ok(AsAlice("task", "add", "Build", "-d", "3d", "--project", "Alpha"));
        Cli.Ok(AsAlice("link", "add", "1", "2", "--project", "Alpha"));

        var list = Cli.Ok(AsAlice("task", "list", "--project", "Alpha")).Stdout;
        Assert.Contains("Build", list, StringComparison.Ordinal);
        Assert.Contains("2026-01-09 17:00", list, StringComparison.Ordinal); // Build finish after Design

        var projects = Cli.Ok(AsAlice("project", "list", "--json")).Json();
        var alpha = projects.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Alpha");
        Assert.Equal(4, alpha.GetProperty("version").GetInt32()); // create + three edits
        Assert.Equal(System.Text.Json.JsonValueKind.Null, alpha.GetProperty("lock").ValueKind); // implicit locks released
    }

    [Fact]
    public void Explicit_checkout_holds_the_lock_across_commands()
    {
        Cli.Ok(AsAlice("project", "create", "Held"));
        Cli.Ok(AsAlice("checkout", "--project", "Held"));
        Cli.Ok(AsAlice("task", "add", "Work", "-d", "1d", "--project", "Held"));

        var held = Cli.Ok(AsAlice("project", "list", "--json")).Json()
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Held");
        Assert.Equal("alice", held.GetProperty("lock").GetProperty("userId").GetString());

        Cli.Ok(AsAlice("checkin", "--project", "Held"));
        var released = Cli.Ok(AsAlice("project", "list", "--json")).Json()
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Held");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, released.GetProperty("lock").ValueKind);
    }

    [Fact]
    public void Init_is_local_only()
    {
        var result = Cli.Fail(AsAlice("init", "Nope"));
        Assert.Contains("project create", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_mode_requires_a_project_reference()
    {
        Cli.Ok(AsAlice("project", "create", "Solo"));
        var result = Cli.Fail(AsAlice("task", "list"));
        Assert.Contains("--project", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_project_name_is_a_clean_error()
    {
        var result = Cli.Fail(AsAlice("task", "list", "--project", "Ghost"));
        Assert.Contains("no project named 'Ghost'", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_removes_the_server_project()
    {
        Cli.Ok(AsAlice("project", "create", "Doomed"));
        Cli.Ok(AsAlice("project", "delete", "Doomed"));
        Assert.Contains(
            "no project named 'Doomed'",
            Cli.Fail(AsAlice("task", "list", "--project", "Doomed")).Stderr,
            StringComparison.Ordinal);
    }
}
