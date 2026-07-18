using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class FieldTools(IProjectSession session)
{
    [McpServerTool(Name = "field_write"), Description(
        "Defines, edits, or removes a custom field. `op` selects the shape of the other parameters:\n" +
        "- define: slot (required, e.g. \"text1\", \"cost3\"), alias (display name), formula (engine expression; " +
        "omit for a stored-value field), indicators (graphical indicator rules)\n" +
        "- remove: field (required, slot id or alias)")]
    public async Task<string> FieldWrite(
        [Description("define|remove")] string op,
        string? slot = null,
        string? alias = null,
        [Description("Formula source in the engine's expression syntax; omit for a plain stored-value field.")] string? formula = null,
        IReadOnlyList<CommandIndicatorRule>? indicators = null,
        string? field = null,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "DEFINE" => new DefineCustomFieldCommand
            {
                Slot = slot ?? throw new ArgumentException("slot is required for op=define"),
                Alias = alias,
                Formula = formula,
                Indicators = indicators,
            },
            "REMOVE" => new RemoveCustomFieldCommand { Field = field ?? throw new ArgumentException("field is required for op=remove") },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
