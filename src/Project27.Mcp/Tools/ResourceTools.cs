using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class ResourceTools(IProjectSession session)
{
    [McpServerTool(Name = "resource_write"), Description(
        "Creates, edits, or removes a resource, or edits its cost-rate table. `op` selects the shape of the other " +
        "parameters:\n" +
        "- add: name (required), type, maxUnits, rate (e.g. \"50/h\"), materialLabel, calendar, initials, group\n" +
        "- set: resource (required, by name), any of name/maxUnits/materialLabel/calendar/clearCalendar/initials/" +
        "group/accrual to change\n" +
        "- remove: resource (required)\n" +
        "- setRate: resource (required), rateTable (default Standard), rateFrom (null = base entry), rate, " +
        "overtimeRate, costPerUse\n" +
        "- removeRate: resource (required), rateTable, rateFrom (required)")]
    public async Task<string> ResourceWrite(
        [Description("add|set|remove|setRate|removeRate")] string op,
        string? name = null,
        string? resource = null,
        ResourceType type = ResourceType.Work,
        decimal? maxUnits = null,
        string? rate = null,
        string? materialLabel = null,
        string? calendar = null,
        bool clearCalendar = false,
        string? initials = null,
        string? group = null,
        CostAccrual? accrual = null,
        CostRateTableId rateTable = CostRateTableId.A,
        DateTime? rateFrom = null,
        string? overtimeRate = null,
        decimal? costPerUse = null,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "ADD" => new AddResourceCommand
            {
                Name = name ?? throw new ArgumentException("name is required for op=add"),
                Type = type,
                MaxUnits = maxUnits,
                Rate = rate,
                MaterialLabel = materialLabel,
                Calendar = calendar,
                Initials = initials,
                Group = group,
            },
            "SET" => new SetResourceCommand
            {
                Resource = resource ?? throw new ArgumentException("resource is required for op=set"),
                Name = name,
                MaxUnits = maxUnits,
                MaterialLabel = materialLabel,
                Calendar = calendar,
                ClearCalendar = clearCalendar,
                Initials = initials,
                Group = group,
                Accrual = accrual,
            },
            "REMOVE" => new RemoveResourceCommand { Resource = resource ?? throw new ArgumentException("resource is required for op=remove") },
            "SETRATE" => new SetResourceRateCommand
            {
                Resource = resource ?? throw new ArgumentException("resource is required for op=setRate"),
                Table = rateTable,
                From = rateFrom,
                Rate = rate,
                OvertimeRate = overtimeRate,
                CostPerUse = costPerUse,
            },
            "REMOVERATE" => new RemoveResourceRateCommand
            {
                Resource = resource ?? throw new ArgumentException("resource is required for op=removeRate"),
                Table = rateTable,
                From = rateFrom ?? throw new ArgumentException("rateFrom is required for op=removeRate"),
            },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
