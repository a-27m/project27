using Project27.Core;
using Project27.Core.Time;

namespace Project27.Server;

// Computed-schedule projection for web/API consumers (docs/spec/07-web-foundation.md).
// A server-owned stopgap: the Core field catalog unifies projections in phase 9.

public sealed record ScheduleProjectDto(
    Guid Id,
    string Name,
    DateTime Start,
    DateTime? Finish,
    ScheduleFrom ScheduleFrom,
    int MinutesPerDay,
    int MinutesPerWeek,
    decimal DaysPerMonth,
    DayOfWeek WeekStartsOn,
    string DayStart,
    string DayEnd,
    decimal CriticalSlackMinutes,
    string Calendar,
    decimal TotalWorkMinutes,
    decimal TotalCost,
    DateTime? StatusDate,
    IReadOnlyList<string> Calendars,
    IReadOnlyList<ResourceSummaryDto> Resources,
    IReadOnlyList<CustomFieldSummaryDto> CustomFields,
    ProjectStatsData Stats);

public sealed record ResourceSummaryDto(
    int Uid,
    string Name,
    ResourceType Type,
    decimal MaxUnits,
    string Rate,
    string? Initials,
    string? Group,
    string? Calendar,
    string? MaterialLabel,
    CostAccrual Accrual,
    IReadOnlyList<CostRateTableDto> RateTables);

/// <summary>One of a resource's five cost-rate tables (A-E) with its effective-dated entries.</summary>
public sealed record CostRateTableDto(CostRateTableId Table, IReadOnlyList<CostRateEntryDto> Entries);

/// <summary>An effective-dated rate entry; <c>From</c> is null for the base entry (which cannot be removed).</summary>
public sealed record CostRateEntryDto(DateTime? From, string StandardRate, string OvertimeRate, decimal CostPerUse);

public sealed record CustomFieldSummaryDto(string Id, string? Alias, string Kind, bool HasFormula);

public sealed record ScheduleAssignmentDto(
    string Resource,
    ResourceType ResourceType,
    decimal Units,
    decimal WorkMinutes,
    WorkContour Contour,
    decimal DelayMinutes,
    CostRateTableId RateTable,
    decimal Cost,
    decimal CostInput,
    RateUnit? UnitsPer,
    decimal? ActualWorkMinutes,
    decimal? ActualCost);

public sealed record ScheduleSegmentDto(DateTime Start, DateTime Finish);

public sealed record SchedulePredecessorDto(int PredecessorUid, DependencyType Type, LagKind LagKind, decimal LagValue);

public sealed record ScheduleTaskDto(
    int Uid,
    int Row,
    string Name,
    int OutlineLevel,
    string Wbs,
    bool Summary,
    bool Milestone,
    bool Recurring,
    bool Critical,
    bool Active,
    TaskMode Mode,
    decimal DurationMinutes,
    decimal RemainingDurationMinutes,
    bool Estimated,
    DateTime? Start,
    DateTime? Finish,
    decimal? TotalSlackMinutes,
    decimal? FreeSlackMinutes,
    ConstraintType Constraint,
    DateTime? ConstraintDate,
    DateTime? Deadline,
    decimal WorkMinutes,
    decimal Cost,
    IReadOnlyList<ScheduleSegmentDto> Segments,
    IReadOnlyList<SchedulePredecessorDto> Predecessors,
    int PercentComplete,
    DateTime? ActualStart,
    DateTime? ActualFinish,
    DateTime? BaselineStart,
    DateTime? BaselineFinish,
    decimal? BaselineCost,
    decimal LevelingDelayMinutes,
    int Priority,
    int SpaceAfter,
    TaskType Type,
    bool EffortDriven,
    bool IgnoresResourceCalendars,
    decimal FixedCost,
    CostAccrual FixedCostAccrual,
    DateTime? ManualStart,
    DateTime? ManualFinish,
    string? Calendar,
    IReadOnlyList<ScheduleAssignmentDto> Assignments,
    IReadOnlyDictionary<string, object?>? CustomValues,
    bool HasDescription);

public sealed record ScheduleDto(int Version, ScheduleProjectDto Project, IReadOnlyList<ScheduleTaskDto> Tasks);

// Time-phased usage projection (docs/spec/09-views-fields.md §9c/9d).

public sealed record UsageBucketDto(DateOnly Date, decimal WorkMinutes, decimal Cost);

public sealed record UsageRowDto(
    int Uid,
    int Row,
    string Name,
    int OutlineLevel,
    bool Summary,
    IReadOnlyList<UsageBucketDto> Buckets,
    decimal TotalWorkMinutes,
    decimal TotalCost);

public sealed record UsageDto(int Version, string Granularity, DayOfWeek WeekStartsOn, IReadOnlyList<UsageRowDto> Rows);

public sealed record CommandsResponse(
    int Version,
    IReadOnlyList<int?> CreatedUids,
    ScheduleDto Schedule,
    IReadOnlyList<Core.Commands.ProjectCommand>? Inverse);

// View-engine projection (12p-1): the CLI's JSON shape, server-side.

public sealed record ViewFieldDto(string Key, string Caption, string Kind);

public sealed record ViewRowDto(int Uid, int Id, IReadOnlyDictionary<string, object?> Values);

public sealed record ViewGroupDto(string? Heading, IReadOnlyList<ViewRowDto> Rows);

public sealed record ViewDto(IReadOnlyList<ViewFieldDto> Fields, IReadOnlyList<ViewGroupDto> Groups);

public sealed record TaskDriverDto(string Kind, string Description, bool Binding, DateTime? Date, int? PredecessorUid);

public sealed record TaskDescriptionDto(string? Description);

// Calendar-detail projection (Task 9, web parity): the client otherwise only
// receives calendar names via ScheduleProjectDto.Calendars.

public sealed record CalendarRecurrenceDto(string Kind, int Every, string EndMode, int? Occurrences);

public sealed record CalendarExceptionDto(
    string Name,
    DateOnly Start,
    DateOnly? End,
    bool DayOff,
    IReadOnlyList<string> Intervals,
    CalendarRecurrenceDto? Recurrence);

public sealed record WorkWeekDto(string Name, DateOnly Start, DateOnly End, IReadOnlyDictionary<string, string> Days);

public sealed record CalendarDetailDto(
    string Name,
    string? Base,
    bool IsProjectDefault,
    int DirectTaskCount,
    int DirectResourceCount,
    IReadOnlyDictionary<string, string> Weekly,
    IReadOnlyList<CalendarExceptionDto> Exceptions,
    IReadOnlyList<WorkWeekDto> WorkWeeks);

public sealed record CalendarDayCellDto(DateOnly Date, bool Working, Core.Time.CalendarRuleSource Source, string? RuleName);

public sealed record CalendarMonthDto(int Year, int Month, IReadOnlyList<CalendarDayCellDto> Days);

public static class ScheduleProjection
{
    /// <summary>Time-phased work/cost per task; call after <see cref="Project.Recalculate"/>.</summary>
    public static UsageDto Usage(Project project, int version, bool weekly)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new UsageDto(
            version,
            weekly ? "week" : "day",
            project.TimeSettings.WeekStartsOn,
            [
                .. project.Tasks.Select(task =>
                {
                    var buckets = Core.Usage.Timephased.ForTask(task);
                    if (weekly)
                    {
                        buckets = Core.Usage.Timephased.ByWeek(buckets, project.TimeSettings.WeekStartsOn);
                    }

                    return new UsageRowDto(
                        task.UniqueId,
                        task.RowNumber,
                        task.Name,
                        task.OutlineLevel,
                        task.IsSummary,
                        [.. buckets.Select(b => new UsageBucketDto(b.Date, b.WorkMinutes, Math.Round(b.Cost, 2)))],
                        buckets.Sum(b => b.WorkMinutes),
                        Math.Round(buckets.Sum(b => b.Cost), 2));
                }),
            ]);
    }

    /// <summary>Projects a recalculated aggregate; call after <see cref="Project.Recalculate"/>.</summary>
    public static ScheduleDto From(Project project, int version)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ScheduleDto(
            version,
            new ScheduleProjectDto(
                project.Id,
                project.Name,
                project.StartDate,
                project.FinishDate,
                project.ScheduleFrom,
                project.TimeSettings.MinutesPerDay,
                project.TimeSettings.MinutesPerWeek,
                project.TimeSettings.DaysPerMonth,
                project.TimeSettings.WeekStartsOn,
                project.TimeSettings.DefaultStartTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                project.TimeSettings.DefaultEndTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                project.CriticalSlackThresholdMinutes,
                project.Calendar.Name,
                project.TotalWorkMinutes,
                project.TotalCost,
                project.StatusDate,
                [.. project.Calendars.Select(c => c.Name)],
                [
                    .. project.Resources.Select(r => new ResourceSummaryDto(
                        r.UniqueId,
                        r.Name,
                        r.Type,
                        r.MaxUnits,
                        r.StandardRate.ToString(),
                        r.Initials,
                        r.Group,
                        r.Calendar?.Name,
                        r.MaterialLabel,
                        r.Accrual,
                        [
                            .. Enum.GetValues<CostRateTableId>().Select(table => new CostRateTableDto(
                                table,
                                [
                                    .. r.RateTable(table).Entries.Select(entry => new CostRateEntryDto(
                                        entry.EffectiveFrom == DateTime.MinValue ? null : entry.EffectiveFrom,
                                        entry.StandardRate.ToString(),
                                        entry.OvertimeRate.ToString(),
                                        entry.CostPerUse)),
                                ])),
                        ])),
                ],
                [
                    .. project.CustomFields.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => new CustomFieldSummaryDto(
                        f.Id, f.Alias, f.Kind.ToString(), f.Formula is not null)),
                ],
                ProjectStats.For(project)),
            [
                .. project.Tasks.Select(task => new ScheduleTaskDto(
                    task.UniqueId,
                    task.RowNumber,
                    task.Name,
                    task.OutlineLevel,
                    task.Wbs,
                    task.IsSummary,
                    task.IsMilestone,
                    task.IsRecurring,
                    task.IsCritical,
                    task.IsActive,
                    task.Mode,
                    task.DurationMinutes,
                    task.RemainingMinutes,
                    task.IsEstimated,
                    task.Start,
                    task.Finish,
                    task.TotalSlackMinutes,
                    task.FreeSlackMinutes,
                    task.Constraint,
                    task.ConstraintDate,
                    task.Deadline,
                    task.WorkMinutes,
                    task.Cost,
                    [.. task.Segments.Select(s => new ScheduleSegmentDto(s.Start, s.Finish))],
                    [
                        .. task.Predecessors.Select(d => new SchedulePredecessorDto(
                            d.Predecessor.UniqueId, d.Type, d.Lag.Kind, d.Lag.Value)),
                    ],
                    task.PercentComplete,
                    task.ActualStart,
                    task.ActualFinish,
                    task.Baseline()?.Start,
                    task.Baseline()?.Finish,
                    task.Baseline()?.Cost,
                    task.LevelingDelayMinutes,
                    task.Priority,
                    task.Formatting?.SpaceAfter ?? 0,
                    task.Type,
                    task.IsEffortDriven,
                    task.IgnoresResourceCalendars,
                    task.FixedCost,
                    task.FixedCostAccrual,
                    task.ManualStart,
                    task.ManualFinish,
                    task.Calendar?.Name,
                    [
                        .. task.Assignments.Select(a => new ScheduleAssignmentDto(
                            a.Resource.Name,
                            a.Resource.Type,
                            a.Units,
                            a.WorkMinutes,
                            a.Contour,
                            a.DelayMinutes,
                            a.RateTable,
                            a.Cost,
                            a.Resource.Type == ResourceType.Cost ? a.CostInput : 0m,
                            a.MaterialRateUnit,
                            a.ActualWorkMinutes,
                            a.ActualCost)),
                    ],
                    project.CustomFields.Count == 0
                        ? null
                        : project.CustomFields.ToDictionary(
                            f => f.Id,
                            f => Core.Fields.FieldCatalog.CustomValue(f, task)),
                    !string.IsNullOrEmpty(task.Description))),
            ]);
    }

    private static readonly DayOfWeek[] WeekdayOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static string DayKey(DayOfWeek day) => day.ToString().ToLowerInvariant();

    private static string FormatDay(DaySchedule? day)
        => day is not { } schedule ? "inherit" : schedule.IsWorking ? string.Join(",", schedule.Intervals) : "off";

    private static Dictionary<string, string> FormatWeek(WeeklyPattern pattern)
        => WeekdayOrder.ToDictionary(DayKey, day => FormatDay(pattern[day]));

    private static CalendarRecurrenceDto? ToRecurrenceDto(CalendarException exception)
    {
        if (exception.Recurrence is not { } recurrence)
        {
            return null;
        }

        var (kind, every) = recurrence switch
        {
            DailyRecurrence d => ("daily", d.EveryDays),
            WeeklyRecurrence w => ("weekly", w.EveryWeeks),
            MonthlyDayRecurrence m => ("monthly", m.EveryMonths),
            MonthlyWeekdayRecurrence m => ("monthly", m.EveryMonths),
            YearlyDateRecurrence => ("yearly", 1),
            YearlyWeekdayRecurrence => ("yearly", 1),
            _ => ("unknown", 1),
        };
        var endMode = exception.Occurrences is not null ? "count" : exception.End is not null ? "date" : "never";
        return new CalendarRecurrenceDto(kind, every, endMode, exception.Occurrences);
    }

    /// <summary>Detail for one calendar: its own weekly pattern, exceptions and work weeks (not the base's).</summary>
    public static CalendarDetailDto CalendarDetail(Project project, WorkCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(calendar);
        return new CalendarDetailDto(
            calendar.Name,
            calendar.BaseCalendar?.Name,
            ReferenceEquals(calendar, project.Calendar),
            project.Tasks.Count(t => ReferenceEquals(t.Calendar, calendar)),
            project.Resources.Count(r => ReferenceEquals(r.Calendar, calendar)),
            FormatWeek(calendar.DefaultWeek),
            [
                .. calendar.Exceptions.Select(e => new CalendarExceptionDto(
                    e.Name,
                    e.Start,
                    e.End,
                    !e.Schedule.IsWorking,
                    [.. e.Schedule.IsWorking ? e.Schedule.Intervals.Select(i => i.ToString()) : []],
                    ToRecurrenceDto(e))),
            ],
            [
                .. calendar.WorkWeeks.Select(w => new WorkWeekDto(w.Name, w.Start, w.End, FormatWeek(w.Pattern))),
            ]);
    }

    /// <summary>Resolved day-by-day schedule for one month, with provenance for the calendar's month preview.</summary>
    public static CalendarMonthDto CalendarMonth(WorkCalendar calendar, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var days = DateTime.DaysInMonth(year, month);
        return new CalendarMonthDto(
            year,
            month,
            [
                .. Enumerable.Range(1, days).Select(d =>
                {
                    var date = new DateOnly(year, month, d);
                    var resolved = calendar.ResolveDetailed(date);
                    return new CalendarDayCellDto(date, resolved.Schedule.IsWorking, resolved.Source, resolved.RuleName);
                }),
            ]);
    }
}
