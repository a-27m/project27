using Project27.Core.Time;

namespace Project27.Core.Persistence;

/// <summary>Maps between the domain aggregate and its persistence document.</summary>
public static class ProjectDocumentMapper
{
    public static ProjectDocument ToDocument(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ProjectDocument
        {
            Id = project.Id,
            Name = project.Name,
            StartDate = project.StartDate,
            FinishDate = project.FinishDate,
            ScheduleFrom = project.ScheduleFrom,
            CalendarId = project.Calendar.Id,
            CriticalSlackThresholdMinutes = project.CriticalSlackThresholdMinutes,
            TimeSettings = new TimeSettingsDocument
            {
                MinutesPerDay = project.TimeSettings.MinutesPerDay,
                MinutesPerWeek = project.TimeSettings.MinutesPerWeek,
                DaysPerMonth = project.TimeSettings.DaysPerMonth,
                WeekStartsOn = project.TimeSettings.WeekStartsOn,
                DefaultStartTime = project.TimeSettings.DefaultStartTime,
                DefaultEndTime = project.TimeSettings.DefaultEndTime,
            },
            Calendars = [.. project.Calendars.Select(ToDocument)],
            Tasks = [.. project.Tasks.Select(ToDocument)],
            Resources = [.. project.Resources.Select(ToDocument)],
            Assignments =
            [
                .. project.Tasks
                    .SelectMany(t => t.Assignments)
                    .Select(a => new AssignmentDocument
                    {
                        Id = a.Id,
                        TaskId = a.Task.Id,
                        ResourceId = a.Resource.Id,
                        Units = a.Units,
                        WorkMinutes = a.WorkMinutes,
                        Contour = a.Contour,
                        DelayMinutes = a.DelayMinutes,
                        RateTable = a.RateTable,
                        CostInput = a.Resource.Type == ResourceType.Cost ? a.CostInput : 0m,
                    }),
            ],
            Dependencies =
            [
                .. project.Tasks
                    .SelectMany(t => t.Successors)
                    .Select(d => new DependencyDocument
                    {
                        PredecessorId = d.Predecessor.Id,
                        SuccessorId = d.Successor.Id,
                        Type = d.Type,
                        LagKind = d.Lag.Kind,
                        LagValue = d.Lag.Value,
                    }),
            ],
        };
    }

    public static Project FromDocument(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion is not (1 or 2))
        {
            throw new NotSupportedException($"Project document schema {document.SchemaVersion} is not supported by this build.");
        }

        // Calendars first (bases wired after creation), then the project, tasks, links.
        var calendars = new Dictionary<Guid, WorkCalendar>();
        foreach (var calendarDoc in document.Calendars)
        {
            calendars.Add(calendarDoc.Id, new WorkCalendar(calendarDoc.Name, defaultWeek: ToPattern(calendarDoc.DefaultWeek), id: calendarDoc.Id));
        }

        foreach (var calendarDoc in document.Calendars)
        {
            var calendar = calendars[calendarDoc.Id];
            if (calendarDoc.BaseCalendarId is { } baseId)
            {
                calendar.SetBaseCalendar(calendars[baseId]);
            }

            foreach (var exceptionDoc in calendarDoc.Exceptions)
            {
                calendar.AddException(new CalendarException(
                    exceptionDoc.Name,
                    exceptionDoc.Start,
                    exceptionDoc.End,
                    ToSchedule(exceptionDoc.Schedule),
                    exceptionDoc.Recurrence is { } r ? ToRecurrence(r) : null,
                    exceptionDoc.Occurrences));
            }

            foreach (var workWeekDoc in calendarDoc.WorkWeeks)
            {
                calendar.AddWorkWeek(new WorkWeek(workWeekDoc.Name, workWeekDoc.Start, workWeekDoc.End, ToPattern(workWeekDoc.Days)));
            }
        }

        var project = new Project(document.Name, document.StartDate, calendars[document.CalendarId], document.Id)
        {
            FinishDate = document.FinishDate,
            ScheduleFrom = document.ScheduleFrom,
            CriticalSlackThresholdMinutes = document.CriticalSlackThresholdMinutes,
        };
        foreach (var calendar in calendars.Values)
        {
            if (!ReferenceEquals(calendar, project.Calendar))
            {
                project.AddCalendar(calendar);
            }
        }

        var settings = document.TimeSettings;
        project.TimeSettings.MinutesPerDay = settings.MinutesPerDay;
        project.TimeSettings.MinutesPerWeek = settings.MinutesPerWeek;
        project.TimeSettings.DaysPerMonth = settings.DaysPerMonth;
        project.TimeSettings.WeekStartsOn = settings.WeekStartsOn;
        project.TimeSettings.DefaultStartTime = settings.DefaultStartTime;
        project.TimeSettings.DefaultEndTime = settings.DefaultEndTime;

        var tasks = new Dictionary<Guid, ProjectTask>();
        foreach (var taskDoc in document.Tasks)
        {
            var parent = taskDoc.ParentId is { } parentId ? tasks[parentId] : null;
            var task = project.RestoreTask(taskDoc.Name, taskDoc.UniqueId, taskDoc.Id, parent);
            task.Duration = new Duration(taskDoc.DurationValue, taskDoc.DurationUnit, taskDoc.IsEstimated);
            task.Type = taskDoc.Type;
            if (taskDoc.Type != TaskType.FixedWork)
            {
                task.IsEffortDriven = taskDoc.IsEffortDriven;
            }

            task.IgnoresResourceCalendars = taskDoc.IgnoresResourceCalendars;
            task.FixedCost = taskDoc.FixedCost;
            task.FixedCostAccrual = taskDoc.FixedCostAccrual;
            task.Mode = taskDoc.Mode;
            task.IsActive = taskDoc.IsActive;
            task.MilestoneOverrideRaw = taskDoc.MilestoneOverride;
            task.SetConstraint(taskDoc.Constraint, taskDoc.ConstraintDate);
            task.Deadline = taskDoc.Deadline;
            task.Calendar = taskDoc.CalendarId is { } calendarId ? calendars[calendarId] : null;
            task.Priority = taskDoc.Priority;
            task.ManualStart = taskDoc.ManualStart;
            task.ManualFinish = taskDoc.ManualFinish;
            task.CustomWbs = taskDoc.CustomWbs;
            task.IsRecurring = taskDoc.IsRecurring;
            if (taskDoc.SplitParts is { Count: > 1 } parts)
            {
                task.RestoreSplitParts([.. parts.Select(p => (p.WorkMinutes, p.GapMinutes))]);
            }

            tasks.Add(taskDoc.Id, task);
        }

        foreach (var dependencyDoc in document.Dependencies)
        {
            project.RestoreLink(
                tasks[dependencyDoc.PredecessorId],
                tasks[dependencyDoc.SuccessorId],
                dependencyDoc.Type,
                Lag.Restore(dependencyDoc.LagKind, dependencyDoc.LagValue));
        }

        var resources = new Dictionary<Guid, Resource>();
        foreach (var resourceDoc in document.Resources)
        {
            var resource = project.RestoreResource(resourceDoc.Name, resourceDoc.Type, resourceDoc.UniqueId, resourceDoc.Id);
            resource.Initials = resourceDoc.Initials;
            resource.Group = resourceDoc.Group;
            resource.MaxUnits = resourceDoc.MaxUnits;
            resource.MaterialLabel = resourceDoc.MaterialLabel;
            resource.Accrual = resourceDoc.Accrual;
            resource.Calendar = resourceDoc.CalendarId is { } resourceCalendarId ? calendars[resourceCalendarId] : null;
            foreach (var tableDoc in resourceDoc.RateTables)
            {
                resource.RateTable(tableDoc.Table).RestoreEntries(
                    tableDoc.Entries.Select(e => new CostRate(
                        e.EffectiveFrom,
                        new Rate(e.StandardRate.Amount, e.StandardRate.Per),
                        new Rate(e.OvertimeRate.Amount, e.OvertimeRate.Per),
                        e.CostPerUse)));
            }

            resources.Add(resourceDoc.Id, resource);
        }

        foreach (var assignmentDoc in document.Assignments)
        {
            var assignment = project.RestoreAssignment(tasks[assignmentDoc.TaskId], resources[assignmentDoc.ResourceId], assignmentDoc.Id);
            assignment.Units = assignmentDoc.Units;
            assignment.WorkMinutes = assignmentDoc.WorkMinutes;
            assignment.Contour = assignmentDoc.Contour;
            assignment.DelayMinutes = assignmentDoc.DelayMinutes;
            assignment.RateTable = assignmentDoc.RateTable;
            assignment.RestoreCostInput(assignmentDoc.CostInput);
        }

        return project;
    }

    private static ResourceDocument ToDocument(Resource resource) => new()
    {
        Id = resource.Id,
        UniqueId = resource.UniqueId,
        Name = resource.Name,
        Type = resource.Type,
        Initials = resource.Initials,
        Group = resource.Group,
        MaxUnits = resource.MaxUnits,
        MaterialLabel = resource.MaterialLabel,
        Accrual = resource.Accrual,
        CalendarId = resource.Calendar?.Id,
        RateTables =
        [
            .. Enum.GetValues<CostRateTableId>()
                .Select(id => (Id: id, Table: resource.RateTable(id)))
                .Where(t => t.Table.Entries.Count > 1 || t.Table.Entries[0] != new CostRate(DateTime.MinValue, Rate.Zero, Rate.Zero, 0m))
                .Select(t => new RateTableDocument
                {
                    Table = t.Id,
                    Entries =
                    [
                        .. t.Table.Entries.Select(e => new CostRateDocument
                        {
                            EffectiveFrom = e.EffectiveFrom,
                            StandardRate = new RateDocument(e.StandardRate.Amount, e.StandardRate.Per),
                            OvertimeRate = new RateDocument(e.OvertimeRate.Amount, e.OvertimeRate.Per),
                            CostPerUse = e.CostPerUse,
                        }),
                    ],
                }),
        ],
    };

    private static CalendarDocument ToDocument(WorkCalendar calendar) => new()
    {
        Id = calendar.Id,
        Name = calendar.Name,
        BaseCalendarId = calendar.BaseCalendar?.Id,
        DefaultWeek = ToDays(calendar.DefaultWeek),
        Exceptions =
        [
            .. calendar.Exceptions.Select(e => new ExceptionDocument
            {
                Name = e.Name,
                Start = e.Start,
                End = e.End,
                Occurrences = e.Occurrences,
                Schedule = ToDay(e.Schedule)!,
                Recurrence = e.Recurrence is { } r ? ToDocument(r) : null,
            }),
        ],
        WorkWeeks =
        [
            .. calendar.WorkWeeks.Select(w => new WorkWeekDocument
            {
                Name = w.Name,
                Start = w.Start,
                End = w.End,
                Days = ToDays(w.Pattern),
            }),
        ],
    };

    private static TaskDocument ToDocument(ProjectTask task) => new()
    {
        Id = task.Id,
        UniqueId = task.UniqueId,
        ParentId = task.Parent is { IsRoot: false } parent ? parent.Id : null,
        Name = task.Name,
        Mode = task.Mode,
        IsActive = task.IsActive,
        DurationValue = task.Duration.Value,
        DurationUnit = task.Duration.Unit,
        IsEstimated = task.Duration.IsEstimated,
        MilestoneOverride = task.MilestoneOverrideRaw,
        Constraint = task.Constraint,
        ConstraintDate = task.ConstraintDate,
        Deadline = task.Deadline,
        CalendarId = task.Calendar?.Id,
        Priority = task.Priority,
        ManualStart = task.ManualStart,
        ManualFinish = task.ManualFinish,
        CustomWbs = task.CustomWbs,
        IsRecurring = task.IsRecurring,
        SplitParts = task.IsSplit
            ? [.. task.SplitParts.Select(p => new SplitPartDocument(p.WorkMinutes, p.GapMinutes))]
            : null,
        Type = task.Type,
        IsEffortDriven = task.IsEffortDriven,
        IgnoresResourceCalendars = task.IgnoresResourceCalendars,
        FixedCost = task.FixedCost,
        FixedCostAccrual = task.FixedCostAccrual,
    };

#pragma warning disable CA1859
    private static IReadOnlyList<DayDocument?> ToDays(WeeklyPattern pattern)
#pragma warning restore CA1859
    {
        var days = new DayDocument?[7];
        for (var i = 0; i < 7; i++)
        {
            days[i] = ToDay(pattern[(DayOfWeek)i]);
        }

        return days;
    }

    private static DayDocument? ToDay(DaySchedule? schedule)
        => schedule is { } s
            ? new DayDocument([.. s.Intervals.Select(i => new IntervalDocument(i.StartMinute, i.EndMinute))])
            : null;

    private static WeeklyPattern ToPattern(IReadOnlyList<DayDocument?> days)
    {
        if (days.Count != 7)
        {
            throw new InvalidDataException("A weekly pattern document must have exactly seven days.");
        }

        var pattern = WeeklyPattern.InheritAll;
        for (var i = 0; i < 7; i++)
        {
            if (days[i] is { } day)
            {
                pattern = pattern.With((DayOfWeek)i, ToSchedule(day));
            }
        }

        return pattern;
    }

    private static DaySchedule ToSchedule(DayDocument day)
        => day.Intervals.Count == 0
            ? DaySchedule.NonWorking
            : DaySchedule.Working([.. day.Intervals.Select(i => new TimeInterval(i.Start, i.End))]);

    private static RecurrenceDocument ToDocument(Recurrence recurrence) => recurrence switch
    {
        DailyRecurrence d => new RecurrenceDocument { Kind = "daily", Every = d.EveryDays },
        WeeklyRecurrence w => new RecurrenceDocument { Kind = "weekly", Every = w.EveryWeeks, Days = w.Days },
        MonthlyDayRecurrence m => new RecurrenceDocument { Kind = "monthlyDay", Every = m.EveryMonths, Day = m.Day },
        MonthlyWeekdayRecurrence m => new RecurrenceDocument { Kind = "monthlyWeekday", Every = m.EveryMonths, Ordinal = m.Ordinal, Weekday = m.Weekday },
        YearlyDateRecurrence y => new RecurrenceDocument { Kind = "yearlyDate", Month = y.Month, Day = y.Day },
        YearlyWeekdayRecurrence y => new RecurrenceDocument { Kind = "yearlyWeekday", Ordinal = y.Ordinal, Weekday = y.Weekday, Month = y.Month },
        _ => throw new NotSupportedException($"Unknown recurrence type {recurrence.GetType().Name}."),
    };

    private static Recurrence ToRecurrence(RecurrenceDocument document) => document.Kind switch
    {
        "daily" => new DailyRecurrence(document.Every),
        "weekly" => new WeeklyRecurrence(document.Every, document.Days),
        "monthlyDay" => new MonthlyDayRecurrence(document.Day, document.Every),
        "monthlyWeekday" => new MonthlyWeekdayRecurrence(document.Ordinal, document.Weekday, document.Every),
        "yearlyDate" => new YearlyDateRecurrence(document.Month, document.Day),
        "yearlyWeekday" => new YearlyWeekdayRecurrence(document.Ordinal, document.Weekday, document.Month),
        _ => throw new InvalidDataException($"Unknown recurrence kind '{document.Kind}'."),
    };
}
