using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Project27.Cli;

// Wire shapes of the server API (docs/spec/06-server.md); enums travel camelCase.
internal sealed record RemoteLock(string UserId, DateTime AcquiredAt, DateTime RefreshedAt, bool Stale);

internal sealed record RemoteProjectInfo(
    Guid Id,
    string Name,
    int Version,
    string CreatedBy,
    DateTime CreatedAt,
    string Role,
    RemoteLock? Lock);

internal sealed record RemoteCheckout(int Version, RemoteLock Lock);

internal sealed record RemoteCheckin(int Version);

internal sealed record RemoteAuthConfig(
    bool DevAuth,
    string? Authority,
    string? ClientId,
    string? Scopes);

/// <summary>
/// Blocking HTTP client for `--server` mode. Server problem responses surface as
/// <see cref="CliException"/> with the problem detail as the message.
/// </summary>
internal sealed class RemoteClient : IDisposable
{
    /// <summary>Test seam: lets tests route requests into an in-process TestServer.</summary>
    internal static Func<HttpMessageHandler>? HandlerFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient _http;

    /// <summary>
    /// <paramref name="timeout"/> overrides the default (100s) request timeout; the
    /// completion path uses a short one so a slow server cannot stall a shell prompt.
    /// </summary>
    public RemoteClient(string baseUrl, string? token, string? devUser, TimeSpan? timeout = null)
    {
        _http = HandlerFactory is { } factory ? new HttpClient(factory(), disposeHandler: true) : new HttpClient();
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (timeout is { } limit)
        {
            _http.Timeout = limit;
        }

        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrEmpty(devUser))
        {
            _http.DefaultRequestHeaders.Add("X-Dev-User", devUser);
        }
    }

    public void Dispose() => _http.Dispose();

    public RemoteAuthConfig GetAuthConfig()
        => Read<RemoteAuthConfig>(Send(HttpMethod.Get, "api/auth/config"));

    public IReadOnlyList<RemoteProjectInfo> ListProjects()
        => Read<List<RemoteProjectInfo>>(Send(HttpMethod.Get, "api/projects"));

    public RemoteProjectInfo CreateProject(string name, DateTime? start)
        => Read<RemoteProjectInfo>(Send(HttpMethod.Post, "api/projects", JsonContent.Create(new { name, start }, options: JsonOptions)));

    public void DeleteProject(Guid id) => Send(HttpMethod.Delete, $"api/projects/{id:D}").Dispose();

    /// <summary>Resolves a name (case-insensitive, must be unique) or a GUID to a project.</summary>
    public RemoteProjectInfo Resolve(string projectRef)
    {
        if (Guid.TryParse(projectRef, out var id))
        {
            return Read<RemoteProjectInfo>(Send(HttpMethod.Get, $"api/projects/{id:D}"));
        }

        var matches = ListProjects()
            .Where(p => string.Equals(p.Name, projectRef, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliException($"no project named '{projectRef}' on the server"),
            _ => throw new CliException($"project name '{projectRef}' is ambiguous on the server; use its id"),
        };
    }

    public (string Json, int Version) GetDocument(Guid id)
    {
        using var response = Send(HttpMethod.Get, $"api/projects/{id:D}/document");
        var version = int.Parse(
            response.Headers.GetValues("X-Project-Version").First(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
        return (response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), version);
    }

    public RemoteCheckout Checkout(Guid id)
        => Read<RemoteCheckout>(Send(HttpMethod.Post, $"api/projects/{id:D}/checkout"));

    public RemoteCheckin Checkin(Guid id, int version, string documentJson, bool keepLock)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/projects/{id:D}/document?keep={(keepLock ? "true" : "false")}")
        {
            Content = new StringContent(documentJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.ToString(CultureInfo.InvariantCulture)}\"");
        return Read<RemoteCheckin>(SendRequest(request));
    }

    public void Unlock(Guid id) => Send(HttpMethod.Delete, $"api/projects/{id:D}/lock").Dispose();

    // ---------------------------------------------------------------- plumbing

    private HttpResponseMessage Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        return SendRequest(request);
    }

    private HttpResponseMessage SendRequest(HttpRequestMessage request)
    {
        HttpResponseMessage response;
        try
        {
            response = _http.SendAsync(request).GetAwaiter().GetResult();
        }
        catch (HttpRequestException exception)
        {
            throw new CliException($"cannot reach server {_http.BaseAddress}: {exception.Message}", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var detail = ProblemDetail(body) ?? response.ReasonPhrase ?? "request failed";
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new CliException("authentication required; run `p27 login --server <url>`, or pass --token or --dev-user"),
                _ => new CliException(detail),
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

    private static T Read<T>(HttpResponseMessage response)
    {
        using (response)
        {
            return response.Content.ReadFromJsonAsync<T>(JsonOptions).GetAwaiter().GetResult()
                ?? throw new CliException("the server returned an empty response");
        }
    }
}
