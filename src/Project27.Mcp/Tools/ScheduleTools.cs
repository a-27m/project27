using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core.Commands;
using Project27.Core.Scheduling;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class ScheduleTools(IProjectSession session)
{
    [McpServerTool(Name = "schedule_write"), Description(
        "Project-wide scheduling operations: baselines, resource leveling, and rescheduling uncompleted work. " +
        "`op` selects the shape of the other parameters:\n" +
        "- setBaseline / clearBaseline: slot (0-10, default 0), uids (empty = whole project)\n" +
        "- level: order (priorityStandard by default), granularity (day by default), splitInProgress\n" +
        "- clearLeveling: (no parameters)\n" +
        "- reschedule: after (cutoff date; omit to use the project's status date)\n" +
        "Destructive ops (level, baselines, reschedule) clear any undo history on hosts that track it.")]
    public async Task<string> ScheduleWrite(
        [Description("setBaseline|clearBaseline|level|clearLeveling|reschedule")] string op,
        int slot = 0,
        IReadOnlyList<int>? uids = null,
        LevelingOrder? order = null,
        LevelingGranularity? granularity = null,
        bool? splitInProgress = null,
        DateTime? after = null,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "SETBASELINE" => new SetBaselineCommand { Slot = slot, Uids = uids ?? [] },
            "CLEARBASELINE" => new ClearBaselineCommand { Slot = slot, Uids = uids ?? [] },
            "LEVEL" => new LevelCommand { Order = order, Granularity = granularity, SplitInProgress = splitInProgress },
            "CLEARLEVELING" => new ClearLevelingCommand(),
            "RESCHEDULE" => new RescheduleCommand { After = after },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
