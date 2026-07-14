using System.Globalization;

namespace Project27.Core.Commands;

/// <summary>
/// Computes the inverse of a command from the aggregate's pre-state
/// (docs/spec/12-polish.md §12b). Null = not invertible (destructive or
/// whole-plan operations); callers treat that as an undo barrier.
/// </summary>
public static class CommandInverter
{
    /// <summary>
    /// Applies the command and returns (created uid, inverse). Call on the same
    /// aggregate the command targets, before recalculating.
    /// </summary>
    public static (int? CreatedUid, ProjectCommand? Inverse) ApplyWithInverse(Project project, ProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        var inverse = PrepareInverse(project, command);
        var createdUid = CommandExecutor.Apply(project, command);
        if (command is AddTaskCommand && createdUid is { } uid)
        {
            inverse = new RemoveTaskCommand { Uid = uid };
        }
        else if (command is AddRecurringTaskCommand && createdUid is { } recurringUid)
        {
            inverse = new RemoveTaskCommand { Uid = recurringUid };
        }

        return (createdUid, inverse);
    }

    private static ProjectCommand? PrepareInverse(Project project, ProjectCommand command) => command switch
    {
        AddTaskCommand or AddRecurringTaskCommand => null, // finalized post-apply (needs the new uid)
        SetTaskCommand set => InvertSetTask(project, set),
        MoveTaskCommand move => CurrentPosition(project, move.Uid),
        IndentTaskCommand indent => CurrentPosition(project, indent.Uid),
        OutdentTaskCommand outdent => CurrentPosition(project, outdent.Uid),
        LinkCommand link => new UnlinkCommand { PredecessorUid = link.PredecessorUid, SuccessorUid = link.SuccessorUid },
        UnlinkCommand unlink => CaptureLink(project, unlink.PredecessorUid, unlink.SuccessorUid),
        SetLinkCommand setLink => CaptureSetLink(project, setLink),
        SplitTaskCommand split => FindTask(project, split.Uid)?.IsSplit == false
            ? new UnsplitTaskCommand { Uid = split.Uid }
            : null,
        SetProjectCommand set => InvertSetProject(project, set),
        AssignCommand assign => new UnassignCommand { Uid = assign.Uid, Resource = assign.Resource },
        UnassignCommand unassign => CaptureAssignment(project, unassign.Uid, unassign.Resource),
        SetAssignmentCommand set => CaptureAssignmentEdit(project, set),
        _ => null,
    };

    private static ProjectTask? FindTask(Project project, int uid)
        => project.Tasks.FirstOrDefault(t => t.UniqueId == uid);

    private static MoveTaskCommand? CurrentPosition(Project project, int uid)
    {
        var task = FindTask(project, uid);
        if (task?.Parent is not { } parent)
        {
            return null;
        }

        return new MoveTaskCommand
        {
            Uid = uid,
            ParentUid = parent.IsRoot ? null : parent.UniqueId,
            At = parent.Children.ToList().IndexOf(task),
        };
    }

    private static LinkCommand? CaptureLink(Project project, int predecessorUid, int successorUid)
    {
        var dependency = FindTask(project, successorUid)?.Predecessors
            .FirstOrDefault(d => d.Predecessor.UniqueId == predecessorUid);
        return dependency is null
            ? null
            : new LinkCommand
            {
                PredecessorUid = predecessorUid,
                SuccessorUid = successorUid,
                Type = dependency.Type,
                Lag = dependency.Lag.IsZero ? null : new CommandLag(dependency.Lag.Kind, dependency.Lag.Value),
            };
    }

    private static SetLinkCommand? CaptureSetLink(Project project, SetLinkCommand command)
    {
        var dependency = FindTask(project, command.SuccessorUid)?.Predecessors
            .FirstOrDefault(d => d.Predecessor.UniqueId == command.PredecessorUid);
        return dependency is null
            ? null
            : new SetLinkCommand
            {
                PredecessorUid = command.PredecessorUid,
                SuccessorUid = command.SuccessorUid,
                Type = dependency.Type,
                Lag = new CommandLag(dependency.Lag.Kind, dependency.Lag.Value),
            };
    }

    private static AssignCommand? CaptureAssignment(Project project, int uid, string resourceName)
    {
        var assignment = FindTask(project, uid)?.Assignments
            .FirstOrDefault(a => string.Equals(a.Resource.Name, resourceName, StringComparison.OrdinalIgnoreCase));
        if (assignment is null)
        {
            return null;
        }

        // Re-assigning with the captured work restores the triangle exactly;
        // contour/delay/table/cost ride along in the same op via a follow-up edit —
        // but a single inverse command is required, so fold what Assign accepts.
        return new AssignCommand
        {
            Uid = uid,
            Resource = assignment.Resource.Name,
            Units = assignment.Units,
            Work = assignment.Resource.Type == ResourceType.Work
                ? assignment.WorkMinutes.ToString(CultureInfo.InvariantCulture) + "m"
                : null,
            Cost = assignment.Resource.Type == ResourceType.Cost ? assignment.CostInput : null,
        };
    }

    private static SetAssignmentCommand? CaptureAssignmentEdit(Project project, SetAssignmentCommand command)
    {
        var assignment = FindTask(project, command.Uid)?.Assignments
            .FirstOrDefault(a => string.Equals(a.Resource.Name, command.Resource, StringComparison.OrdinalIgnoreCase));
        if (assignment is null)
        {
            return null;
        }

        return new SetAssignmentCommand
        {
            Uid = command.Uid,
            Resource = command.Resource,
            Units = command.Units is null ? null : assignment.Units,
            Work = command.Work is null ? null : assignment.WorkMinutes.ToString(CultureInfo.InvariantCulture) + "m",
            Contour = command.Contour is null ? null : assignment.Contour,
            Delay = command.Delay is null ? null : assignment.DelayMinutes.ToString(CultureInfo.InvariantCulture) + "m",
            RateTable = command.RateTable is null ? null : assignment.RateTable,
            Cost = command.Cost is null ? null : assignment.CostInput,
        };
    }

    private static SetTaskCommand? InvertSetTask(Project project, SetTaskCommand command)
    {
        var task = FindTask(project, command.Uid);
        if (task is null)
        {
            return null;
        }

        string Minutes(decimal minutes) => minutes.ToString(CultureInfo.InvariantCulture) + "m";
        return new SetTaskCommand
        {
            Uid = command.Uid,
            Name = command.Name is null ? null : task.Name,
            Duration = (command.Duration is null && command.RemainingDuration is null) || task.IsSummary ? null : Minutes(task.DurationMinutes),
            Mode = command.Mode is null ? null : task.Mode,
            Active = command.Active is null ? null : task.IsActive,
            Milestone = command.Milestone is null ? null : task.IsMilestone,
            Priority = command.Priority is null ? null : task.Priority,
            SpaceAfter = command.SpaceAfter is null ? null : task.Formatting?.SpaceAfter ?? 0,
            Deadline = (command.Deadline is null && !command.ClearDeadline) ? null : task.Deadline,
            ClearDeadline = (command.Deadline is not null || command.ClearDeadline) && task.Deadline is null,
            Constraint = command.Constraint is null && command.ConstraintDate is null ? null : task.Constraint,
            ConstraintDate = command.Constraint is null && command.ConstraintDate is null ? null : task.ConstraintDate,
            Calendar = (command.Calendar is null && !command.ClearCalendar) ? null : task.Calendar?.Name,
            ClearCalendar = (command.Calendar is not null || command.ClearCalendar) && task.Calendar is null,
            Wbs = (command.Wbs is null && !command.ClearWbs) ? null : task.CustomWbs,
            ClearWbs = (command.Wbs is not null || command.ClearWbs) && task.CustomWbs is null,
            ManualStart = (command.ManualStart is null && !command.ClearManualStart) ? null : task.ManualStart,
            ClearManualStart = (command.ManualStart is not null || command.ClearManualStart) && task.ManualStart is null,
            ManualFinish = (command.ManualFinish is null && !command.ClearManualFinish) ? null : task.ManualFinish,
            ClearManualFinish = (command.ManualFinish is not null || command.ClearManualFinish) && task.ManualFinish is null,
            Type = command.Type is null ? null : task.Type,
            EffortDriven = command.EffortDriven is null ? null : task.IsEffortDriven,
            FixedCost = command.FixedCost is null ? null : task.FixedCost,
            FixedCostAccrual = command.FixedCostAccrual is null ? null : task.FixedCostAccrual,
            IgnoreResourceCalendars = command.IgnoreResourceCalendars is null ? null : task.IgnoresResourceCalendars,
            PercentComplete = command.PercentComplete is null && command.RemainingDuration is null && !command.ClearActualFinish && command.ActualFinish is null
                ? null
                : task.PercentComplete,
            ActualStart = (command.ActualStart is null && !command.ClearActualStart && command.PercentComplete is null) ? null : task.ActualStart,
            ClearActualStart = (command.ActualStart is not null || command.ClearActualStart) && task.ActualStart is null,
            ActualFinish = (command.ActualFinish is null && !command.ClearActualFinish && command.PercentComplete is null) ? null : task.ActualFinish,
            ClearActualFinish = (command.ActualFinish is not null || command.ClearActualFinish || command.PercentComplete is not null)
                && task.ActualFinish is null,
            RemainingDuration = null, // remaining-duration edits fold into Duration + PercentComplete captures
            CustomValues = command.CustomValues is null
                ? null
                : command.CustomValues.Keys.ToDictionary(
                    key => key,
                    key =>
                    {
                        var field = project.FindCustomField(key);
                        var value = field is null ? null : task.GetCustomValue(field.Id);
                        return value switch
                        {
                            null => null,
                            decimal number => number.ToString(CultureInfo.InvariantCulture),
                            DateTime date => date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                            bool flag => flag ? "true" : "false",
                            _ => value.ToString(),
                        };
                    }),
        };
    }

    private static SetProjectCommand InvertSetProject(Project project, SetProjectCommand command) => new()
    {
        Name = command.Name is null ? null : project.Name,
        Start = command.Start is null ? null : project.StartDate,
        ScheduleFrom = command.ScheduleFrom is null ? null : project.ScheduleFrom,
        Calendar = command.Calendar is null ? null : project.Calendar.Name,
        StatusDate = (command.StatusDate is null && !command.ClearStatusDate) ? null : project.StatusDate,
        ClearStatusDate = (command.StatusDate is not null || command.ClearStatusDate) && project.StatusDate is null,
        MinutesPerDay = command.MinutesPerDay is null ? null : project.TimeSettings.MinutesPerDay,
        MinutesPerWeek = command.MinutesPerWeek is null ? null : project.TimeSettings.MinutesPerWeek,
        DaysPerMonth = command.DaysPerMonth is null ? null : project.TimeSettings.DaysPerMonth,
        WeekStartsOn = command.WeekStartsOn is null ? null : project.TimeSettings.WeekStartsOn,
        DayStart = command.DayStart is null ? null : project.TimeSettings.DefaultStartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        DayEnd = command.DayEnd is null ? null : project.TimeSettings.DefaultEndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        CriticalSlack = command.CriticalSlack is null
            ? null
            : project.CriticalSlackThresholdMinutes.ToString(CultureInfo.InvariantCulture) + "m",
    };
}
