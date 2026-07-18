using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Core;
using Project27.Core.Commands;

namespace Project27.Mcp.Session;

// Wire shapes of the Project27.Server API (docs/spec/06-server.md, docs/spec/12p-1). Field
// names match the server's DTOs (ServerModels.cs / ScheduleProjection.cs) exactly, so these
// deserialize straight from the server's JSON; unrecognized JSON members (e.g. `schedule` on
// the commands response) are ignored by System.Text.Json rather than requiring a matching type.

internal sealed record RemoteLock(string UserId, DateTime AcquiredAt, DateTime RefreshedAt, bool Stale);

internal sealed record RemoteProjectInfo(Guid Id, string Name);

internal sealed record RemoteCheckout(int Version, RemoteLock Lock);

internal sealed record RemoteScheduleProjectDto(
    Guid Id,
    string Name,
    DateTime Start,
    DateTime? Finish,
    ScheduleFrom ScheduleFrom,
    string Calendar,
    DateTime? StatusDate,
    decimal TotalWorkMinutes,
    decimal TotalCost,
    IReadOnlyList<string> Calendars,
    IReadOnlyList<ResourceSummary> Resources,
    IReadOnlyList<CustomFieldSummary> CustomFields,
    ProjectStatsData Stats);

internal sealed record RemoteScheduleDto(int Version, RemoteScheduleProjectDto Project);

internal sealed record RemoteCommandsResponse(int Version, IReadOnlyList<int?> CreatedUids);

/// <summary>
/// A checked-out project on a Project27.Server (D6/D6a): checkout acquires the edit lock at
/// session start, every mutation goes through the command endpoint, and the lock releases on
/// dispose if this session was the one that acquired it (mirrors the CLI's `p27 --server`
/// inferred-keep rule, E19).
/// </summary>
public sealed class RemoteProjectSession : IProjectSession
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient _http;
    private readonly bool _releaseLockOnDispose;

    private RemoteProjectSession(HttpClient http, Guid projectId, bool releaseLockOnDispose)
    {
        _http = http;
        ProjectId = projectId;
        _releaseLockOnDispose = releaseLockOnDispose;
    }

    public Guid ProjectId { get; }

    public static async Task<RemoteProjectSession> OpenAsync(
        string serverUrl, string projectRef, string? token, string? devUser, CancellationToken cancellationToken)
    {
        var http = BuildClient(serverUrl, token, devUser);
        var info = await Resolve(http, projectRef, cancellationToken).ConfigureAwait(false);
        return await CheckoutAsync(http, info.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a brand-new server project (mirrors `POST /api/projects`), then checks it out.</summary>
    public static async Task<RemoteProjectSession> CreateAsync(
        string serverUrl, string name, DateTime? start, string? token, string? devUser, CancellationToken cancellationToken)
    {
        var http = BuildClient(serverUrl, token, devUser);
        var body = JsonContent.Create(new { name, start }, options: JsonOptions);
        var info = await ReadAsync<RemoteProjectInfo>(
            await SendAsync(http, HttpMethod.Post, "api/projects", body, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        return await CheckoutAsync(http, info.Id, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient BuildClient(string serverUrl, string? token, string? devUser)
    {
        var http = new HttpClient { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/", UriKind.Absolute) };
        if (!string.IsNullOrEmpty(token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrEmpty(devUser))
        {
            http.DefaultRequestHeaders.Add("X-Dev-User", devUser);
        }

        return http;
    }

    private static async Task<RemoteProjectSession> CheckoutAsync(HttpClient http, Guid id, CancellationToken cancellationToken)
    {
        var checkout = await ReadAsync<RemoteCheckout>(
            await SendAsync(http, HttpMethod.Post, $"api/projects/{id:D}/checkout", null, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);

        // A lock whose AcquiredAt predates RefreshedAt already existed before this call (E19).
        var acquiredHere = checkout.Lock.AcquiredAt == checkout.Lock.RefreshedAt;
        return new RemoteProjectSession(http, id, acquiredHere);
    }

    public async Task<ProjectSummary> GetProjectAsync(CancellationToken cancellationToken)
    {
        var schedule = await ReadAsync<RemoteScheduleDto>(
            await SendAsync(_http, HttpMethod.Get, $"api/projects/{ProjectId:D}/schedule", null, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        var project = schedule.Project;
        return new ProjectSummary(
            project.Id,
            project.Name,
            project.Start,
            project.Finish,
            project.ScheduleFrom,
            project.Calendar,
            project.StatusDate,
            project.TotalWorkMinutes,
            project.TotalCost,
            project.Calendars,
            project.Resources,
            project.CustomFields,
            project.Stats,
            EarnedValueData.Zero); // remote mode has no dedicated project-EVM endpoint yet.
    }

    public async Task<TaskView> ListTasksAsync(
        IReadOnlyList<string>? fields, string? table, string? filter, string? sort, string? groupBy, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (fields is { Count: > 0 })
        {
            query.Add($"fields={Uri.EscapeDataString(string.Join(',', fields))}");
        }
        else if (!string.IsNullOrWhiteSpace(table))
        {
            query.Add($"table={Uri.EscapeDataString(table)}");
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query.Add($"filter={Uri.EscapeDataString(filter)}");
        }

        if (!string.IsNullOrWhiteSpace(sort))
        {
            query.Add($"sort={Uri.EscapeDataString(sort)}");
        }

        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            query.Add($"groupBy={Uri.EscapeDataString(groupBy)}");
        }

        var path = $"api/projects/{ProjectId:D}/view" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        return await ReadAsync<TaskView>(await SendAsync(_http, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResourceSummary>> ListResourcesAsync(CancellationToken cancellationToken)
        => (await GetProjectAsync(cancellationToken).ConfigureAwait(false)).Resources;

    public async Task<IReadOnlyList<TaskDriver>> GetTaskDriversAsync(int uid, CancellationToken cancellationToken)
        => await ReadAsync<IReadOnlyList<TaskDriver>>(
            await SendAsync(_http, HttpMethod.Get, $"api/projects/{ProjectId:D}/drivers/{uid}", null, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);

    public async Task<UsageResult> GetUsageAsync(bool weekly, CancellationToken cancellationToken)
        => await ReadAsync<UsageResult>(
            await SendAsync(_http, HttpMethod.Get, $"api/projects/{ProjectId:D}/usage?granularity={(weekly ? "week" : "day")}", null, cancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);

    public async Task<string> GetReportAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(_http, HttpMethod.Get, $"api/projects/{ProjectId:D}/reports/{Uri.EscapeDataString(name)}", null, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandResult> ApplyAsync(IReadOnlyList<ProjectCommand> commands, CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(commands, options: JsonOptions);
        var result = await ReadAsync<RemoteCommandsResponse>(
            await SendAsync(_http, HttpMethod.Post, $"api/projects/{ProjectId:D}/commands", content, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        return new CommandResult(result.CreatedUids);
    }

    public async ValueTask DisposeAsync()
    {
        if (_releaseLockOnDispose)
        {
            try
            {
                using var response = await SendAsync(_http, HttpMethod.Delete, $"api/projects/{ProjectId:D}/lock", null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (ProjectSessionException)
            {
                // Best-effort: a crashed/unreachable server shouldn't block shutdown; `p27 unlock` recovers (E19).
            }
        }

        _http.Dispose();
    }

    private static async Task<RemoteProjectInfo> Resolve(HttpClient http, string projectRef, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(projectRef, out var id))
        {
            return await ReadAsync<RemoteProjectInfo>(await SendAsync(http, HttpMethod.Get, $"api/projects/{id:D}", null, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        var all = await ReadAsync<List<RemoteProjectInfo>>(await SendAsync(http, HttpMethod.Get, "api/projects", null, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        var matches = all.Where(p => string.Equals(p.Name, projectRef, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ProjectSessionException($"no project named '{projectRef}' on the server"),
            _ => throw new ProjectSessionException($"project name '{projectRef}' is ambiguous on the server; use its id"),
        };
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProjectSessionException($"cannot reach server {http.BaseAddress}: {exception.Message}", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var detail = ProblemDetail(body) ?? response.ReasonPhrase ?? "request failed";
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new ProjectSessionException("authentication required; pass --token or --dev-user"),
                _ => new ProjectSessionException(detail),
            };
        }
    }

    private static string? ProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions).ConfigureAwait(false)
                ?? throw new ProjectSessionException("the server returned an empty response");
        }
    }
}
