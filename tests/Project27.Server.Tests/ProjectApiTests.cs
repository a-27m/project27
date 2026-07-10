using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Project27.Server.Tests;

[Collection("server")]
public sealed class ProjectApiTests
{
    private readonly ServerFixture _server;

    public ProjectApiTests(ServerFixture server) => _server = server;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Guid Id, HttpClient Alice)> CreateProject(string name)
    {
        var alice = _server.Client("alice");
        var response = await alice.PostAsJsonAsync("/api/projects", new { name, start = "2026-01-05T08:00:00" }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return (info.GetProperty("id").GetGuid(), alice);
    }

    private static async Task<string> Checkout(HttpClient client, Guid id)
    {
        var response = await client.PostAsync($"/api/projects/{id:D}/checkout", content: null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return body.GetProperty("version").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> Checkin(HttpClient client, Guid id, string version, string document, bool keep = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{id:D}/document?keep={keep}")
        {
            Content = new StringContent(document, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request, Token);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var anonymous = _server.Client(user: null);
        var response = await anonymous.GetAsync("/api/projects", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_dev_user_is_rejected()
    {
        var mallory = _server.Client("mallory");
        var response = await mallory.GetAsync("/api/projects", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_makes_the_caller_owner_at_version_1()
    {
        var (id, alice) = await CreateProject("Owned-" + Guid.NewGuid().ToString("N"));
        var info = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}", Token);
        Assert.Equal("owner", info.GetProperty("role").GetString());
        Assert.Equal(1, info.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Non_members_cannot_see_the_project()
    {
        var (id, _) = await CreateProject("Hidden-" + Guid.NewGuid().ToString("N"));
        var bob = _server.Client("bob");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/projects/{id:D}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/projects/{id:D}/document", Token)).StatusCode);
    }

    [Fact]
    public async Task Readers_read_but_cannot_edit()
    {
        var (id, alice) = await CreateProject("Roles-" + Guid.NewGuid().ToString("N"));
        await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/bob", new { role = "reader" }, Token);

        var bob = _server.Client("bob");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/projects/{id:D}/document", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsync($"/api/projects/{id:D}/checkout", null, Token)).StatusCode);

        await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/bob", new { role = "editor" }, Token);
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsync($"/api/projects/{id:D}/checkout", null, Token)).StatusCode);
        await bob.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Checkin_bumps_the_version_and_releases_the_lock()
    {
        var (id, alice) = await CreateProject("Flow-" + Guid.NewGuid().ToString("N"));
        var version = await Checkout(alice, id);
        var document = await alice.GetStringAsync($"/api/projects/{id:D}/document", Token);

        var response = await Checkin(alice, id, version, document);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(2, body.GetProperty("version").GetInt32());

        var info = await alice.GetFromJsonAsync<JsonElement>($"/api/projects/{id:D}", Token);
        Assert.Equal(JsonValueKind.Null, info.GetProperty("lock").ValueKind);

        // Without the lock a second check-in is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await Checkin(alice, id, "2", document)).StatusCode);
    }

    [Fact]
    public async Task Version_mismatch_is_a_conflict()
    {
        var (id, alice) = await CreateProject("Conflict-" + Guid.NewGuid().ToString("N"));
        await Checkout(alice, id);
        var document = await alice.GetStringAsync($"/api/projects/{id:D}/document", Token);
        var response = await Checkin(alice, id, "41", document);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Invalid_documents_are_unprocessable()
    {
        var (id, alice) = await CreateProject("Invalid-" + Guid.NewGuid().ToString("N"));
        var version = await Checkout(alice, id);
        var response = await Checkin(alice, id, version, """{"schemaVersion": 99}""");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task Held_locks_block_other_editors_but_owners_can_break_them()
    {
        var (id, alice) = await CreateProject("Locks-" + Guid.NewGuid().ToString("N"));
        await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/bob", new { role = "editor" }, Token);
        var bob = _server.Client("bob");

        await Checkout(bob, id);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token)).StatusCode);

        // Owner breaks the fresh lock; a plain editor could not have.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token)).StatusCode);
        await alice.DeleteAsync($"/api/projects/{id:D}/lock", Token);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_or_removed()
    {
        var (id, alice) = await CreateProject("LastOwner-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/alice", new { role = "editor" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"/api/projects/{id:D}/members/alice", Token)).StatusCode);
    }

    [Fact]
    public async Task Checkin_events_reach_subscribers()
    {
        var (id, alice) = await CreateProject("Events-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var subscription = await alice.GetAsync(
            $"/api/projects/{id:D}/events",
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        var stream = await subscription.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);

        var version = await Checkout(alice, id);
        var document = await alice.GetStringAsync($"/api/projects/{id:D}/document", cancellation.Token);
        await Checkin(alice, id, version, document);

        var kinds = new List<string>();
        while (!cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellation.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                kinds.Add(line["event: ".Length..]);
                if (kinds.Contains("checkin"))
                {
                    break;
                }
            }
        }

        Assert.Contains("checkin", kinds);
    }

    [Fact]
    public async Task Stale_locks_can_be_taken_over_by_editors()
    {
        using var fixture = ServerFixture.WithStaleMinutes(0);
        var alice = fixture.Client("alice");
        var created = await alice.PostAsJsonAsync("/api/projects", new { name = "Stale" }, Token);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
        await alice.PutAsJsonAsync($"/api/projects/{id:D}/members/bob", new { role = "editor" }, Token);

        await alice.PostAsync($"/api/projects/{id:D}/checkout", null, Token);

        // With StaleAfterMinutes=0 every lock is immediately stale: bob may steal it.
        var bob = fixture.Client("bob");
        var response = await bob.PostAsync($"/api/projects/{id:D}/checkout", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("bob", body.GetProperty("lock").GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Delete_removes_the_project_for_everyone()
    {
        var (id, alice) = await CreateProject("Doomed-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/projects/{id:D}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/projects/{id:D}", Token)).StatusCode);
    }
}
