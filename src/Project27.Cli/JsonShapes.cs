using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

// JSON output DTOs (docs/spec/03-persistence-cli.md §2.4). Serialized with camelCase
// names and camelCase string enums; shapes grow with the field catalog in later phases.

internal sealed record PredecessorJson(int Uid, int Id, DependencyType Type, string? Lag);

internal sealed record SegmentJson(DateTime Start, DateTime Finish);

internal sealed record TaskJson(
    int Uid,
    int Id,
    string Name,
    int OutlineLevel,
    string Wbs,
    bool Summary,
    bool Milestone,
    bool Recurring,
    TaskMode Mode,
    bool Active,
    bool Critical,
    string Duration,
    bool Estimated,
    DateTime? Start,
    DateTime? Finish,
    DateTime? EarlyStart,
    DateTime? EarlyFinish,
    DateTime? LateStart,
    DateTime? LateFinish,
    string? TotalSlack,
    string? FreeSlack,
    ConstraintType Constraint,
    DateTime? ConstraintDate,
    DateTime? Deadline,
    int Priority,
    string? Calendar,
    DateTime? ManualStart,
    DateTime? ManualFinish,
    IReadOnlyList<SegmentJson> Segments,
    IReadOnlyList<PredecessorJson> Predecessors);

internal sealed record ProjectJson(
    Guid Id,
    string Name,
    ScheduleFrom ScheduleFrom,
    DateTime Start,
    DateTime? Finish,
    string Calendar,
    string CriticalSlack,
    int MinutesPerDay,
    int MinutesPerWeek,
    decimal DaysPerMonth,
    DayOfWeek WeekStartsOn,
    string DayStart,
    string DayEnd,
    int Tasks,
    IReadOnlyList<string> Calendars);

internal sealed record LinkJson(
    int PredecessorUid,
    int PredecessorId,
    string PredecessorName,
    int SuccessorUid,
    int SuccessorId,
    string SuccessorName,
    DependencyType Type,
    string? Lag);

internal sealed record CalendarDayJson(string Day, string Hours);

internal sealed record CalendarExceptionJson(
    string Name,
    DateOnly From,
    DateOnly? To,
    string Hours,
    string? Recur,
    int? Times);

internal sealed record WorkWeekJson(string Name, DateOnly From, DateOnly To, IReadOnlyList<CalendarDayJson> Days);

internal sealed record CalendarJson(
    Guid Id,
    string Name,
    string? Base,
    IReadOnlyList<CalendarDayJson> Week,
    IReadOnlyList<CalendarExceptionJson> Exceptions,
    IReadOnlyList<WorkWeekJson> WorkWeeks);

internal sealed record RemovedJson(string Kind, string Name, int? Uid = null);

internal static class JsonShapes
{
    private static readonly DayOfWeek[] WeekDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static TaskJson ForTask(ProjectTask task, TimeSettings settings) => new(
        Uid: task.UniqueId,
        Id: task.RowNumber,
        Name: task.Name,
        OutlineLevel: task.OutlineLevel,
        Wbs: task.Wbs,
        Summary: task.IsSummary,
        Milestone: task.IsMilestone,
        Recurring: task.IsRecurring,
        Mode: task.Mode,
        Active: task.IsActive,
        Critical: task.IsCritical,
        Duration: Render.DurationText(task, settings),
        Estimated: task.IsEstimated,
        Start: task.Start,
        Finish: task.Finish,
        EarlyStart: task.EarlyStart,
        EarlyFinish: task.EarlyFinish,
        LateStart: task.LateStart,
        LateFinish: task.LateFinish,
        TotalSlack: Render.MinutesAsDays(task.TotalSlackMinutes, settings),
        FreeSlack: Render.MinutesAsDays(task.FreeSlackMinutes, settings),
        Constraint: task.Constraint,
        ConstraintDate: task.ConstraintDate,
        Deadline: task.Deadline,
        Priority: task.Priority,
        Calendar: task.Calendar?.Name,
        ManualStart: task.ManualStart,
        ManualFinish: task.ManualFinish,
        Segments: [.. task.Segments.Select(s => new SegmentJson(s.Start, s.Finish))],
        Predecessors:
        [
            .. task.Predecessors.Select(d => new PredecessorJson(
                d.Predecessor.UniqueId, d.Predecessor.RowNumber, d.Type, Render.LagText(d.Lag, settings))),
        ]);

    public static ProjectJson ForProject(Project project)
    {
        var settings = project.TimeSettings;
        return new(
            Id: project.Id,
            Name: project.Name,
            ScheduleFrom: project.ScheduleFrom,
            Start: project.StartDate,
            Finish: project.FinishDate,
            Calendar: project.Calendar.Name,
            CriticalSlack: Render.MinutesAsDays(project.CriticalSlackThresholdMinutes, settings)!,
            MinutesPerDay: settings.MinutesPerDay,
            MinutesPerWeek: settings.MinutesPerWeek,
            DaysPerMonth: settings.DaysPerMonth,
            WeekStartsOn: settings.WeekStartsOn,
            DayStart: Render.Time(settings.DefaultStartTime),
            DayEnd: Render.Time(settings.DefaultEndTime),
            Tasks: project.Tasks.Count,
            Calendars: [.. project.Calendars.Select(c => c.Name)]);
    }

    public static LinkJson ForLink(TaskDependency dependency, TimeSettings settings) => new(
        PredecessorUid: dependency.Predecessor.UniqueId,
        PredecessorId: dependency.Predecessor.RowNumber,
        PredecessorName: dependency.Predecessor.Name,
        SuccessorUid: dependency.Successor.UniqueId,
        SuccessorId: dependency.Successor.RowNumber,
        SuccessorName: dependency.Successor.Name,
        Type: dependency.Type,
        Lag: Render.LagText(dependency.Lag, settings));

    public static CalendarJson ForCalendar(WorkCalendar calendar) => new(
        Id: calendar.Id,
        Name: calendar.Name,
        Base: calendar.BaseCalendar?.Name,
        Week: DaysOf(calendar.DefaultWeek),
        Exceptions:
        [
            .. calendar.Exceptions.Select(e => new CalendarExceptionJson(
                e.Name,
                e.Start,
                e.End,
                Render.ScheduleText(e.Schedule),
                e.Recurrence is null ? null : Render.RecurrenceSpec(e.Recurrence),
                e.Occurrences)),
        ],
        WorkWeeks: [.. calendar.WorkWeeks.Select(w => new WorkWeekJson(w.Name, w.Start, w.End, DaysOf(w.Pattern)))]);

    internal static List<CalendarDayJson> DaysOf(WeeklyPattern pattern) =>
    [
        .. WeekDays.Select(day => new CalendarDayJson(
            Render.DayName(day),
            pattern[day] is { } schedule ? Render.ScheduleText(schedule) : "inherit")),
    ];
}
