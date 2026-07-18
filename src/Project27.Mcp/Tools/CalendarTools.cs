using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class CalendarTools(IProjectSession session)
{
    [McpServerTool(Name = "calendar_write"), Description(
        "Creates or edits a working calendar: base calendars, weekly day patterns, dated exceptions, and " +
        "recurring work weeks. `op` selects the shape of the other parameters:\n" +
        "- addCalendar: name (required, the new calendar's name), baseCalendar, preset (standard|24h|night-shift, " +
        "ignored when baseCalendar is given)\n" +
        "- removeCalendar: calendar (required)\n" +
        "- setDay: calendar, day (required); off=true for non-working, or intervals for working hours, or omit both " +
        "to inherit from the base calendar\n" +
        "- setBase: calendar (required); baseCalendar (omit for standalone)\n" +
        "- addException: calendar, name, exceptionFrom (required); exceptionTo for a range; intervals (omit/empty " +
        "= day off); recurrence + times to repeat it\n" +
        "- removeException: calendar, name (required)\n" +
        "- addWorkWeek: calendar, name, workWeekFrom, workWeekTo (required); days maps each weekday to its working " +
        "intervals (empty list = day off; a day absent from the map inherits)\n" +
        "- removeWorkWeek: calendar, name (required)")]
    public async Task<string> CalendarWrite(
        [Description("addCalendar|removeCalendar|setDay|setBase|addException|removeException|addWorkWeek|removeWorkWeek")] string op,
        string? calendar = null,
        string? name = null,
        string? baseCalendar = null,
        [Description("addCalendar only: standard|24h|night-shift.")] string? preset = null,
        DayOfWeek? day = null,
        bool off = false,
        [Description("Working intervals as [{start:\"08:00\", end:\"12:00\"}, ...].")] IReadOnlyList<CommandInterval>? intervals = null,
        DateOnly? exceptionFrom = null,
        DateOnly? exceptionTo = null,
        CommandRecurrence? recurrence = null,
        int? times = null,
        DateOnly? workWeekFrom = null,
        DateOnly? workWeekTo = null,
        [Description("Per-weekday interval overrides for addWorkWeek; a day absent from the map inherits.")]
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<CommandInterval>>? days = null,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "ADDCALENDAR" => new AddCalendarCommand
            {
                Name = name ?? throw new ArgumentException("name is required for op=addCalendar"),
                BaseCalendar = baseCalendar,
                Preset = preset,
            },
            "REMOVECALENDAR" => new RemoveCalendarCommand { Calendar = calendar ?? throw new ArgumentException("calendar is required for op=removeCalendar") },
            "SETDAY" => new SetCalendarDayCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=setDay"),
                Day = day ?? throw new ArgumentException("day is required for op=setDay"),
                Off = off,
                Intervals = intervals,
            },
            "SETBASE" => new SetCalendarBaseCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=setBase"),
                BaseCalendar = baseCalendar,
            },
            "ADDEXCEPTION" => new AddCalendarExceptionCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=addException"),
                Name = name ?? throw new ArgumentException("name is required for op=addException"),
                From = exceptionFrom ?? throw new ArgumentException("exceptionFrom is required for op=addException"),
                To = exceptionTo,
                Intervals = intervals,
                Recurrence = recurrence,
                Times = times,
            },
            "REMOVEEXCEPTION" => new RemoveCalendarExceptionCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=removeException"),
                Name = name ?? throw new ArgumentException("name is required for op=removeException"),
            },
            "ADDWORKWEEK" => new AddWorkWeekCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=addWorkWeek"),
                Name = name ?? throw new ArgumentException("name is required for op=addWorkWeek"),
                From = workWeekFrom ?? throw new ArgumentException("workWeekFrom is required for op=addWorkWeek"),
                To = workWeekTo ?? throw new ArgumentException("workWeekTo is required for op=addWorkWeek"),
                Days = days,
            },
            "REMOVEWORKWEEK" => new RemoveWorkWeekCommand
            {
                Calendar = calendar ?? throw new ArgumentException("calendar is required for op=removeWorkWeek"),
                Name = name ?? throw new ArgumentException("name is required for op=removeWorkWeek"),
            },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
