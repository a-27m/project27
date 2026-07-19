using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Project27.Mcp.Session;
using Project27.Storage;

// Mode selection mirrors the CLI (D8/docs/progress.md): --file/P27_FILE for a local .p27 file
// (falling back to the sole .p27 in the current directory), or --server/P27_SERVER +
// --project/P27_PROJECT for a checked-out server project, authenticated with --dev-user/
// P27_DEV_USER or --token/P27_TOKEN. Unlike the CLI, a specific project is optional at
// startup: a chat client may not have one to point at yet, in which case the session starts
// idle and the create_project/open_project tools establish it later in the conversation.
//
// --transport/P27_MCP_TRANSPORT picks stdio (default, one process = one client = one session,
// same as always) or http (network-reachable, many concurrent sessions in one process). The
// two shapes diverge enough — one session resolved once at launch vs. many sessions resolved
// per HTTP connection — that they're built by separate functions below rather than threaded
// through shared branches; see docs/spec/14-mcp-server.md "HTTP transport" for the full design.

var arguments = ParseArguments(args);
var transport = (arguments.GetValueOrDefault("transport") ?? Environment.GetEnvironmentVariable("P27_MCP_TRANSPORT") ?? "stdio").ToLowerInvariant();

switch (transport)
{
    case "stdio":
        await RunStdioAsync(arguments, args).ConfigureAwait(false);
        break;
    case "http":
        await RunHttpAsync(arguments).ConfigureAwait(false);
        break;
    default:
        throw new InvalidOperationException($"unknown --transport '{transport}'; expected 'stdio' or 'http'");
}

static async Task RunStdioAsync(Dictionary<string, string> arguments, string[] args)
{
    var serverUrl = arguments.GetValueOrDefault("server") ?? Environment.GetEnvironmentVariable("P27_SERVER");

    SessionConnection connection;
    string? initialReference;
    if (!string.IsNullOrWhiteSpace(serverUrl))
    {
        var token = arguments.GetValueOrDefault("token") ?? Environment.GetEnvironmentVariable("P27_TOKEN");
        var devUser = arguments.GetValueOrDefault("dev-user") ?? Environment.GetEnvironmentVariable("P27_DEV_USER");
        connection = new RemoteConnection(serverUrl, () => token, devUser);
        initialReference = arguments.GetValueOrDefault("project") ?? Environment.GetEnvironmentVariable("P27_PROJECT");
    }
    else
    {
        var baseDirectory = Environment.CurrentDirectory;
        connection = new LocalConnection(baseDirectory);
        initialReference = arguments.GetValueOrDefault("file")
            ?? Environment.GetEnvironmentVariable("P27_FILE")
            ?? TryResolveSoleLocalFile(baseDirectory);
    }

    var sessionHost = new ProjectSessionHost(connection);
    if (!string.IsNullOrWhiteSpace(initialReference))
    {
        await sessionHost.OpenProjectAsync(initialReference, CancellationToken.None).ConfigureAwait(false);
    }

    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(sessionHost);
    builder.Services.AddSingleton<IProjectSession>(sessionHost);
    builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()
        .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsUserFacing(exception))
            {
                throw new ModelContextProtocol.McpException(exception.Message);
            }
        }));

    var app = builder.Build();
    try
    {
        await app.RunAsync().ConfigureAwait(false);
    }
    finally
    {
        await sessionHost.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Network-reachable, multi-session mode. Unlike stdio — where the process itself is launched
/// by whoever holds the credentials, so trust is implicit and one process always serves exactly
/// one client — an HTTP endpoint is a shared service: whoever can reach it drives every tool
/// call. Two things follow, both required rather than optional here (see docs/spec/14-mcp-server.md):
///
/// - The endpoint authenticates every request itself (JWT bearer against the same OIDC
///   `Auth:Authority`/`Auth:Audience` the Project27 server already trusts), instead of relying
///   purely on network placement.
/// - Each MCP session gets its own <see cref="ProjectSessionHost"/>, seeded with *that session's
///   caller's own bearer token* — forwarded downstream to the Project27 server — rather than one
///   fixed P27_TOKEN/P27_DEV_USER shared by every caller of the process. There is deliberately no
///   local-file mode over HTTP and no dev-user fallback: both would mean every caller of a shared
///   endpoint acts as the same backend identity.
/// </summary>
static async Task RunHttpAsync(Dictionary<string, string> arguments)
{
    var serverUrl = arguments.GetValueOrDefault("server") ?? Environment.GetEnvironmentVariable("P27_SERVER")
        ?? throw new InvalidOperationException(
            "--transport http requires --server or P27_SERVER: every session forwards its own " +
            "caller's bearer token to that Project27 server, so there is no local-file mode over HTTP.");

    var builder = WebApplication.CreateBuilder();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    var authority = builder.Configuration["Auth:Authority"]
        ?? throw new InvalidOperationException(
            "--transport http requires Auth:Authority (OIDC): every session authenticates with its " +
            "caller's own bearer token, validated against the same authority Project27.Server trusts, " +
            "then forwards that token downstream (docs/spec/14-mcp-server.md).");
    var audience = builder.Configuration["Auth:Audience"];

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddHttpContextAccessor();

    // One session (project + checkout lock + backend credential) per MCP session, keyed by the
    // "Mcp-Session-Id" header the Streamable HTTP transport sends on every request after the
    // initial handshake — not one per process. See McpSessionRegistry's doc comment for what this
    // buys, what it costs (a captured-once token, not refreshed mid-session), and why
    // McpIdleSessionSweeper exists (nothing here evicts on its own otherwise).
    var sessionIdleAfter = TimeSpan.FromMinutes(builder.Configuration.GetValue("Mcp:SessionIdleMinutes", 30));
    builder.Services.AddSingleton<McpSessionRegistry>();
    builder.Services.AddHostedService(sp => new McpIdleSessionSweeper(sp.GetRequiredService<McpSessionRegistry>(), sessionIdleAfter));
    builder.Services.AddScoped(sp =>
    {
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var registry = sp.GetRequiredService<McpSessionRegistry>();
        return registry.GetOrCreate(accessor, serverUrl);
    });
    builder.Services.AddScoped<IProjectSession>(sp => sp.GetRequiredService<ProjectSessionHost>());

    builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly()
        .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsUserFacing(exception))
            {
                throw new ModelContextProtocol.McpException(exception.Message);
            }
        }));

    var app = builder.Build();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();
    app.MapMcp().RequireAuthorization();

    await app.RunAsync().ConfigureAwait(false);
}

/// <summary>
/// These are user-facing validation/state messages, not internals — the SDK otherwise swallows
/// all tool exceptions to a generic "An error occurred" string (McpException is the one type
/// whose Message reaches the client).
/// </summary>
static bool IsUserFacing(Exception exception) => exception is ProjectSessionException or ArgumentException
    or KeyNotFoundException or Project27.Core.Commands.CommandException;

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            result[args[i][2..]] = args[i + 1];
            i++;
        }
    }

    return result;
}

/// <summary>The sole `.p27` in <paramref name="directory"/>, or null if there isn't exactly one to fall back to
/// (zero means "nothing to open yet" — the session starts idle; more than one is an ambiguous misconfiguration).</summary>
static string? TryResolveSoleLocalFile(string directory)
{
    var candidates = Directory.GetFiles(directory, "*" + SqliteProjectStore.FileExtension)
        .Where(f => f.EndsWith(SqliteProjectStore.FileExtension, StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToArray();
    return candidates.Length switch
    {
        1 => candidates[0],
        0 => null,
        _ => throw new InvalidOperationException($"several {SqliteProjectStore.FileExtension} files in {directory}; pass --file <path> or P27_FILE"),
    };
}
