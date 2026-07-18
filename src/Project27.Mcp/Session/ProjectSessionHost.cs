using Project27.Core.Commands;
using Project27.Storage;

namespace Project27.Mcp.Session;

/// <summary>Where new/existing projects are looked for: a local directory, or a server + credentials.</summary>
public abstract record SessionConnection;

public sealed record LocalConnection(string BaseDirectory) : SessionConnection;

public sealed record RemoteConnection(string ServerUrl, string? Token, string? DevUser) : SessionConnection;

/// <summary>
/// Holds the one project this MCP process operates on, established either eagerly at startup
/// (today's behavior: `--file`/`P27_FILE` or the sole `.p27` in the cwd; `--project`/`P27_PROJECT`
/// on a server) or lazily via the `create_project`/`open_project` tools — the shape a chat client
/// needs when there's no pre-existing file or project to point at. Forwards <see cref="IProjectSession"/>
/// reads/writes to whichever session is current, so existing tool classes need no changes.
/// </summary>
public sealed class ProjectSessionHost(SessionConnection connection) : IProjectSession
{
    private readonly object _gate = new();
    private IProjectSession? _current;
    private bool _reserved;

    private IProjectSession Current => _current ?? throw new ProjectSessionException(
        "No project is open yet. Call create_project to start a new one or open_project to attach to an existing one.");

    public async Task<ProjectSummary> CreateProjectAsync(string name, DateTime? start, string? path, CancellationToken cancellationToken)
    {
        ReserveSlot();
        IProjectSession session;
        try
        {
            session = connection switch
            {
                LocalConnection local => LocalProjectSession.Create(ResolveCreatePath(local, path, name), name, start),
                RemoteConnection remote => await RemoteProjectSession.CreateAsync(
                    remote.ServerUrl, name, start, remote.Token, remote.DevUser, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unknown session connection."),
            };
        }
        catch
        {
            ReleaseSlot();
            throw;
        }

        CommitSlot(session);
        return await session.GetProjectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectSummary> OpenProjectAsync(string reference, CancellationToken cancellationToken)
    {
        ReserveSlot();
        IProjectSession session;
        try
        {
            session = connection switch
            {
                LocalConnection local => LocalProjectSession.Open(ResolveOpenPath(local, reference)),
                RemoteConnection remote => await RemoteProjectSession.OpenAsync(
                    remote.ServerUrl, reference, remote.Token, remote.DevUser, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unknown session connection."),
            };
        }
        catch
        {
            ReleaseSlot();
            throw;
        }

        CommitSlot(session);
        return await session.GetProjectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fails fast, before any file/HTTP side effect, if a project is already open or being established.</summary>
    private void ReserveSlot()
    {
        lock (_gate)
        {
            if (_current is not null || _reserved)
            {
                throw new ProjectSessionException(
                    "A project is already open in this session; restart the MCP server to work with a different one.");
            }

            _reserved = true;
        }
    }

    private void CommitSlot(IProjectSession session)
    {
        lock (_gate)
        {
            _current = session;
        }
    }

    private void ReleaseSlot()
    {
        lock (_gate)
        {
            _reserved = false;
        }
    }

    private static string ResolveCreatePath(LocalConnection local, string? path, string name)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.IsPathRooted(path) ? path : Path.Combine(local.BaseDirectory, path);
        }

        var slug = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return Path.Combine(local.BaseDirectory, slug + SqliteProjectStore.FileExtension);
    }

    private static string ResolveOpenPath(LocalConnection local, string reference)
        => Path.IsPathRooted(reference) ? reference : Path.Combine(local.BaseDirectory, reference);

    public Task<ProjectSummary> GetProjectAsync(CancellationToken cancellationToken) => Current.GetProjectAsync(cancellationToken);

    public Task<TaskView> ListTasksAsync(
        IReadOnlyList<string>? fields, string? table, string? filter, string? sort, string? groupBy, CancellationToken cancellationToken)
        => Current.ListTasksAsync(fields, table, filter, sort, groupBy, cancellationToken);

    public Task<IReadOnlyList<ResourceSummary>> ListResourcesAsync(CancellationToken cancellationToken)
        => Current.ListResourcesAsync(cancellationToken);

    public Task<IReadOnlyList<TaskDriver>> GetTaskDriversAsync(int uid, CancellationToken cancellationToken)
        => Current.GetTaskDriversAsync(uid, cancellationToken);

    public Task<UsageResult> GetUsageAsync(bool weekly, CancellationToken cancellationToken) => Current.GetUsageAsync(weekly, cancellationToken);

    public Task<string> GetReportAsync(string name, CancellationToken cancellationToken) => Current.GetReportAsync(name, cancellationToken);

    public Task<CommandResult> ApplyAsync(IReadOnlyList<ProjectCommand> commands, CancellationToken cancellationToken)
        => Current.ApplyAsync(commands, cancellationToken);

    public ValueTask DisposeAsync() => _current?.DisposeAsync() ?? ValueTask.CompletedTask;
}
