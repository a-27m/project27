using Project27.Core.Commands;

namespace Project27.Mcp.Session;

/// <summary>
/// The one project this MCP server process operates on, for either a local `.p27` file
/// (<see cref="LocalProjectSession"/>) or a checked-out project on a Project27.Server
/// (<see cref="RemoteProjectSession"/>). Tool classes depend only on this interface so the
/// same tool code works in both modes (D1/D4: engine parity across hosts).
/// </summary>
public interface IProjectSession : IAsyncDisposable
{
    public Task<ProjectSummary> GetProjectAsync(CancellationToken cancellationToken);

    public Task<TaskView> ListTasksAsync(
        IReadOnlyList<string>? fields, string? table, string? filter, string? sort, string? groupBy, CancellationToken cancellationToken);

    public Task<IReadOnlyList<ResourceSummary>> ListResourcesAsync(CancellationToken cancellationToken);

    public Task<IReadOnlyList<TaskDriver>> GetTaskDriversAsync(int uid, CancellationToken cancellationToken);

    public Task<UsageResult> GetUsageAsync(bool weekly, CancellationToken cancellationToken);

    public Task<string> GetReportAsync(string name, CancellationToken cancellationToken);

    /// <summary>Applies a batch of commands, recalculates once, and persists. Throws <see cref="CommandException"/> on failure.</summary>
    public Task<CommandResult> ApplyAsync(IReadOnlyList<ProjectCommand> commands, CancellationToken cancellationToken);
}
