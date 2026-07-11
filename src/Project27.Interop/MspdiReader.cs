using System.Xml.Linq;
using Project27.Core;
using Project27.Core.Time;
using static Project27.Interop.Mspdi;

namespace Project27.Interop;

/// <summary>
/// MSPDI XML import (docs/spec/05-interop.md §5b). Lenient: unknown elements are
/// ignored, missing optionals default. Recalculates before returning, so imported
/// plans schedule under our clean-room semantics (deviations.md applies).
/// </summary>
public static class MspdiReader
{
    public static Project Read(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The document has no root element.");
        if (root.Name.LocalName != "Project")
        {
            throw new InvalidDataException($"Expected an MSPDI <Project> root, found <{root.Name.LocalName}>.");
        }

        // Calendars first, so the project can be born with its own.
        var calendarsById = new Dictionary<int, WorkCalendar>();
        var calendarElements = root.Element(Ns + "Calendars")?.Elements(Ns + "Calendar").ToList() ?? [];
        foreach (var element in calendarElements)
        {
            calendarsById[Int(element, "UID")] = ReadCalendar(element);
        }

        foreach (var element in calendarElements)
        {
            if (Int(element, "BaseCalendarUID", -1) is var baseUid and >= 0 && calendarsById.TryGetValue(baseUid, out var baseCalendar))
            {
                calendarsById[Int(element, "UID")].SetBaseCalendar(baseCalendar);
            }
        }

        var projectCalendar = calendarsById.TryGetValue(Int(root, "CalendarUID", -1), out var main)
            ? main
            : WorkCalendar.CreateStandard();
        var project = new Project(
            Text(root, "Name") is { Length: > 0 } name ? name : "Imported project",
            DateOrNull(root, "StartDate") ?? DateTime.Today.AddHours(8),
            projectCalendar);
        foreach (var calendar in calendarsById.Values)
        {
            if (!ReferenceEquals(calendar, projectCalendar))
            {
                project.AddCalendar(calendar);
            }
        }

        if (!Bool(root, "ScheduleFromStart", fallback: true))
        {
            project.ScheduleFrom = ScheduleFrom.ProjectFinish;
            project.FinishDate = DateOrNull(root, "FinishDate");
        }

        project.StatusDate = DateOrNull(root, "StatusDate");
        if (Int(root, "MinutesPerDay") is var minutesPerDay and > 0)
        {
            project.TimeSettings.MinutesPerDay = minutesPerDay;
        }

        if (Int(root, "MinutesPerWeek") is var minutesPerWeek and > 0)
        {
            project.TimeSettings.MinutesPerWeek = minutesPerWeek;
        }

        if (Dec(root, "DaysPerMonth") is var daysPerMonth and > 0)
        {
            project.TimeSettings.DaysPerMonth = daysPerMonth;
        }

        project.TimeSettings.WeekStartsOn = (DayOfWeek)Math.Clamp(Int(root, "WeekStartDay", 1), 0, 6);

        ReadTasks(root, project, calendarsById);
        var resourcesById = ReadResources(root, project, calendarsById);
        ReadAssignments(root, project, resourcesById);

        project.Recalculate();
        return project;
    }

    private static WorkCalendar ReadCalendar(XElement element)
    {
        var isBase = Bool(element, "IsBaseCalendar", fallback: true);
        var pattern = WeeklyPattern.InheritAll;
        var exceptions = new List<CalendarException>();
        foreach (var weekDay in element.Element(Ns + "WeekDays")?.Elements(Ns + "WeekDay") ?? [])
        {
            var dayType = Int(weekDay, "DayType");
            var working = Bool(weekDay, "DayWorking");
            var schedule = working ? ReadWorkingTimes(weekDay) : DaySchedule.NonWorking;
            if (dayType is >= 1 and <= 7)
            {
                pattern = pattern.With((DayOfWeek)(dayType - 1), schedule);
            }
            else if (dayType == 0 && weekDay.Element(Ns + "TimePeriod") is { } period)
            {
                var from = DateOrNull(period, "FromDate") ?? DateTime.Today;
                var to = DateOrNull(period, "ToDate") ?? from;
                exceptions.Add(new CalendarException(
                    $"Imported exception {exceptions.Count + 1}",
                    DateOnly.FromDateTime(from),
                    DateOnly.FromDateTime(to),
                    schedule));
            }
        }

        var calendar = new WorkCalendar(
            Text(element, "Name") is { Length: > 0 } name ? name : $"Calendar {Int(element, "UID")}",
            baseCalendar: null,
            defaultWeek: isBase && pattern.Equals(WeeklyPattern.InheritAll) ? WorkCalendar.CreateStandard().DefaultWeek : pattern);
        foreach (var exception in exceptions)
        {
            calendar.AddException(exception);
        }

        return calendar;
    }

    private static DaySchedule ReadWorkingTimes(XElement weekDay)
    {
        var intervals = new List<TimeInterval>();
        foreach (var workingTime in weekDay.Element(Ns + "WorkingTimes")?.Elements(Ns + "WorkingTime") ?? [])
        {
            var from = Text(workingTime, "FromTime");
            var to = Text(workingTime, "ToTime");
            if (from is { Length: > 0 } && to is { Length: > 0 })
            {
                var end = MinutesOfDay(to);
                intervals.Add(new TimeInterval(MinutesOfDay(from), end == 0 ? 24 * 60 : end));
            }
        }

        return intervals.Count == 0 ? DaySchedule.NonWorking : DaySchedule.Working([.. intervals]);
    }

    private static void ReadTasks(XElement root, Project project, Dictionary<int, WorkCalendar> calendarsById)
    {
        var byUid = new Dictionary<int, ProjectTask>();
        var links = new List<(int SuccessorUid, XElement Element)>();
        var parents = new Stack<(ProjectTask Task, int Level)>();

        foreach (var element in root.Element(Ns + "Tasks")?.Elements(Ns + "Task") ?? [])
        {
            if (Bool(element, "IsNull"))
            {
                continue;
            }

            var level = Math.Max(1, Int(element, "OutlineLevel", 1)); // 1-based in MSPDI
            while (parents.Count > 0 && parents.Peek().Level >= level)
            {
                parents.Pop();
            }

            var parent = parents.Count > 0 ? parents.Peek().Task : null;
            var task = project.AddTask(Text(element, "Name") is { Length: > 0 } name ? name : "(unnamed)", parent: parent);
            parents.Push((task, level));
            byUid[Int(element, "UID")] = task;

            task.Type = TaskTypeFromCode(Int(element, "Type"));
            if (task.Type != TaskType.FixedWork)
            {
                task.IsEffortDriven = Bool(element, "EffortDriven");
            }

            if (Text(element, "Duration") is { Length: > 0 } duration)
            {
                task.Duration = Core.Time.Duration.FromMinutes(
                    ParseDuration(duration), DurationUnit.Days, project.TimeSettings, Bool(element, "Estimated"));
            }

            task.Priority = Math.Clamp(Int(element, "Priority", 500), 0, 1000);
            task.IsActive = Bool(element, "Active", fallback: true);
            if (Bool(element, "Milestone"))
            {
                task.IsMilestone = true;
            }

            if (Bool(element, "Manual"))
            {
                task.Mode = TaskMode.Manual;
                task.ManualStart = DateOrNull(element, "Start");
                task.ManualFinish = DateOrNull(element, "Finish");
            }

            var constraint = ConstraintFromCode(Int(element, "ConstraintType"));
            if (constraint != ConstraintType.AsSoonAsPossible)
            {
                var date = DateOrNull(element, "ConstraintDate");
                if (constraint == ConstraintType.AsLateAsPossible || date is not null)
                {
                    task.SetConstraint(constraint, date);
                }
            }

            task.Deadline = DateOrNull(element, "Deadline");
            if (calendarsById.TryGetValue(Int(element, "CalendarUID", -1), out var taskCalendar))
            {
                task.Calendar = taskCalendar;
            }

            task.FixedCost = Dec(element, "FixedCost");
            task.FixedCostAccrual = AccrualFromCode(Int(element, "FixedCostAccrual", 3));
            task.LevelingDelayMinutes = Dec(element, "LevelingDelay") / 10m;
            var percent = Math.Clamp(Int(element, "PercentComplete"), 0, 100);
            if (percent > 0)
            {
                task.PercentComplete = percent;
            }

            task.ActualStart = DateOrNull(element, "ActualStart");
            if (DateOrNull(element, "ActualFinish") is { } actualFinish)
            {
                task.ActualFinish = actualFinish;
            }

            if (element.Element(Ns + "Baseline") is { } baseline)
            {
                task.SetBaselineSlot(0, new TaskBaseline(
                    DateOrNull(baseline, "Start"),
                    DateOrNull(baseline, "Finish"),
                    Text(baseline, "Duration") is { Length: > 0 } baselineDuration ? ParseDuration(baselineDuration) : 0m,
                    Text(baseline, "Work") is { Length: > 0 } baselineWork ? ParseDuration(baselineWork) : 0m,
                    Dec(baseline, "Cost") / 100m));
            }

            foreach (var link in element.Elements(Ns + "PredecessorLink"))
            {
                links.Add((Int(element, "UID"), link));
            }
        }

        foreach (var (successorUid, element) in links)
        {
            if (!byUid.TryGetValue(successorUid, out var successor)
                || !byUid.TryGetValue(Int(element, "PredecessorUID"), out var predecessor))
            {
                continue;
            }

            project.Link(
                predecessor,
                successor,
                LinkFromCode(Int(element, "Type", 1)),
                LagFrom(Int(element, "LinkLag"), Int(element, "LagFormat", 7)));
        }

        // Imported summaries lose their explicit rows; our outline recomputes them.
        _ = byUid;
    }

    private static Dictionary<int, Resource> ReadResources(XElement root, Project project, Dictionary<int, WorkCalendar> calendarsById)
    {
        var byUid = new Dictionary<int, Resource>();
        foreach (var element in root.Element(Ns + "Resources")?.Elements(Ns + "Resource") ?? [])
        {
            var name = Text(element, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue; // MSP files carry an unnamed UID-0 placeholder resource
            }

            var resource = project.AddResource(name, Int(element, "Type", 1) == 0 ? ResourceType.Material : ResourceType.Work);
            resource.Initials = Text(element, "Initials");
            resource.Group = Text(element, "Group");
            resource.MaxUnits = Dec(element, "MaxUnits", 1m);
            resource.MaterialLabel = Text(element, "MaterialLabel");
            resource.Accrual = AccrualFromCode(Int(element, "AccrueAt", 3));
            if (calendarsById.TryGetValue(Int(element, "CalendarUID", -1), out var calendar) && resource.Type == ResourceType.Work)
            {
                resource.Calendar = calendar;
            }

            var standard = Dec(element, "StandardRate");
            var overtime = Dec(element, "OvertimeRate");
            var perUse = Dec(element, "CostPerUse");
            if (standard != 0m || overtime != 0m || perUse != 0m)
            {
                resource.RateTable(CostRateTableId.A).SetRate(
                    DateTime.MinValue,
                    new Rate(standard, resource.Type == ResourceType.Material ? RateUnit.Hour : RateUnit.Hour),
                    new Rate(overtime, RateUnit.Hour),
                    perUse);
            }

            byUid[Int(element, "UID")] = resource;
        }

        return byUid;
    }

    private static void ReadAssignments(XElement root, Project project, Dictionary<int, Resource> resourcesById)
    {
        var tasksByUid = project.Tasks.ToDictionary(t => t.UniqueId);
        var importedByMspdiUid = new Dictionary<int, ProjectTask>();
        // Our AddTask assigned fresh uids in document order == MSPDI order; map by position.
        var mspdiUids = (root.Element(Ns + "Tasks")?.Elements(Ns + "Task") ?? [])
            .Where(e => !Bool(e, "IsNull"))
            .Select(e => Int(e, "UID"))
            .ToList();
        var ourTasks = project.Tasks.ToList();
        for (var i = 0; i < Math.Min(mspdiUids.Count, ourTasks.Count); i++)
        {
            importedByMspdiUid[mspdiUids[i]] = ourTasks[i];
        }

        foreach (var element in root.Element(Ns + "Assignments")?.Elements(Ns + "Assignment") ?? [])
        {
            if (!importedByMspdiUid.TryGetValue(Int(element, "TaskUID"), out var task)
                || !resourcesById.TryGetValue(Int(element, "ResourceUID"), out var resource)
                || task.IsSummary
                || task.Assignments.Any(a => ReferenceEquals(a.Resource, resource)))
            {
                continue;
            }

            // Restore path: exact units/work without triangle side effects.
            var assignment = project.RestoreAssignment(task, resource, Guid.NewGuid());
            assignment.Units = Math.Max(resource.Type == ResourceType.Work ? 0.01m : 0m, Dec(element, "Units", 1m));
            assignment.WorkMinutes = Text(element, "Work") is { Length: > 0 } work ? ParseDuration(work) : 0m;
            assignment.DelayMinutes = Math.Max(0m, Dec(element, "Delay") / 10m);
        }

        _ = tasksByUid;
    }
}
