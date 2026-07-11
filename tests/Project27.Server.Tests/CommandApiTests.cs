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
    public async Task Usage_projection_returns_time_phased_buckets()
    {
        var (id, alice) = await CreateProject("Usage-" + Guid.NewGuid().ToString("N"));

        // Assignments aren't editable via commands yet: check a document in that has one.
        var project = new Project27.Core.Project("Usage", new DateTime(2026, 1, 5, 8, 0, 0), id: id);
        var dev = project.AddResource("Dev");
        dev.RateTable(Project27.Core.CostRateTableId.A).SetRate(DateTime.MinValue, new Project27.Core.Rate(60m, Project27.Core.RateUnit.Hour));
        var build = project.AddTask("Build", Project27.Core.Time.Duration.Parse("3d"));
        project.Assign(build, dev);
        project.Recalculate();
        var document = Project27.Storage.ProjectDocumentSerializer.Serialize(
            Project27.Core.Persistence.ProjectDocumentMapper.ToDocument(project));

        var checkoutResponse = await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);
        var version = (await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("version").GetInt32();
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{id:D}/document")
        {
            Content = new System.Net.Http.StringContent(document, System.Text.Encoding.UTF8, "application/json"),
        };
        putRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var checkin = await alice.SendAsync(putRequest, Token);
        Assert.Equal(HttpStatusCode.OK, checkin.StatusCode);

        var usage = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}/usage?granularity=day", Token);
        var row = usage.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Build");
        Assert.Equal(3, row.GetProperty("buckets").GetArrayLength());
        Assert.Equal(480m, row.GetProperty("buckets")[0].GetProperty("workMinutes").GetDecimal());
        Assert.Equal(1440m, row.GetProperty("totalWorkMinutes").GetDecimal());

        var weekly = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}/usage", Token);
        Assert.Equal("week", weekly.GetProperty("granularity").GetString());

        var bad = await alice.GetAsync($"/api/projects/{id:D}/usage?granularity=hour", Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
    }

    [Fact]
    public async Task View_and_drivers_endpoints_project_the_engine()
    {
        var (id, alice) = await CreateProject("View-" + Guid.NewGuid().ToString("N"));
        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);
        await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", GoldenBatch(), Token);
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);

        var view = await alice.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{id:D}/view?fields=id,name,duration&filter=critical%20%3D%20true&sort=duration%20desc", Token);
        var rows = view.GetProperty("groups")[0].GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("Build", rows[0].GetProperty("values").GetProperty("name").GetString());

        var bad = await alice.GetAsync($"/api/projects/{id:D}/view?filter=bogus%20%3D%201", Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);

        var drivers = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}/drivers/2", Token);
        Assert.Contains(
            drivers.EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "Predecessor" && d.GetProperty("binding").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/projects/{id:D}/drivers/99", Token)).StatusCode);
    }

    [Fact]
    public async Task Resource_ops_flow_through_the_commands_endpoint()
    {
        var (id, alice) = await CreateProject("Ops-" + Guid.NewGuid().ToString("N"));
        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);
        var batch = new object[]
        {
            new Dictionary<string, object> { ["op"] = "addResource", ["name"] = "Dev", ["rate"] = "50/h" },
            new Dictionary<string, object> { ["op"] = "addTask", ["name"] = "Build", ["duration"] = "3d" },
            new Dictionary<string, object> { ["op"] = "assign", ["uid"] = 1, ["resource"] = "Dev" },
        };
        var response = await alice.PostAsJsonAsync($"/api/projects/{id:D}/commands", batch, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var projectDto = body.GetProperty("schedule").GetProperty("project");
        Assert.Equal("Dev", projectDto.GetProperty("resources")[0].GetProperty("name").GetString());
        Assert.Equal("50/h", projectDto.GetProperty("resources")[0].GetProperty("rate").GetString());
        var build = body.GetProperty("schedule").GetProperty("tasks")[0];
        Assert.Equal(1200m, build.GetProperty("cost").GetDecimal()); // 24h × 50
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Reports_render_as_html_for_readers()
    {
        var (id, alice) = await CreateProject("Report-" + Guid.NewGuid().ToString("N"));
        var response = await alice.GetAsync($"/api/projects/{id:D}/reports/overview", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("<!doctype html>", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/projects/{id:D}/reports/bogus", Token)).StatusCode);
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
