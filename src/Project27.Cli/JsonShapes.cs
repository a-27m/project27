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
    IReadOnlyList<PredecessorJson> Predecessors,
    TaskType Type,
    bool EffortDriven,
    bool IgnoresResourceCalendars,
    string Work,
    decimal Cost,
    decimal FixedCost,
    CostAccrual FixedCostAccrual,
    IReadOnlyList<AssignmentJson> Assignments,
    int PercentComplete,
    DateTime? ActualStart,
    DateTime? ActualFinish,
    BaselineJson? Baseline);

internal sealed record BaselineJson(DateTime? Start, DateTime? Finish, string Duration, string Work, decimal Cost);

internal sealed record EvmJson(
    int Id,
    string Name,
    decimal Bac,
    decimal Bcws,
    decimal Bcwp,
    decimal Acwp,
    decimal Sv,
    decimal Cv,
    decimal? Spi,
    decimal? Cpi,
    decimal Eac,
    decimal Vac,
    decimal? Tcpi);

internal sealed record AssignmentJson(
    int TaskUid,
    int TaskId,
    string Task,
    int ResourceUid,
    string Resource,
    ResourceType ResourceType,
    string Units,
    string? Work,
    WorkContour Contour,
    string? Delay,
    CostRateTableId RateTable,
    DateTime? Start,
    DateTime? Finish,
    decimal Cost,
    RateUnit? UnitsPer,
    decimal? Quantity,
    string? ActualWork,
    decimal? ActualCost);

internal sealed record RateEntryJson(DateTime? From, string StandardRate, string OvertimeRate, decimal CostPerUse);

internal sealed record RateTableJson(CostRateTableId Table, IReadOnlyList<RateEntryJson> Entries);

internal sealed record ResourceJson(
    int Uid,
    string Name,
    ResourceType Type,
    string? Initials,
    string? Group,
    string? MaxUnits,
    string? MaterialLabel,
    CostAccrual Accrual,
    string? Calendar,
    string Rate,
    IReadOnlyList<RateTableJson> RateTables,
    int Assignments);

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
    IReadOnlyList<string> Calendars,
    int Resources,
    string Work,
    decimal Cost,
    DateTime? StatusDate);

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
        ],
        Type: task.Type,
        EffortDriven: task.IsEffortDriven,
        IgnoresResourceCalendars: task.IgnoresResourceCalendars,
        Work: Render.WorkHours(task.WorkMinutes),
        Cost: task.Cost,
        FixedCost: task.FixedCost,
        FixedCostAccrual: task.FixedCostAccrual,
        Assignments: [.. task.Assignments.Select(ForAssignment)],
        PercentComplete: task.PercentComplete,
        ActualStart: task.ActualStart,
        ActualFinish: task.ActualFinish,
        Baseline: task.Baseline() is { } baseline
            ? new BaselineJson(
                baseline.Start,
                baseline.Finish,
                Duration.FromMinutes(baseline.DurationMinutes, DurationUnit.Days, settings).ToString(),
                Render.WorkHours(baseline.WorkMinutes),
                baseline.Cost)
            : null);

    public static EvmJson ForEvm(int id, string name, EarnedValueData data) => new(
        id, name, data.Bac, data.Bcws, data.Bcwp, data.Acwp, data.Sv, data.Cv, data.Spi, data.Cpi, data.Eac, data.Vac, data.Tcpi);

    public static AssignmentJson ForAssignment(Assignment assignment) => new(
        TaskUid: assignment.Task.UniqueId,
        TaskId: assignment.Task.RowNumber,
        Task: assignment.Task.Name,
        ResourceUid: assignment.Resource.UniqueId,
        Resource: assignment.Resource.Name,
        ResourceType: assignment.Resource.Type,
        Units: Render.Units(assignment),
        Work: assignment.Resource.Type == ResourceType.Work ? Render.WorkHours(assignment.WorkMinutes) : null,
        Contour: assignment.Contour,
        Delay: assignment.DelayMinutes > 0 ? Render.WorkHours(assignment.DelayMinutes) : null,
        RateTable: assignment.RateTable,
        Start: assignment.Start,
        Finish: assignment.Finish,
        Cost: assignment.Cost,
        UnitsPer: assignment.MaterialRateUnit,
        Quantity: assignment.MaterialRateUnit is null ? null : assignment.MaterialQuantity,
        ActualWork: assignment.ActualWorkMinutes is { } actualWork ? Render.WorkHours(actualWork) : null,
        ActualCost: assignment.ActualCost);

    public static ResourceJson ForResource(Resource resource) => new(
        Uid: resource.UniqueId,
        Name: resource.Name,
        Type: resource.Type,
        Initials: resource.Initials,
        Group: resource.Group,
        MaxUnits: resource.Type == ResourceType.Work ? Render.Num(resource.MaxUnits * 100m) + "%" : null,
        MaterialLabel: resource.MaterialLabel,
        Accrual: resource.Accrual,
        Calendar: resource.Calendar?.Name,
        Rate: Render.RateText(resource.StandardRate, resource.Type),
        RateTables:
        [
            .. Enum.GetValues<CostRateTableId>()
                .Select(id => (Id: id, Table: resource.RateTable(id)))
                .Where(t => t.Table.Entries.Count > 1 || t.Table.Entries[0].StandardRate != Rate.Zero
                    || t.Table.Entries[0].OvertimeRate != Rate.Zero || t.Table.Entries[0].CostPerUse != 0m)
                .Select(t => new RateTableJson(
                    t.Id,
                    [
                        .. t.Table.Entries.Select(e => new RateEntryJson(
                            e.EffectiveFrom == DateTime.MinValue ? null : e.EffectiveFrom,
                            Render.RateText(e.StandardRate, resource.Type),
                            Render.RateText(e.OvertimeRate, resource.Type),
                            e.CostPerUse)),
                    ])),
        ],
        Assignments: resource.Assignments.Count);

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
            Calendars: [.. project.Calendars.Select(c => c.Name)],
            Resources: project.Resources.Count,
            Work: Render.WorkHours(project.TotalWorkMinutes),
            Cost: project.TotalCost,
            StatusDate: project.StatusDate);
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
