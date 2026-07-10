using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Project27.Server.Tests;

[Collection("server")]
public sealed class CommandApiTests
{
    private readonly ServerFixture _server;

    public CommandApiTests(ServerFixture server) => _server = server;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Guid Id, HttpClient Alice)> CreateProject(string name)
    {
        var alice = _server.Client("alice");
        var response = await alice.PostAsJsonAsync("/api/projects", new { name, start = "2026-01-05T08:00:00" }, Token);
        var info = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return (info.GetProperty("id").GetGuid(), alice);
    }

    private static object[] GoldenBatch() =>
    [
        new Dictionary<string, object> { ["op"] = "addTask", ["name"] = "Design", ["duration"] = "2d" },
        new Dictionary<string, object> { ["op"] = "addTask", ["name"] = "Build", ["duration"] = "3d" },
        new Dictionary<string, object> { ["op"] = "link", ["predecessorUid"] = 1, ["successorUid"] = 2 },
    ];

    [Fact]
    public async Task Commands_require_the_lock()
    {
        var (id, alice) = await CreateProject("CmdLock-" + Guid.NewGuid().ToString("N"));
        var response = await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", GoldenBatch(), Token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Commands_apply_bump_the_version_and_return_the_schedule()
    {
        var (id, alice) = await CreateProject("Cmd-" + Guid.NewGuid().ToString("N"));
        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);

        var response = await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", GoldenBatch(), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(2, body.GetProperty("version").GetInt32());
        Assert.Equal(1, body.GetProperty("createdUids")[0].GetInt32());

        var tasks = body.GetProperty("schedule").GetProperty("tasks");
        var build = tasks.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Build");
        Assert.Equal("2026-01-07T08:00:00", build.GetProperty("start").GetString());
        Assert.True(build.GetProperty("critical").GetBoolean());

        // The lock is kept for further batches.
        var info = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}", Token);
        Assert.Equal("alice", info.GetProperty("lock").GetProperty("userId").GetString());
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Invalid_commands_are_unprocessable_and_change_nothing()
    {
        var (id, alice) = await CreateProject("CmdBad-" + Guid.NewGuid().ToString("N"));
        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);

        var bad = new object[]
        {
            new Dictionary<string, object> { ["op"] = "setTask", ["uid"] = 42, ["name"] = "X" },
        };
        var response = await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", bad, Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var info = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}", Token);
        Assert.Equal(1, info.GetProperty("version").GetInt32());
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Schedule_projection_is_readable_by_readers()
    {
        var (id, alice) = await CreateProject("Sched-" + Guid.NewGuid().ToString("N"));
        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);
        await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", GoldenBatch(), Token);
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
        await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/bob", new { role = "reader" }, Token);

        var bob = _server.Client("bob");
        var schedule = await bob.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}/schedule", Token);
        Assert.Equal(2, schedule.GetProperty("version").GetInt32());
        Assert.Equal(2, schedule.GetProperty("tasks").GetArrayLength());
        Assert.Equal(480, schedule.GetProperty("project").GetProperty("minutesPerDay").GetInt32());
        var design = schedule.GetProperty("tasks")[0];
        Assert.Equal(960m, design.GetProperty("durationMinutes").GetDecimal());
        Assert.Equal("2026-01-06T17:00:00", design.GetProperty("finish").GetString());
    }
}
