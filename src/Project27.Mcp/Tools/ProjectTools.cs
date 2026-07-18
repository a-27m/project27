using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectTools(IProjectSession session)
{
    [McpServerTool(Name = "project_write"), Description(
        "Edits project-wide settings: name, start date, scheduling direction, default calendar, status date, " +
        "working-time definitions, and the critical-slack threshold. Only the parameters given are changed.")]
    public async Task<string> ProjectWrite(
        string? name = null,
        DateTime? start = null,
        ScheduleFrom? scheduleFrom = null,
        [Description("Default project calendar, by name.")] string? calendar = null,
        DateTime? statusDate = null,
        bool clearStatusDate = false,
        int? minutesPerDay = null,
        int? minutesPerWeek = null,
        decimal? daysPerMonth = null,
        DayOfWeek? weekStartsOn = null,
        [Description("\"HH:mm\" default day start.")] string? dayStart = null,
        [Description("\"HH:mm\" default day end.")] string? dayEnd = null,
        [Description("Engine duration syntax; total-slack threshold for criticality, e.g. \"0d\".")] string? criticalSlack = null,
        CancellationToken cancellationToken = default)
    {
        var command = new SetProjectCommand
        {
            Name = name,
            Start = start,
            ScheduleFrom = scheduleFrom,
            Calendar = calendar,
            StatusDate = statusDate,
            ClearStatusDate = clearStatusDate,
            MinutesPerDay = minutesPerDay,
            MinutesPerWeek = minutesPerWeek,
            DaysPerMonth = daysPerMonth,
            WeekStartsOn = weekStartsOn,
            DayStart = dayStart,
            DayEnd = dayEnd,
            CriticalSlack = criticalSlack,
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
