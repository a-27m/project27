using Project27.Core.Time;

namespace Project27.Core.Commands;

/// <summary>Raised when a command references missing entities or carries invalid values.</summary>
public sealed class CommandException : Exception
{
    public CommandException()
    {
    }

    public CommandException(string message)
        : base(message)
    {
    }

    public CommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Applies commands to a project aggregate. Does not recalculate — apply a batch,
/// then call <see cref="Project.Recalculate"/> once.
/// </summary>
public static class CommandExecutor
{
    /// <summary>Applies one command; returns the uid of a created task, otherwise null.</summary>
    public static int? Apply(Project project, ProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            return command switch
            {
                AddTaskCommand add => AddTask(project, add),
                SetTaskCommand set => SetTask(project, set),
                RemoveTaskCommand remove => Run(() => project.RemoveTask(Task(project, remove.Uid))),
                MoveTaskCommand move => Run(() => project.MoveTask(
                    Task(project, move.Uid),
                    move.ParentUid is { } parentUid ? Task(project, parentUid) : null,
                    move.At)),
                IndentTaskCommand indent => Run(() => project.Indent(Task(project, indent.Uid))),
                OutdentTaskCommand outdent => Run(() => project.Outdent(Task(project, outdent.Uid))),
                LinkCommand link => Run(() => project.Link(
                    Task(project, link.PredecessorUid),
                    Task(project, link.SuccessorUid),
                    link.Type,
                    ToLag(link.Lag))),
                SetLinkCommand setLink => SetLink(project, setLink),
                UnlinkCommand unlink => Run(() => project.Unlink(Dependency(project, unlink.PredecessorUid, unlink.SuccessorUid))),
                SplitTaskCommand split => Run(() => Task(project, split.Uid).SplitAt(
                    ParseDuration(split.At),
                    ParseDuration(split.Gap))),
                UnsplitTaskCommand unsplit => Run(() => Task(project, unsplit.Uid).ClearSplits()),
                SetProjectCommand set => SetProject(project, set),
                _ => throw new CommandException($"Unknown command type {command.GetType().Name}."),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            throw new CommandException(exception.Message, exception);
        }
    }

    /// <summary>Applies a batch in order; returns created-task uids aligned with the batch (null for non-creating commands).</summary>
    public static IReadOnlyList<int?> ApplyAll(Project project, IEnumerable<ProjectCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return [.. commands.Select(command => Apply(project, command))];
    }

    private static int? AddTask(Project project, AddTaskCommand command)
    {
        var duration = command.Milestone
            ? new Duration(0, DurationUnit.Days)
            : command.Duration is { } text ? ParseDuration(text) : (Duration?)null;
        var parent = command.ParentUid is { } parentUid ? Task(project, parentUid) : null;
        return project.AddTask(command.Name, duration, parent, command.At).UniqueId;
    }

    private static int? SetTask(Project project, SetTaskCommand command)
    {
        var task = Task(project, command.Uid);
        if (command.Name is { } name)
        {
            task.Name = name;
        }

        if (command.Type is { } type)
        {
            task.Type = type;
        }

        if (command.EffortDriven is { } effortDriven)
        {
            task.IsEffortDriven = effortDriven;
        }

        if (command.Duration is { } duration)
        {
            task.Duration = ParseDuration(duration);
        }

        if (command.Mode is { } mode)
        {
            task.Mode = mode;
        }

        if (command.Active is { } active)
        {
            task.IsActive = active;
        }

        if (command.Milestone is { } milestone)
        {
            task.IsMilestone = milestone;
        }

        if (command.Priority is { } priority)
        {
            task.Priority = priority;
        }

        if (command.ClearDeadline)
        {
            task.Deadline = null;
        }
        else if (command.Deadline is { } deadline)
        {
            task.Deadline = deadline;
        }

        if (command.Constraint is { } constraint)
        {
            task.SetConstraint(constraint, command.ConstraintDate ?? task.ConstraintDate);
        }
        else if (command.ConstraintDate is { } constraintDate)
        {
            task.SetConstraint(task.Constraint, constraintDate);
        }

        if (command.ClearCalendar)
        {
            task.Calendar = null;
        }
        else if (command.Calendar is { } calendarName)
        {
            task.Calendar = Calendar(project, calendarName);
        }

        if (command.ClearWbs)
        {
            task.CustomWbs = null;
        }
        else if (command.Wbs is { } wbs)
        {
            task.CustomWbs = wbs;
        }

        if (command.ClearManualStart)
        {
            task.ManualStart = null;
        }
        else if (command.ManualStart is { } manualStart)
        {
            task.ManualStart = manualStart;
        }

        if (command.ClearManualFinish)
        {
            task.ManualFinish = null;
        }
        else if (command.ManualFinish is { } manualFinish)
        {
            task.ManualFinish = manualFinish;
        }

        if (command.FixedCost is { } fixedCost)
        {
            task.FixedCost = fixedCost;
        }

        if (command.FixedCostAccrual is { } accrual)
        {
            task.FixedCostAccrual = accrual;
        }

        if (command.IgnoreResourceCalendars is { } ignores)
        {
            task.IgnoresResourceCalendars = ignores;
        }

        return null;
    }

    private static int? SetLink(Project project, SetLinkCommand command)
    {
        var dependency = Dependency(project, command.PredecessorUid, command.SuccessorUid);
        // TaskDependency is immutable in shape: replace it.
        var type = command.Type ?? dependency.Type;
        var lag = command.Lag is { } newLag ? ToLag(newLag) : dependency.Lag;
        project.Unlink(dependency);
        project.Link(dependency.Predecessor, dependency.Successor, type, lag);
        return null;
    }

    private static int? SetProject(Project project, SetProjectCommand command)
    {
        if (command.Name is { } name)
        {
            project.Name = name;
        }

        if (command.Start is { } start)
        {
            project.StartDate = start;
        }

        if (command.ScheduleFrom is { } scheduleFrom)
        {
            project.ScheduleFrom = scheduleFrom;
        }

        if (command.Calendar is { } calendarName)
        {
            project.Calendar = Calendar(project, calendarName);
        }

        return null;
    }

    private static int? Run(Action action)
    {
        action();
        return null;
    }

    private static ProjectTask Task(Project project, int uid)
        => project.Tasks.FirstOrDefault(t => t.UniqueId == uid)
            ?? throw new CommandException($"No task with uid {uid}.");

    private static TaskDependency Dependency(Project project, int predecessorUid, int successorUid)
    {
        var successor = Task(project, successorUid);
        return successor.Predecessors.FirstOrDefault(d => d.Predecessor.UniqueId == predecessorUid)
            ?? throw new CommandException($"No link between uid {predecessorUid} and uid {successorUid}.");
    }

    private static WorkCalendar Calendar(Project project, string name)
        => project.Calendars.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CommandException($"No calendar named '{name}'.");

    private static Duration ParseDuration(string text)
        => Duration.TryParse(text, out var duration)
            ? duration
            : throw new CommandException($"Invalid duration '{text}'.");

    private static Lag ToLag(CommandLag? lag)
        => lag is null ? Lag.Zero : Lag.Restore(lag.Kind, lag.Value);
}
