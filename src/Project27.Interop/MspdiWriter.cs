using System.Xml.Linq;
using Project27.Core;
using Project27.Core.Time;
using static Project27.Interop.Mspdi;

namespace Project27.Interop;

/// <summary>
/// MSPDI XML export (docs/spec/05-interop.md §5b). Outline levels are emitted
/// 1-based (deviation #10); percent lags are materialized into working minutes
/// and cost-resource identity flattens to a work resource (deviation #32).
/// </summary>
public static class MspdiWriter
{
    public static string Write(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.TimeSettings;
        var calendarUids = project.Calendars.Select((calendar, index) => (calendar, Uid: index + 1))
            .ToDictionary(pair => pair.calendar, pair => pair.Uid);

        var root = E("Project",
            E("SaveVersion", "14"),
            E("Name", project.Name),
            E("StartDate", Date(project.StartDate)),
            project.FinishDate is { } finish ? E("FinishDate", Date(finish)) : null,
            E("ScheduleFromStart", Flag(project.ScheduleFrom == ScheduleFrom.ProjectStart)),
            project.StatusDate is { } status ? E("StatusDate", Date(status)) : null,
            E("MinutesPerDay", Num(settings.MinutesPerDay)),
            E("MinutesPerWeek", Num(settings.MinutesPerWeek)),
            E("DaysPerMonth", Num(settings.DaysPerMonth)),
            E("WeekStartDay", Num((int)settings.WeekStartsOn)),
            E("CalendarUID", Num(calendarUids[project.Calendar])),
            E("Calendars", project.Calendars.Select(calendar => WriteCalendar(calendar, calendarUids))),
            E("Tasks", project.Tasks.Select(task => WriteTask(project, task, calendarUids))),
            E("Resources", project.Resources.Select(resource => WriteResource(resource, calendarUids))),
            E("Assignments", project.Tasks
                .SelectMany(t => t.Assignments)
                .Select((assignment, index) => WriteAssignment(assignment, index + 1))));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return document.Declaration + Environment.NewLine + document.Root;
    }

    private static XElement WriteCalendar(WorkCalendar calendar, Dictionary<WorkCalendar, int> uids)
    {
        var weekDays = new List<XElement>();
        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            if (calendar.DefaultWeek[day] is not { } schedule)
            {
                continue; // inherited days are omitted; MSP falls back to the base calendar
            }

            weekDays.Add(E("WeekDay",
                E("DayType", Num((int)day + 1)),
                E("DayWorking", Flag(schedule.IsWorking)),
                schedule.IsWorking
                    ? E("WorkingTimes", schedule.Intervals.Select(interval => E("WorkingTime",
                        E("FromTime", TimeOfDay(interval.StartMinute)),
                        E("ToTime", TimeOfDay(interval.EndMinute)))))
                    : null));
        }

        // Dated exceptions ride as DayType=0 week days (classic MSPDI shape).
        foreach (var exception in calendar.Exceptions.Where(e => e.Recurrence is null))
        {
            weekDays.Add(E("WeekDay",
                E("DayType", "0"),
                E("DayWorking", Flag(exception.Schedule.IsWorking)),
                E("TimePeriod",
                    E("FromDate", Date(exception.Start.ToDateTime(TimeOnly.MinValue))),
                    E("ToDate", Date((exception.End ?? exception.Start).ToDateTime(new TimeOnly(23, 59, 59))))),
                exception.Schedule.IsWorking
                    ? E("WorkingTimes", exception.Schedule.Intervals.Select(interval => E("WorkingTime",
                        E("FromTime", TimeOfDay(interval.StartMinute)),
                        E("ToTime", TimeOfDay(interval.EndMinute)))))
                    : null));
        }

        return E("Calendar",
            E("UID", Num(uids[calendar])),
            E("Name", calendar.Name),
            E("IsBaseCalendar", Flag(calendar.BaseCalendar is null)),
            calendar.BaseCalendar is { } baseCalendar ? E("BaseCalendarUID", Num(uids[baseCalendar])) : null,
            E("WeekDays", weekDays));
    }

    private static XElement WriteTask(Project project, ProjectTask task, Dictionary<WorkCalendar, int> calendarUids)
        => E("Task",
            E("UID", Num(task.UniqueId)),
            E("ID", Num(task.RowNumber)),
            E("Name", task.Name),
            E("Type", Num(TaskTypeCode(task.Type))),
            E("WBS", task.Wbs),
            E("OutlineNumber", task.OutlineNumber),
            E("OutlineLevel", Num(task.OutlineLevel + 1)), // MSPDI is 1-based (deviation #10)
            E("Priority", Num(task.Priority)),
            task.Start is { } start ? E("Start", Date(start)) : null,
            task.Finish is { } taskFinish ? E("Finish", Date(taskFinish)) : null,
            E("Duration", Duration(task.DurationMinutes)),
            E("Milestone", Flag(task.IsMilestone)),
            E("Summary", Flag(task.IsSummary)),
            E("Critical", Flag(task.IsCritical)),
            E("Active", Flag(task.IsActive)),
            E("Manual", Flag(task.Mode == TaskMode.Manual)),
            E("EffortDriven", Flag(task.IsEffortDriven)),
            E("Estimated", Flag(task.IsEstimated)),
            E("PercentComplete", Num(task.IsSummary ? 0 : task.PercentComplete)),
            task.IsSummary ? null : task.ActualStart is { } actualStart ? E("ActualStart", Date(actualStart)) : null,
            task.IsSummary ? null : task.ActualFinish is { } actualFinish ? E("ActualFinish", Date(actualFinish)) : null,
            E("ConstraintType", Num(ConstraintCode(task.Constraint))),
            task.ConstraintDate is { } constraintDate ? E("ConstraintDate", Date(constraintDate)) : null,
            task.Deadline is { } deadline ? E("Deadline", Date(deadline)) : null,
            task.Calendar is { } taskCalendar ? E("CalendarUID", Num(calendarUids[taskCalendar])) : null,
            E("FixedCost", Num(task.FixedCost)),
            E("FixedCostAccrual", Num(AccrualCode(task.FixedCostAccrual))),
            task.LevelingDelayMinutes > 0 ? E("LevelingDelay", Num(Math.Round(task.LevelingDelayMinutes * 10m))) : null,
            task.Baseline() is { } baseline
                ? E("Baseline",
                    E("Number", "0"),
                    baseline.Start is { } baselineStart ? E("Start", Date(baselineStart)) : null,
                    baseline.Finish is { } baselineFinish ? E("Finish", Date(baselineFinish)) : null,
                    E("Duration", Duration(baseline.DurationMinutes)),
                    E("Work", Duration(baseline.WorkMinutes)),
                    E("Cost", Num(Math.Round(baseline.Cost * 100m)))) // MSPDI costs are in cents… of the currency
                : null,
            task.Predecessors.Select(link => E("PredecessorLink",
                E("PredecessorUID", Num(link.Predecessor.UniqueId)),
                E("Type", Num(LinkCode(link.Type))),
                E("LinkLag", Num(Math.Round(MaterializedLagMinutes(link) * 10m))),
                E("LagFormat", link.Lag.Kind == LagKind.Elapsed ? "5" : "7"))));

    /// <summary>Percent lags have no MSPDI form: materialize to working minutes (deviation #32).</summary>
    private static decimal MaterializedLagMinutes(TaskDependency link) => link.Lag.Kind switch
    {
        LagKind.Percent => link.Predecessor.DurationMinutes * link.Lag.Value / 100m,
        _ => link.Lag.Value,
    };

    private static XElement WriteResource(Resource resource, Dictionary<WorkCalendar, int> calendarUids)
    {
        var baseRate = resource.RateTable(CostRateTableId.A).Entries[0];
        return E("Resource",
            E("UID", Num(resource.UniqueId)),
            E("ID", Num(resource.UniqueId)),
            E("Name", resource.Name),
            E("Type", Num(resource.Type == ResourceType.Material ? 0 : 1)), // cost flattens to work (deviation #32)
            resource.Initials is { } initials ? E("Initials", initials) : null,
            resource.Group is { } group ? E("Group", group) : null,
            E("MaxUnits", Num(resource.MaxUnits)),
            E("StandardRate", Num(HourlyRate(baseRate.StandardRate, resource))),
            E("StandardRateFormat", "2"),
            E("OvertimeRate", Num(HourlyRate(baseRate.OvertimeRate, resource))),
            E("OvertimeRateFormat", "2"),
            E("CostPerUse", Num(baseRate.CostPerUse)),
            E("AccrueAt", Num(AccrualCode(resource.Accrual))),
            resource.MaterialLabel is { } label ? E("MaterialLabel", label) : null,
            resource.Calendar is { } calendar ? E("CalendarUID", Num(calendarUids[calendar])) : null);
    }

    private static decimal HourlyRate(Rate rate, Resource resource)
        => resource.Type == ResourceType.Material || rate.IsZero
            ? rate.Amount
            : Math.Round(rate.CostForMinutes(60m, resource.Project.TimeSettings), 4);

    private static XElement WriteAssignment(Assignment assignment, int uid)
        => E("Assignment",
            E("UID", Num(uid)),
            E("TaskUID", Num(assignment.Task.UniqueId)),
            E("ResourceUID", Num(assignment.Resource.UniqueId)),
            E("Units", Num(assignment.Units)),
            E("Work", Duration(assignment.WorkMinutes)),
            assignment.DelayMinutes > 0 ? E("Delay", Num(Math.Round(assignment.DelayMinutes * 10m))) : null,
            assignment.Resource.Type == ResourceType.Cost ? E("Cost", Num(Math.Round(assignment.CostInput * 100m))) : null);
}
