using System.Globalization;
using Project27.Core.Time;

namespace Project27.Core.Scheduling;

public enum TaskDriverKind
{
    ProjectStart,
    Predecessor,
    Constraint,
    ActualStart,
    ManualDates,
    LevelingDelay,
    Rollup,
    Calendar,
}

/// <summary>One influence on a task's scheduled start; binding = it set the date.</summary>
public sealed record TaskDriver(
    TaskDriverKind Kind,
    string Description,
    bool Binding,
    DateTime? Date = null,
    int? PredecessorUid = null);

/// <summary>
/// The task inspector: reproduces the forward-pass reasoning read-only
/// (docs/spec/10-advanced-scheduling.md). Recalculate before asking.
/// </summary>
public static class TaskDrivers
{
    public static IReadOnlyList<TaskDriver> Explain(ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var project = task.Project;
        var calendar = task.Calendar ?? project.Calendar;
        var drivers = new List<TaskDriver>();

        if (task.IsSummary)
        {
            drivers.Add(new TaskDriver(TaskDriverKind.Rollup, "Summary: dates roll up from the subtasks.", Binding: true));
            return drivers;
        }

        if (task.Start is not { } start)
        {
            return [new TaskDriver(TaskDriverKind.ProjectStart, "Not scheduled yet — recalculate first.", Binding: false)];
        }

        // Hard pins first: they override everything else.
        if (task.ActualStartRaw is { } actualStart)
        {
            drivers.Add(new TaskDriver(
                TaskDriverKind.ActualStart,
                $"Actual start {Text(actualStart)} pins the task (progress outranks links and constraints).",
                Binding: true,
                actualStart));
        }
        else if (task.Mode == TaskMode.Manual)
        {
            drivers.Add(new TaskDriver(
                TaskDriverKind.ManualDates,
                task.ManualStart is { } manualStart
                    ? $"Manually scheduled at {Text(manualStart)}."
                    : "Manually scheduled (no explicit start; placed at the project start).",
                Binding: true,
                task.ManualStart));
        }

        var pinned = drivers.Count > 0;

        // Project anchor.
        var anchorBinding = !pinned && calendar.NextWorkingTime(project.StartDate) == start && task.LevelingDelayMinutes == 0;
        drivers.Add(new TaskDriver(
            TaskDriverKind.ProjectStart,
            $"Project start {Text(project.StartDate)}.",
            anchorBinding,
            project.StartDate));

        // Predecessors, own and inherited from ancestors.
        for (var scope = task; scope is { IsRoot: false }; scope = scope.Parent!)
        {
            foreach (var dependency in scope.Predecessors)
            {
                var predecessor = dependency.Predecessor;
                if (!predecessor.IsActive || predecessor.EarlyStart is null || predecessor.EarlyFinish is null)
                {
                    continue;
                }

                var basePoint = dependency.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish
                    ? predecessor.EarlyFinish.Value
                    : predecessor.EarlyStart.Value;
                var imposed = ProjectScheduler.ApplyLag(basePoint, dependency.Lag, calendar, predecessor.DurationMinutes, forward: true);
                var startLike = dependency.Type is DependencyType.FinishToStart or DependencyType.StartToStart;
                var binding = !pinned && task.LevelingDelayMinutes == 0 && (startLike
                    ? calendar.NextWorkingTime(imposed) == start
                    : imposed == task.Finish);
                var inherited = ReferenceEquals(scope, task) ? "" : $" (inherited from summary '{scope.Name}')";
                drivers.Add(new TaskDriver(
                    TaskDriverKind.Predecessor,
                    $"Predecessor {predecessor.RowNumber} '{predecessor.Name}' imposes {(startLike ? "start" : "finish")} ≥ {Text(imposed)}{inherited}.",
                    binding,
                    imposed,
                    predecessor.UniqueId));
            }
        }

        // Constraint.
        if (task.Constraint != ConstraintType.AsSoonAsPossible)
        {
            var binding = !pinned && task.Constraint switch
            {
                ConstraintType.StartNoEarlierThan or ConstraintType.MustStartOn =>
                    task.ConstraintDate is { } date && calendar.NextWorkingTime(date) == start,
                ConstraintType.FinishNoEarlierThan or ConstraintType.MustFinishOn =>
                    task.ConstraintDate is { } date && date == task.Finish,
                ConstraintType.AsLateAsPossible => true,
                _ => false, // SNLT/FNLT act on the late pass, not the start
            };
            drivers.Add(new TaskDriver(
                TaskDriverKind.Constraint,
                $"Constraint {task.Constraint}{(task.ConstraintDate is { } d ? " " + Text(d) : "")}.",
                binding,
                task.ConstraintDate));
        }

        if (task.LevelingDelayMinutes > 0)
        {
            drivers.Add(new TaskDriver(
                TaskDriverKind.LevelingDelay,
                $"Leveling delay of {(task.LevelingDelayMinutes / project.TimeSettings.MinutesPerDay).ToString("0.##", CultureInfo.InvariantCulture)}d postpones the start.",
                Binding: !pinned));
        }

        drivers.Add(new TaskDriver(
            TaskDriverKind.Calendar,
            task.Calendar is { } taskCalendar
                ? $"Task calendar '{taskCalendar.Name}' shapes working time."
                : $"Project calendar '{project.Calendar.Name}' shapes working time.",
            Binding: false));

        return drivers;
    }

    private static string Text(DateTime date) => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
