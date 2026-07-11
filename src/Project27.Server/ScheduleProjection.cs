using Project27.Core;

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
    string Calendar,
    decimal TotalWorkMinutes,
    decimal TotalCost,
    DateTime? StatusDate,
    IReadOnlyList<string> Calendars,
    IReadOnlyList<ResourceSummaryDto> Resources,
    IReadOnlyList<CustomFieldSummaryDto> CustomFields);

public sealed record ResourceSummaryDto(int Uid, string Name, ResourceType Type, decimal MaxUnits, string Rate);

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
    decimal CostInput);

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
    TaskType Type,
    bool EffortDriven,
    bool IgnoresResourceCalendars,
    decimal FixedCost,
    CostAccrual FixedCostAccrual,
    DateTime? ManualStart,
    DateTime? ManualFinish,
    string? Calendar,
    IReadOnlyList<ScheduleAssignmentDto> Assignments,
    IReadOnlyDictionary<string, object?>? CustomValues);

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
                project.Calendar.Name,
                project.TotalWorkMinutes,
                project.TotalCost,
                project.StatusDate,
                [.. project.Calendars.Select(c => c.Name)],
                [
                    .. project.Resources.Select(r => new ResourceSummaryDto(
                        r.UniqueId, r.Name, r.Type, r.MaxUnits, r.StandardRate.ToString())),
                ],
                [
                    .. project.CustomFields.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => new CustomFieldSummaryDto(
                        f.Id, f.Alias, f.Kind.ToString(), f.Formula is not null)),
                ]),
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
                            a.Resource.Type == ResourceType.Cost ? a.CostInput : 0m)),
                    ],
                    project.CustomFields.Count == 0
                        ? null
                        : project.CustomFields.ToDictionary(
                            f => f.Id,
                            f => Core.Fields.FieldCatalog.CustomValue(f, task)))),
            ]);
    }
}
