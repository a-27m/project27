using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class AssignmentTools(IProjectSession session)
{
    [McpServerTool(Name = "assignment_write"), Description(
        "Assigns a resource to a task, edits an existing assignment, or removes it. `op` selects the shape of the " +
        "other parameters:\n" +
        "- assign: uid, resource (required), units, unitsPer (material rate unit), work (engine duration), cost\n" +
        "- set: uid, resource (required), any of units/work/contour/delay/rateTable/cost/unitsPer(+clearUnitsPer)/" +
        "actualWork(+clearActualWork)/actualCost(+clearActualCost) to change\n" +
        "- unassign: uid, resource (required)")]
    public async Task<string> AssignmentWrite(
        [Description("assign|set|unassign")] string op,
        int uid,
        string resource,
        decimal? units = null,
        RateUnit? unitsPer = null,
        bool clearUnitsPer = false,
        string? work = null,
        decimal? cost = null,
        WorkContour? contour = null,
        string? delay = null,
        CostRateTableId? rateTable = null,
        string? actualWork = null,
        bool clearActualWork = false,
        decimal? actualCost = null,
        bool clearActualCost = false,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "ASSIGN" => new AssignCommand { Uid = uid, Resource = resource, Units = units, UnitsPer = unitsPer, Work = work, Cost = cost },
            "SET" => new SetAssignmentCommand
            {
                Uid = uid,
                Resource = resource,
                Units = units,
                Work = work,
                Contour = contour,
                Delay = delay,
                RateTable = rateTable,
                Cost = cost,
                UnitsPer = unitsPer,
                ClearUnitsPer = clearUnitsPer,
                ActualWork = actualWork,
                ClearActualWork = clearActualWork,
                ActualCost = actualCost,
                ClearActualCost = clearActualCost,
            },
            "UNASSIGN" => new UnassignCommand { Uid = uid, Resource = resource },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
