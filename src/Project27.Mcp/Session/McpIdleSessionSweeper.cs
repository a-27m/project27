using Microsoft.Extensions.Hosting;

namespace Project27.Mcp.Session;

/// <summary>
/// Periodically evicts <see cref="McpSessionRegistry"/> entries idle past <paramref
/// name="idleAfter"/> — the registry itself never removes anything on its own, since the
/// Streamable HTTP transport gives application code no "session closed" hook to drive that from
/// (see <see cref="McpSessionRegistry"/>'s doc comment for why that matters).
/// </summary>
public sealed class McpIdleSessionSweeper(McpSessionRegistry registry, TimeSpan idleAfter) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await registry.PruneIdleAsync(idleAfter, stoppingToken).ConfigureAwait(false);
        }
    }
}
