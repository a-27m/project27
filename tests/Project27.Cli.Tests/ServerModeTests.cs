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

    // ---------------------------------------------------------- completion (D4)

    private static Completion.CompletionResult Complete(params string[] argv)
        => Completion.CompletionCommands.Resolve(argv);

    [Fact]
    public void Completes_server_project_names_with_the_callers_role()
    {
        Cli.Ok(AsAlice("project", "create", "Alpha Project"));
        Cli.Ok(AsAlice("project", "create", "Beta"));

        var candidates = Complete(
            "p27", "--server", "http://localhost", "--dev-user", "alice", "--project", "").Candidates;

        // A name with a space stays one candidate; the shell dequoted it for us.
        Assert.Contains(candidates, c => c.Value == "Alpha Project" && c.Description == "owner");
        Assert.Contains(candidates, c => c.Value == "Beta");
    }

    [Fact]
    public void Completes_task_references_from_the_server_project()
    {
        Cli.Ok(AsAlice("project", "create", "Remote Plan", "--start", "2026-01-05"));
        Cli.Ok(AsAlice("task", "add", "Remote design", "-d", "2d", "--project", "Remote Plan"));

        var candidates = Complete(
            "p27", "--server", "http://localhost", "--dev-user", "alice",
            "--project", "Remote Plan", "task", "show", "").Candidates;

        Assert.Equal(["1"], candidates.Select(c => c.Value));
        Assert.Equal(["Remote design"], candidates.Select(c => c.Description));
    }

    /// <summary>
    /// `Resolve` rejects a name shared by two projects, so completing the name would only
    /// produce a value the command refuses. Ambiguous names come back as ids instead.
    /// </summary>
    [Fact]
    public void An_ambiguous_project_name_completes_to_ids_not_the_name()
    {
        Cli.Ok(AsAlice("project", "create", "Twin"));
        Cli.Ok(AsAlice("project", "create", "Twin"));

        var candidates = Complete(
            "p27", "--server", "http://localhost", "--dev-user", "alice", "--project", "").Candidates;

        Assert.DoesNotContain(candidates, c => c.Value == "Twin");
        var ids = candidates.Where(c => c.Description?.StartsWith("Twin", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, c => Assert.True(Guid.TryParse(c.Value, out _), $"'{c.Value}' should be an id"));
    }

    /// <summary>
    /// P27_SERVER puts the CLI in server mode with nothing on the command line to show
    /// it, so completion has to resolve the source exactly the way the verbs do.
    /// </summary>
    [Fact]
    public void The_server_environment_variable_alone_puts_completion_in_server_mode()
    {
        Cli.Ok(AsAlice("project", "create", "Env Only"));

        var candidates = WithEnvironment(
            () => Complete("p27", "--dev-user", "alice", "--project", "").Candidates,
            ("P27_SERVER", "http://localhost"));

        Assert.Contains(candidates, c => c.Value == "Env Only");
    }

    [Fact]
    public void The_project_environment_variable_resolves_task_references_too()
    {
        Cli.Ok(AsAlice("project", "create", "Env Plan", "--start", "2026-01-05"));
        Cli.Ok(AsAlice("task", "add", "Env task", "-d", "1d", "--project", "Env Plan"));

        var candidates = WithEnvironment(
            () => Complete("p27", "--dev-user", "alice", "task", "show", "").Candidates,
            ("P27_SERVER", "http://localhost"),
            ("P27_PROJECT", "Env Plan"));

        Assert.Equal(["Env task"], candidates.Select(c => c.Description));
    }

    private static T WithEnvironment<T>(Func<T> action, params (string Name, string Value)[] variables)
    {
        var previous = variables.Select(v => (v.Name, Old: Environment.GetEnvironmentVariable(v.Name))).ToList();
        foreach (var (name, value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            return action();
        }
        finally
        {
            foreach (var (name, old) in previous)
            {
                Environment.SetEnvironmentVariable(name, old);
            }
        }
    }

    [Fact]
    public void Import_mspdi_creates_a_server_project()
    {
        // Export a local project to MSPDI, then import it to the server.
        var tempDir = new TempDir();
        try
        {
            var localFile = tempDir.File("local.p27");
            var xmlFile = tempDir.File("export.xml");
            Cli.Ok("init", "SourceProject", "--start", "2026-01-05", "--file", localFile);
            Cli.Ok("task", "add", "Design", "-d", "2d", "--file", localFile);
            Cli.Ok("task", "add", "Build", "-d", "3d", "--file", localFile);
            Cli.Ok("link", "add", "1", "2", "--file", localFile);
            Cli.Ok("export", "mspdi", "--out", xmlFile, "--file", localFile);

            // Import to the server; JSON output matches `project create`'s shape (full project info, not just id/name).
            var imported = Cli.Ok(AsAlice("import", "mspdi", xmlFile, "--json")).Json();
            Assert.Equal("SourceProject", imported.GetProperty("name").GetString());
            Assert.Equal(1, imported.GetProperty("version").GetInt32());
            Assert.Equal("owner", imported.GetProperty("role").GetString());

            // Verify the project exists on the server and tasks are intact.
            var list = Cli.Ok(AsAlice("task", "list", "--project", "SourceProject", "--json")).Json();
            Assert.Equal(2, list.GetArrayLength());
            Assert.Equal("Design", list[0].GetProperty("name").GetString());
            Assert.Equal("Build", list[1].GetProperty("name").GetString());
            Assert.Equal("2026-01-07T08:00:00", list[1].GetProperty("start").GetString()); // FS link kept
        }
        finally
        {
            tempDir.Dispose();
        }
    }

    [Fact]
    public void Import_mspdi_rejects_missing_files()
    {
        var result = Cli.Fail(AsAlice("import", "mspdi", "/nonexistent/file.xml"));
        Assert.Contains("not found", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_p27_creates_a_server_project()
    {
        // Create a local .p27 file and import it to the server.
        var tempDir = new TempDir();
        try
        {
            var localFile = tempDir.File("local.p27");
            Cli.Ok("init", "ImportedP27", "--start", "2026-02-01", "--file", localFile);
            Cli.Ok("task", "add", "Phase1", "-d", "1d", "--file", localFile);
            Cli.Ok("task", "add", "Phase2", "-d", "2d", "--file", localFile);

            // Import to the server.
            Cli.Ok(AsAlice("import", "p27", localFile));

            // Verify the project exists on the server and tasks are intact.
            var list = Cli.Ok(AsAlice("task", "list", "--project", "ImportedP27", "--json")).Json();
            Assert.Equal(2, list.GetArrayLength());
            Assert.Equal("Phase1", list[0].GetProperty("name").GetString());
            Assert.Equal("Phase2", list[1].GetProperty("name").GetString());
        }
        finally
        {
            tempDir.Dispose();
        }
    }

    [Fact]
    public void Import_p27_rejects_missing_files()
    {
        var result = Cli.Fail(AsAlice("import", "p27", "/nonexistent/file.p27"));
        Assert.Contains("not found", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_mspdi_still_works_without_server_flag()
    {
        var tempDir = new TempDir();
        try
        {
            var localFile = tempDir.File("local.p27");
            var xmlFile = tempDir.File("export.xml");
            Cli.Ok("init", "SourceProject", "--start", "2026-01-05", "--file", localFile);
            Cli.Ok("export", "mspdi", "--out", xmlFile, "--file", localFile);

            var importedFile = tempDir.File("imported.p27");
            Cli.Ok("import", "mspdi", xmlFile, "--file", importedFile);
            Assert.True(File.Exists(importedFile));
        }
        finally
        {
            tempDir.Dispose();
        }
    }

    [Fact]
    public void Import_p27_without_server_mode_fails()
    {
        var tempDir = new TempDir();
        try
        {
            var localFile = tempDir.File("local.p27");
            Cli.Ok("init", "SourceProject", "--start", "2026-01-05", "--file", localFile);

            // p27 import without --server should fail.
            var result = Cli.Fail("import", "p27", localFile);
            Assert.Contains("--server", result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Dispose();
        }
    }

    [Fact]
    public void Import_mspdi_rejects_file_together_with_server()
    {
        var tempDir = new TempDir();
        try
        {
            var localFile = tempDir.File("local.p27");
            var xmlFile = tempDir.File("export.xml");
            Cli.Ok("init", "SourceProject", "--start", "2026-01-05", "--file", localFile);
            Cli.Ok("export", "mspdi", "--out", xmlFile, "--file", localFile);

            // --file is a local-mode concept; combined with --server it's ambiguous
            // (there's no local file to write to remotely), so it errors rather than
            // being silently dropped.
            var result = Cli.Fail(AsAlice("import", "mspdi", xmlFile, "--file", "ignored.p27"));
            Assert.Contains("--file", result.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Dispose();
        }
    }
}
