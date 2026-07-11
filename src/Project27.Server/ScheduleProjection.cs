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
    DateTime? StatusDate);

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
    decimal? BaselineCost);

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

public sealed record CommandsResponse(int Version, IReadOnlyList<int?> CreatedUids, ScheduleDto Schedule);

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
                project.StatusDate),
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
                    task.Baseline()?.Cost)),
            ]);
    }
}
