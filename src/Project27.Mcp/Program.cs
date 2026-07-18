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

var arguments = ParseArguments(args);
var serverUrl = arguments.GetValueOrDefault("server") ?? Environment.GetEnvironmentVariable("P27_SERVER");

SessionConnection connection;
string? initialReference;
if (!string.IsNullOrWhiteSpace(serverUrl))
{
    var token = arguments.GetValueOrDefault("token") ?? Environment.GetEnvironmentVariable("P27_TOKEN");
    var devUser = arguments.GetValueOrDefault("dev-user") ?? Environment.GetEnvironmentVariable("P27_DEV_USER");
    connection = new RemoteConnection(serverUrl, token, devUser);
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
    await sessionHost.OpenProjectAsync(initialReference, CancellationToken.None);
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
            return await next(context, cancellationToken);
        }
        catch (Exception exception) when (exception is Project27.Mcp.Session.ProjectSessionException or ArgumentException
            or KeyNotFoundException or Project27.Core.Commands.CommandException)
        {
            // These are user-facing validation/state messages, not internals — the SDK otherwise
            // swallows all tool exceptions to a generic "An error occurred" string (McpException
            // is the one type whose Message reaches the client).
            throw new ModelContextProtocol.McpException(exception.Message);
        }
    }));

var app = builder.Build();
try
{
    await app.RunAsync();
}
finally
{
    await sessionHost.DisposeAsync();
}

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
