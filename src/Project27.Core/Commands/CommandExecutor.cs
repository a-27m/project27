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
                SetBaselineCommand baseline => Run(() => project.SetBaseline(baseline.Slot, ResolveScope(project, baseline.Uids))),
                ClearBaselineCommand baseline => Run(() => project.ClearBaseline(baseline.Slot, ResolveScope(project, baseline.Uids))),
                LevelCommand => Run(() => project.Level()),
                ClearLevelingCommand => Run(project.ClearLeveling),
                AddResourceCommand add => AddResource(project, add),
                SetResourceCommand set => SetResource(project, set),
                RemoveResourceCommand remove => Run(() => project.RemoveResource(ResourceByName(project, remove.Resource))),
                SetResourceRateCommand rate => Run(() => ResourceByName(project, rate.Resource).RateTable(rate.Table).SetRate(
                    rate.From ?? DateTime.MinValue,
                    rate.Rate is { } standard ? ParseRate(standard) : null,
                    rate.OvertimeRate is { } overtime ? ParseRate(overtime) : null,
                    rate.CostPerUse)),
                RemoveResourceRateCommand rate => Run(() =>
                {
                    if (!ResourceByName(project, rate.Resource).RateTable(rate.Table).RemoveRate(rate.From))
                    {
                        throw new CommandException($"No rate entry effective at {rate.From:yyyy-MM-dd HH:mm} in table {rate.Table}.");
                    }
                }),
                AssignCommand assign => Assign(project, assign),
                SetAssignmentCommand set => SetAssignment(project, set),
                UnassignCommand unassign => Run(() => project.Unassign(AssignmentOf(project, unassign.Uid, unassign.Resource))),
                AddCalendarCommand add => Run(() => project.AddCalendar(BuildCalendar(project, add))),
                RemoveCalendarCommand remove => Run(() => project.RemoveCalendar(Calendar(project, remove.Calendar))),
                SetCalendarDayCommand day => Run(() => Calendar(project, day.Calendar).SetDay(
                    day.Day,
                    day.Off ? DaySchedule.NonWorking : day.Intervals is null ? null : ToSchedule(day.Intervals))),
                SetCalendarBaseCommand baseCalendar => Run(() => Calendar(project, baseCalendar.Calendar).SetBaseCalendar(
                    baseCalendar.BaseCalendar is { } baseName ? Calendar(project, baseName) : null)),
                AddCalendarExceptionCommand exception => Run(() => Calendar(project, exception.Calendar).AddException(new CalendarException(
                    exception.Name,
                    exception.From,
                    exception.To,
                    exception.Intervals is { Count: > 0 } intervals ? ToSchedule(intervals) : DaySchedule.NonWorking,
                    exception.Recurrence is { } recurrence ? ToRecurrence(recurrence) : null,
                    exception.Times))),
                RemoveCalendarExceptionCommand exception => Run(() =>
                {
                    var calendar = Calendar(project, exception.Calendar);
                    var match = calendar.Exceptions.FirstOrDefault(e => string.Equals(e.Name, exception.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new CommandException($"No exception named '{exception.Name}' in calendar '{calendar.Name}'.");
                    calendar.RemoveException(match);
                }),
                AddWorkWeekCommand week => Run(() => Calendar(project, week.Calendar).AddWorkWeek(new WorkWeek(
                    week.Name, week.From, week.To, ToPattern(week.Days)))),
                RemoveWorkWeekCommand week => Run(() =>
                {
                    var calendar = Calendar(project, week.Calendar);
                    var match = calendar.WorkWeeks.FirstOrDefault(w => string.Equals(w.Name, week.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new CommandException($"No work week named '{week.Name}' in calendar '{calendar.Name}'.");
                    calendar.RemoveWorkWeek(match);
                }),
                DefineCustomFieldCommand field => Run(() => project.DefineCustomField(
                    field.Slot,
                    field.Alias,
                    field.Formula,
                    field.Indicators?.Select(rule => ToIndicatorRule(project, field.Slot, rule)))),
                RemoveCustomFieldCommand field => Run(() =>
                {
                    if (!project.RemoveCustomField(field.Field))
                    {
                        throw new CommandException($"No custom field '{field.Field}'.");
                    }
                }),
                AddRecurringTaskCommand recurring => project.AddRecurringTask(
                    recurring.Name,
                    ParseDuration(recurring.Duration),
                    ToRecurrence(recurring.Recurrence),
                    recurring.From,
                    recurring.Until,
                    recurring.Times,
                    recurring.ParentUid is { } parentUid ? Task(project, parentUid) : null).UniqueId,
                RescheduleCommand reschedule => Run(() => project.RescheduleUncompletedWork(reschedule.After)),
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

        if (command.PercentComplete is { } percent)
        {
            task.PercentComplete = percent;
        }

        if (command.ClearActualStart)
        {
            task.ActualStart = null;
        }
        else if (command.ActualStart is { } actualStart)
        {
            task.ActualStart = actualStart;
        }

        if (command.ClearActualFinish)
        {
            task.ActualFinish = null;
        }
        else if (command.ActualFinish is { } actualFinish)
        {
            task.ActualFinish = actualFinish;
        }

        if (command.RemainingDuration is { } remaining)
        {
            task.SetRemainingDuration(ParseDuration(remaining));
        }

        foreach (var (fieldName, text) in command.CustomValues ?? new Dictionary<string, string?>())
        {
            var field = project.FindCustomField(fieldName)
                ?? throw new CommandException($"No custom field '{fieldName}'.");
            task.SetCustomValue(field, text is null
                ? null
                : Fields.FieldCatalog.ParseLiteral(field.Kind, text, project.TimeSettings));
        }

        return null;
    }

    private static List<ProjectTask>? ResolveScope(Project project, IReadOnlyList<int> uids)
        => uids.Count == 0
            ? null
            : [.. uids.Select(uid => Task(project, uid)).SelectMany(t => t.SelfAndDescendants()).Distinct()];

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

        if (command.ClearStatusDate)
        {
            project.StatusDate = null;
        }
        else if (command.StatusDate is { } statusDate)
        {
            project.StatusDate = statusDate;
        }

        if (command.MinutesPerDay is { } minutesPerDay)
        {
            project.TimeSettings.MinutesPerDay = minutesPerDay;
        }

        if (command.MinutesPerWeek is { } minutesPerWeek)
        {
            project.TimeSettings.MinutesPerWeek = minutesPerWeek;
        }

        if (command.DaysPerMonth is { } daysPerMonth)
        {
            project.TimeSettings.DaysPerMonth = daysPerMonth;
        }

        if (command.WeekStartsOn is { } weekStartsOn)
        {
            project.TimeSettings.WeekStartsOn = weekStartsOn;
        }

        if (command.DayStart is { } dayStart)
        {
            project.TimeSettings.DefaultStartTime = ParseTime(dayStart);
        }

        if (command.DayEnd is { } dayEnd)
        {
            project.TimeSettings.DefaultEndTime = ParseTime(dayEnd);
        }

        if (command.CriticalSlack is { } criticalSlack)
        {
            project.CriticalSlackThresholdMinutes = ParseDuration(criticalSlack).ToMinutes(project.TimeSettings);
        }

        return null;
    }

    // -------------------------------------------------------- 12p-1 helpers

    private static int? AddResource(Project project, AddResourceCommand command)
    {
        var resource = project.AddResource(command.Name, command.Type);
        if (command.MaxUnits is { } maxUnits)
        {
            resource.MaxUnits = maxUnits;
        }

        if (command.Rate is { } rate)
        {
            resource.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, ParseRate(rate));
        }

        resource.MaterialLabel = command.MaterialLabel;
        resource.Initials = command.Initials;
        resource.Group = command.Group;
        if (command.Calendar is { } calendarName)
        {
            resource.Calendar = Calendar(project, calendarName);
        }

        return null;
    }

    private static int? SetResource(Project project, SetResourceCommand command)
    {
        var resource = ResourceByName(project, command.Resource);
        if (command.Name is { } name)
        {
            resource.Name = name;
        }

        if (command.MaxUnits is { } maxUnits)
        {
            resource.MaxUnits = maxUnits;
        }

        if (command.MaterialLabel is { } label)
        {
            resource.MaterialLabel = label;
        }

        if (command.ClearCalendar)
        {
            resource.Calendar = null;
        }
        else if (command.Calendar is { } calendarName)
        {
            resource.Calendar = Calendar(project, calendarName);
        }

        if (command.Initials is { } initials)
        {
            resource.Initials = initials;
        }

        if (command.Group is { } group)
        {
            resource.Group = group;
        }

        if (command.Accrual is { } accrual)
        {
            resource.Accrual = accrual;
        }

        return null;
    }

    private static int? Assign(Project project, AssignCommand command)
    {
        var assignment = project.Assign(
            Task(project, command.Uid),
            ResourceByName(project, command.Resource),
            command.Units,
            command.Work is { } work ? ParseDuration(work) : null);
        if (command.Cost is { } cost)
        {
            assignment.CostInput = cost;
        }

        return null;
    }

    private static int? SetAssignment(Project project, SetAssignmentCommand command)
    {
        var assignment = AssignmentOf(project, command.Uid, command.Resource);
        if (command.Units is { } units)
        {
            assignment.SetUnits(units);
        }

        if (command.Work is { } work)
        {
            assignment.SetWork(ParseDuration(work));
        }

        if (command.Contour is { } contour)
        {
            assignment.SetContour(contour);
        }

        if (command.Delay is { } delay)
        {
            assignment.DelayMinutes = ParseDuration(delay).ToMinutes(project.TimeSettings);
        }

        if (command.RateTable is { } table)
        {
            assignment.RateTable = table;
        }

        if (command.Cost is { } cost)
        {
            assignment.CostInput = cost;
        }

        return null;
    }

    private static WorkCalendar BuildCalendar(Project project, AddCalendarCommand command)
    {
        if (command.BaseCalendar is { } baseName)
        {
            return new WorkCalendar(command.Name, Calendar(project, baseName));
        }

        return (command.Preset?.Trim().ToUpperInvariant() ?? "STANDARD") switch
        {
            "STANDARD" => WorkCalendar.CreateStandard(command.Name),
            "24H" => WorkCalendar.Create24Hours(command.Name),
            "NIGHT-SHIFT" => WorkCalendar.CreateNightShift(command.Name),
            var other => throw new CommandException($"Unknown calendar preset '{other}'; use standard, 24h, or night-shift."),
        };
    }

    private static Resource ResourceByName(Project project, string name)
        => project.Resources.FirstOrDefault(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new CommandException($"No resource named '{name}'.");

    private static Assignment AssignmentOf(Project project, int uid, string resourceName)
    {
        var task = Task(project, uid);
        var resource = ResourceByName(project, resourceName);
        return task.Assignments.FirstOrDefault(a => ReferenceEquals(a.Resource, resource))
            ?? throw new CommandException($"'{resource.Name}' is not assigned to uid {uid}.");
    }

    private static Rate ParseRate(string text)
    {
        try
        {
            return Rate.Parse(text);
        }
        catch (FormatException exception)
        {
            throw new CommandException(exception.Message, exception);
        }
    }

    private static TimeOnly ParseTime(string text)
        => TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
            ? time
            : throw new CommandException($"'{text}' is not a time; use HH:mm.");

    private static DaySchedule ToSchedule(IReadOnlyList<CommandInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return DaySchedule.NonWorking;
        }

        try
        {
            return DaySchedule.Working([.. intervals.Select(i => TimeInterval.FromTimes(ParseTime(i.Start), ParseTime(i.End)))]);
        }
        catch (ArgumentException exception)
        {
            throw new CommandException(exception.Message, exception);
        }
    }

    private static WeeklyPattern ToPattern(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<CommandInterval>>? days)
    {
        var pattern = WeeklyPattern.InheritAll;
        foreach (var (day, intervals) in days ?? new Dictionary<DayOfWeek, IReadOnlyList<CommandInterval>>())
        {
            pattern = pattern.With(day, ToSchedule(intervals));
        }

        return pattern;
    }

    private static Recurrence ToRecurrence(CommandRecurrence recurrence)
    {
        var days = (recurrence.Days ?? []).Aggregate(DayOfWeekSet.None, (set, day) => set | day.AsSet());
        try
        {
            return recurrence.Kind.Trim().ToUpperInvariant() switch
            {
                "DAILY" => new DailyRecurrence(recurrence.Every),
                "WEEKLY" => new WeeklyRecurrence(recurrence.Every, days),
                "MONTHLYDAY" or "MONTHLY-DAY" => new MonthlyDayRecurrence(recurrence.Day, recurrence.Every),
                "MONTHLYWEEKDAY" or "MONTHLY-WEEKDAY" => new MonthlyWeekdayRecurrence(recurrence.Ordinal, recurrence.Weekday, recurrence.Every),
                "YEARLYDATE" or "YEARLY-DATE" => new YearlyDateRecurrence(recurrence.Month, recurrence.Day),
                "YEARLYWEEKDAY" or "YEARLY-WEEKDAY" => new YearlyWeekdayRecurrence(recurrence.Ordinal, recurrence.Weekday, recurrence.Month),
                var other => throw new CommandException($"Unknown recurrence kind '{other}'."),
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new CommandException($"Invalid recurrence: {exception.Message}", exception);
        }
    }

    private static Fields.IndicatorRule ToIndicatorRule(Project project, string slot, CommandIndicatorRule rule)
    {
        var op = rule.Op.Trim() switch
        {
            "=" or "==" => Views.FilterOperator.Equals,
            "!=" or "<>" => Views.FilterOperator.NotEquals,
            ">" => Views.FilterOperator.GreaterThan,
            ">=" => Views.FilterOperator.GreaterOrEqual,
            "<" => Views.FilterOperator.LessThan,
            "<=" => Views.FilterOperator.LessOrEqual,
            "~" => Views.FilterOperator.Contains,
            var other => throw new CommandException($"Unknown indicator operator '{other}'."),
        };
        var kind = Fields.CustomFieldDefinition.KindOfSlot(slot);
        object value = op == Views.FilterOperator.Contains
            ? rule.Value
            : Fields.FieldCatalog.ParseLiteral(kind, rule.Value, project.TimeSettings);
        return new Fields.IndicatorRule(op, value, rule.Icon);
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
