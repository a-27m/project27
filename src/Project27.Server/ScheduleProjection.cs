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
    decimal TotalCost);

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
    IReadOnlyList<SchedulePredecessorDto> Predecessors);

public sealed record ScheduleDto(int Version, ScheduleProjectDto Project, IReadOnlyList<ScheduleTaskDto> Tasks);

public sealed record CommandsResponse(int Version, IReadOnlyList<int?> CreatedUids, ScheduleDto Schedule);

public static class ScheduleProjection
{
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
                project.TotalCost),
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
                    ])),
            ]);
    }
}
