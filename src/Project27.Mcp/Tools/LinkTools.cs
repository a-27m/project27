using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class LinkTools(IProjectSession session)
{
    [McpServerTool(Name = "link_write"), Description(
        "Adds, edits, or removes a dependency between two tasks. `op` selects the shape of the other parameters:\n" +
        "- link: predecessorUid, successorUid (required), type (default finishToStart), lagKind/lagValue\n" +
        "- set: predecessorUid, successorUid (required), and any of type/lagKind+lagValue to change\n" +
        "- unlink: predecessorUid, successorUid (required)")]
    public async Task<string> LinkWrite(
        [Description("link|set|unlink")] string op,
        int predecessorUid,
        int successorUid,
        DependencyType? type = null,
        [Description("Lag unit: Days, ElapsedDays, PercentOfDuration, etc. Pair with lagValue.")] LagKind? lagKind = null,
        decimal? lagValue = null,
        CancellationToken cancellationToken = default)
    {
        var lag = lagKind is { } kind ? new CommandLag(kind, lagValue ?? 0) : null;
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "LINK" => new LinkCommand { PredecessorUid = predecessorUid, SuccessorUid = successorUid, Type = type ?? DependencyType.FinishToStart, Lag = lag },
            "SET" => new SetLinkCommand { PredecessorUid = predecessorUid, SuccessorUid = successorUid, Type = type, Lag = lag },
            "UNLINK" => new UnlinkCommand { PredecessorUid = predecessorUid, SuccessorUid = successorUid },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
