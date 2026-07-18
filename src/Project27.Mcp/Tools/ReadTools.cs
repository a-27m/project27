using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core.Fields;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class ReadTools(IProjectSession session)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] AllFieldKeys = [.. FieldCatalog.All.Select(f => f.Key)];

    [McpServerTool(Name = "get_project"), Description(
        "Project-level summary: name, start/finish, calendar, totals, resources, custom fields, stats, and earned value.")]
    public async Task<string> GetProject(CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await session.GetProjectAsync(cancellationToken), JsonOptions);

    [McpServerTool(Name = "list_tasks"), Description(
        "Lists tasks as a filtered/sorted/grouped view. Use `table` for a built-in field set (entry, schedule, cost, " +
        "work, tracking, variance, evm, summary) or `fields` for an explicit comma-separated field key list. " +
        "`filter` uses the engine's expression syntax, e.g. \"critical = true and duration > 3d\". " +
        "`sort` is a comma-separated \"field[:desc]\" list. `groupBy` is a single field key.")]
    public async Task<string> ListTasks(
        [Description("Comma-separated field keys; overrides `table` when given.")] string? fields = null,
        [Description("Built-in table name: entry, schedule, cost, work, tracking, variance, evm, summary. Default: entry.")] string? table = null,
        [Description("Filter expression, e.g. \"critical = true\".")] string? filter = null,
        [Description("Sort spec, e.g. \"start,priority:desc\".")] string? sort = null,
        [Description("Field key to group rows by.")] string? groupBy = null,
        CancellationToken cancellationToken = default)
    {
        var fieldKeys = string.IsNullOrWhiteSpace(fields)
            ? null
            : (IReadOnlyList<string>)[.. fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        var result = await session.ListTasksAsync(fieldKeys, table, filter, sort, groupBy, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "get_task"), Description("Every built-in field for one task, addressed by uid.")]
    public async Task<string> GetTask(
        [Description("The task's stable uid (not its row number).")] int uid, CancellationToken cancellationToken = default)
    {
        var view = await session.ListTasksAsync(AllFieldKeys, null, null, null, null, cancellationToken);
        var row = view.Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Uid == uid)
            ?? throw new KeyNotFoundException($"No task with uid {uid}.");
        return JsonSerializer.Serialize(row, JsonOptions);
    }

    [McpServerTool(Name = "list_resources"), Description("All resources: uid, name, type, max units, standard rate.")]
    public async Task<string> ListResources(CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await session.ListResourcesAsync(cancellationToken), JsonOptions);

    [McpServerTool(Name = "get_task_drivers"), Description(
        "Explains why a task is scheduled where it is: driving predecessor links, constraints, calendar, or leveling delay.")]
    public async Task<string> GetTaskDrivers(
        [Description("The task's stable uid.")] int uid, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await session.GetTaskDriversAsync(uid, cancellationToken), JsonOptions);

    [McpServerTool(Name = "get_usage"), Description("Time-phased work and cost per task, bucketed by day or week.")]
    public async Task<string> GetUsage(
        [Description("\"day\" or \"week\"; default week.")] string? granularity = null, CancellationToken cancellationToken = default)
    {
        var weekly = (granularity ?? "week").Trim().ToUpperInvariant() switch
        {
            "DAY" => false,
            "WEEK" => true,
            _ => throw new ArgumentException($"Unknown granularity '{granularity}'; use day or week."),
        };
        return JsonSerializer.Serialize(await session.GetUsageAsync(weekly, cancellationToken), JsonOptions);
    }

    [McpServerTool(Name = "get_report"), Description(
        "Renders a built-in report as self-contained HTML: overview, critical, late, resources, costs, upcoming.")]
    public async Task<string> GetReport(
        [Description("Report name: overview, critical, late, resources, costs, upcoming.")] string name, CancellationToken cancellationToken = default)
        => await session.GetReportAsync(name, cancellationToken);
}
