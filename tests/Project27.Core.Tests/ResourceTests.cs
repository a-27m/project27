using System.Globalization;
using Project27.Core;
using Project27.Core.Persistence;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>
/// Resources, assignments, the task-type triangle, resource calendars in scheduling,
/// and cost rollup. Project starts Monday 2026-01-05 08:00 on the Standard calendar
/// (8-12, 13-17): one day = 480 working minutes.
/// </summary>
public sealed class ResourceTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    // ------------------------------------------------------------- resources

    [Fact]
    public void Resource_names_are_unique_case_insensitive()
    {
        var project = NewProject();
        project.AddResource("Dev");
        Assert.Throws<InvalidOperationException>(() => project.AddResource("dev"));

        var other = project.AddResource("Other");
        Assert.Throws<InvalidOperationException>(() => other.Name = "DEV");
    }

    [Fact]
    public void Non_work_resources_reject_calendars_and_work()
    {
        var project = NewProject();
        var cement = project.AddResource("Cement", ResourceType.Material);
        Assert.Throws<InvalidOperationException>(() => cement.Calendar = project.Calendar);

        var task = project.AddTask("T", Dur("1d"));
        Assert.Throws<ArgumentException>(() => project.Assign(task, cement, work: Dur("8h")));
        var assignment = project.Assign(task, cement, units: 5m);
        Assert.Throws<InvalidOperationException>(() => assignment.SetWork(Dur("8h")));
    }

    [Fact]
    public void Removing_a_resource_drops_its_assignments()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("2d"));
        project.Assign(task, dev);
        Assert.Single(task.Assignments);

        project.RemoveResource(dev);
        Assert.Empty(task.Assignments);
        Assert.Empty(project.Resources);
    }

    [Fact]
    public void Removing_a_task_drops_its_assignments_from_resources()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("2d"));
        project.Assign(task, dev);

        project.RemoveTask(task);
        Assert.Empty(dev.Assignments);
    }

    [Fact]
    public void Summary_tasks_cannot_be_assigned()
    {
        var project = NewProject();
        var parent = project.AddTask("Phase");
        project.AddTask("Child", Dur("1d"), parent);
        var dev = project.AddResource("Dev");
        Assert.Throws<InvalidOperationException>(() => project.Assign(parent, dev));
    }

    // -------------------------------------------------------------- triangle

    [Fact]
    public void Default_work_is_duration_times_units()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        var assignment = project.Assign(task, dev, units: 0.5m);

        Assert.Equal(4m * 480m * 0.5m, assignment.WorkMinutes); // 960 = 16h
        Assert.Equal(4m * 480m, task.DurationMinutes);          // unchanged
    }

    [Fact]
    public void Fixed_units_work_edit_recalculates_duration()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        var assignment = project.Assign(task, dev);

        assignment.SetWork(Dur("80h")); // 4800 min at 100%
        Assert.Equal(4800m, task.DurationMinutes); // 10d
        Assert.Equal(1m, assignment.Units);
    }

    [Fact]
    public void Fixed_units_units_edit_recalculates_duration_keeping_work()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        var assignment = project.Assign(task, dev); // 1920 min work

        assignment.SetUnits(2m);
        Assert.Equal(1920m, assignment.WorkMinutes);
        Assert.Equal(960m, task.DurationMinutes); // halved
    }

    [Fact]
    public void Fixed_duration_work_edit_recalculates_units()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        task.Type = TaskType.FixedDuration;
        var assignment = project.Assign(task, dev);

        assignment.SetWork(Dur("8h")); // 480 min over 1920 min duration
        Assert.Equal(1920m, task.DurationMinutes);
        Assert.Equal(0.25m, assignment.Units);
    }

    [Fact]
    public void Fixed_duration_duration_edit_recalculates_work()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        task.Type = TaskType.FixedDuration;
        var assignment = project.Assign(task, dev, units: 0.5m); // 960 min

        task.Duration = Dur("2d");
        Assert.Equal(480m, assignment.WorkMinutes);
        Assert.Equal(0.5m, assignment.Units);
    }

    [Fact]
    public void Fixed_work_duration_edit_recalculates_units()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("4d"));
        task.Type = TaskType.FixedWork;
        var assignment = project.Assign(task, dev); // 1920 min

        task.Duration = Dur("8d");
        Assert.Equal(1920m, assignment.WorkMinutes);
        Assert.Equal(0.5m, assignment.Units);
    }

    [Fact]
    public void Fixed_work_is_always_effort_driven()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("4d"));
        task.Type = TaskType.FixedWork;
        Assert.True(task.IsEffortDriven);
        Assert.Throws<InvalidOperationException>(() => task.IsEffortDriven = false);
    }

    [Fact]
    public void Effort_driven_add_keeps_total_work_and_shrinks_duration()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var helper = project.AddResource("Helper");
        var task = project.AddTask("T", Dur("4d"));
        task.IsEffortDriven = true;
        var first = project.Assign(task, dev); // 1920 min

        var second = project.Assign(task, helper);
        Assert.Equal(960m, first.WorkMinutes);
        Assert.Equal(960m, second.WorkMinutes);
        Assert.Equal(1920m, task.WorkMinutes);
        Assert.Equal(960m, task.DurationMinutes); // 2d
    }

    [Fact]
    public void Effort_driven_remove_redistributes_work()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var helper = project.AddResource("Helper");
        var task = project.AddTask("T", Dur("4d"));
        task.IsEffortDriven = true;
        var first = project.Assign(task, dev);
        var second = project.Assign(task, helper);

        project.Unassign(second);
        Assert.Equal(1920m, first.WorkMinutes);
        Assert.Equal(1920m, task.DurationMinutes); // back to 4d
    }

    [Fact]
    public void Non_effort_driven_add_just_adds_work()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var helper = project.AddResource("Helper");
        var task = project.AddTask("T", Dur("4d"));
        project.Assign(task, dev);
        project.Assign(task, helper);

        Assert.Equal(3840m, task.WorkMinutes); // 1920 each
        Assert.Equal(1920m, task.DurationMinutes);
    }

    [Fact]
    public void Contour_stretches_the_assignment_duration()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("3d"));
        var assignment = project.Assign(task, dev); // 1440 min

        assignment.SetContour(WorkContour.BackLoaded); // avg 0.60
        Assert.Equal(1440m, assignment.WorkMinutes);
        Assert.Equal(2400m, task.DurationMinutes); // 1440 / 0.6 = 5d
    }

    // ------------------------------------------------- scheduling with calendars

    [Fact]
    public void Resource_calendar_restricts_the_assignment_and_extends_the_finish()
    {
        var project = NewProject();
        var mornings = new WorkCalendar("Mornings", project.Calendar);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            mornings.SetDay(day, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        }

        project.AddCalendar(mornings);
        var ann = project.AddResource("Ann");
        ann.Calendar = mornings;
        var task = project.AddTask("Job", Dur("2d"));
        var assignment = project.Assign(task, ann); // 960 min at 4h/day = 4 mornings
        project.Recalculate();

        Assert.Equal(At("2026-01-05 08:00"), task.Start);
        Assert.Equal(At("2026-01-08 12:00"), task.Finish);
        Assert.Equal(At("2026-01-08 12:00"), assignment.Finish);

        task.IgnoresResourceCalendars = true;
        project.Recalculate();
        Assert.Equal(At("2026-01-06 17:00"), task.Finish);
    }

    [Fact]
    public void Assignment_delay_shifts_the_assignment_and_the_finish()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("2d"));
        var assignment = project.Assign(task, dev);
        assignment.DelayMinutes = 480m; // 1d
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), assignment.Start);
        Assert.Equal(At("2026-01-07 17:00"), assignment.Finish);
        Assert.Equal(At("2026-01-07 17:00"), task.Finish);
        Assert.Equal(At("2026-01-05 08:00"), task.Start);
    }

    [Fact]
    public void Successors_wait_for_the_assignment_extended_finish()
    {
        var project = NewProject();
        var mornings = new WorkCalendar("Mornings", project.Calendar);
        mornings.SetDay(DayOfWeek.Monday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        mornings.SetDay(DayOfWeek.Tuesday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        mornings.SetDay(DayOfWeek.Wednesday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        mornings.SetDay(DayOfWeek.Thursday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        mornings.SetDay(DayOfWeek.Friday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        project.AddCalendar(mornings);
        var ann = project.AddResource("Ann");
        ann.Calendar = mornings;

        var first = project.AddTask("First", Dur("1d")); // 480 min = 2 mornings
        var second = project.AddTask("Second", Dur("1d"));
        project.Assign(first, ann);
        project.Link(first, second);
        project.Recalculate();

        Assert.Equal(At("2026-01-06 12:00"), first.Finish);
        Assert.Equal(At("2026-01-06 13:00"), second.Start); // next working time on Standard
    }

    // ----------------------------------------------------------------- costs

    [Fact]
    public void Costs_roll_up_from_assignments_and_fixed_cost()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour), costPerUse: 100m);
        var cement = project.AddResource("Cement", ResourceType.Material);
        cement.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(12.5m, RateUnit.Hour));
        var travel = project.AddResource("Travel", ResourceType.Cost);

        var parent = project.AddTask("Phase");
        var task = project.AddTask("Build", Dur("4d"), parent);
        task.FixedCost = 40m;
        project.Assign(task, dev);                       // 32h × 50 + 100 = 1700
        project.Assign(task, cement, units: 10m);        // 10 × 12.5 = 125
        project.Assign(task, travel).CostInput = 300m;   // 300
        project.Recalculate();

        Assert.Equal(2165m, task.Cost);
        Assert.Equal(2165m, parent.Cost);
        Assert.Equal(2165m, project.TotalCost);
        Assert.Equal(1920m, project.TotalWorkMinutes);
    }

    [Fact]
    public void Rate_effective_at_assignment_start_prices_the_whole_assignment()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        dev.RateTable(CostRateTableId.A).SetRate(At("2026-01-06 00:00"), new Rate(100m, RateUnit.Hour));

        var early = project.AddTask("Early", Dur("1d"));
        var late = project.AddTask("Late", Dur("1d"));
        late.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-07 08:00"));
        var earlyAssignment = project.Assign(early, dev);
        var lateAssignment = project.Assign(late, dev);
        project.Recalculate();

        Assert.Equal(400m, earlyAssignment.Cost);  // 8h × 50
        Assert.Equal(800m, lateAssignment.Cost);   // 8h × 100
    }

    [Fact]
    public void Assignment_rate_table_selection_changes_the_price()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        dev.RateTable(CostRateTableId.B).SetRate(DateTime.MinValue, new Rate(80m, RateUnit.Hour));

        var task = project.AddTask("T", Dur("1d"));
        var assignment = project.Assign(task, dev);
        assignment.RateTable = CostRateTableId.B;
        project.Recalculate();

        Assert.Equal(640m, assignment.Cost); // 8h × 80
    }

    [Fact]
    public void Daily_rates_convert_via_time_settings()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(400m, RateUnit.Day));
        var task = project.AddTask("T", Dur("3d"));
        var assignment = project.Assign(task, dev);
        project.Recalculate();

        Assert.Equal(1200m, assignment.Cost); // 3 days × 400
    }

    [Fact]
    public void Base_rate_entry_cannot_be_removed()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        Assert.Throws<InvalidOperationException>(() => dev.RateTable(CostRateTableId.A).RemoveRate(DateTime.MinValue));
    }

    [Fact]
    public void Inactive_tasks_are_excluded_from_rollups()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("T", Dur("1d"));
        project.Assign(task, dev);
        project.Recalculate();
        Assert.Equal(400m, project.TotalCost);

        task.IsActive = false;
        project.Recalculate();
        Assert.Equal(0m, project.TotalCost);
        Assert.Equal(0m, project.TotalWorkMinutes);
    }

    // ------------------------------------------------------------ persistence

    [Fact]
    public void Document_round_trip_preserves_resources_and_assignments()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.Initials = "DV";
        dev.Group = "Eng";
        dev.MaxUnits = 2m;
        dev.Accrual = CostAccrual.End;
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour), new Rate(75m, RateUnit.Hour), 10m);
        dev.RateTable(CostRateTableId.B).SetRate(At("2026-06-01 00:00"), new Rate(80m, RateUnit.Hour));
        var cement = project.AddResource("Cement", ResourceType.Material);
        cement.MaterialLabel = "t";
        var travel = project.AddResource("Travel", ResourceType.Cost);

        var task = project.AddTask("Build", Dur("4d"));
        task.Type = TaskType.FixedWork;
        task.FixedCost = 99m;
        task.FixedCostAccrual = CostAccrual.Start;
        task.IgnoresResourceCalendars = true;
        var assignment = project.Assign(task, dev, units: 0.5m);
        assignment.SetContour(WorkContour.Bell);
        assignment.DelayMinutes = 60m;
        assignment.RateTable = CostRateTableId.B;
        project.Assign(task, cement, units: 3m);
        project.Assign(task, travel).CostInput = 500m;
        project.Recalculate();

        var restored = ProjectDocumentMapper.FromDocument(ProjectDocumentMapper.ToDocument(project));
        restored.Recalculate();

        var restoredDev = restored.Resources.Single(r => r.Name == "Dev");
        Assert.Equal("DV", restoredDev.Initials);
        Assert.Equal(2m, restoredDev.MaxUnits);
        Assert.Equal(CostAccrual.End, restoredDev.Accrual);
        Assert.Equal(new Rate(75m, RateUnit.Hour), restoredDev.RateTable(CostRateTableId.A).Entries[0].OvertimeRate);
        Assert.Equal(2, restoredDev.RateTable(CostRateTableId.B).Entries.Count);

        var restoredTask = restored.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(TaskType.FixedWork, restoredTask.Type);
        Assert.True(restoredTask.IsEffortDriven);
        Assert.True(restoredTask.IgnoresResourceCalendars);
        Assert.Equal(99m, restoredTask.FixedCost);
        Assert.Equal(CostAccrual.Start, restoredTask.FixedCostAccrual);
        Assert.Equal(3, restoredTask.Assignments.Count);

        var restoredAssignment = restoredTask.Assignments.Single(a => a.Resource.Name == "Dev");
        Assert.Equal(0.5m, restoredAssignment.Units);
        Assert.Equal(assignment.WorkMinutes, restoredAssignment.WorkMinutes);
        Assert.Equal(WorkContour.Bell, restoredAssignment.Contour);
        Assert.Equal(60m, restoredAssignment.DelayMinutes);
        Assert.Equal(CostRateTableId.B, restoredAssignment.RateTable);
        Assert.Equal(500m, restoredTask.Assignments.Single(a => a.Resource.Name == "Travel").CostInput);

        Assert.Equal(project.TotalCost, restored.TotalCost);
        Assert.Equal(project.TotalWorkMinutes, restored.TotalWorkMinutes);
    }

    [Fact]
    public void Schema_1_documents_still_load()
    {
        var project = NewProject();
        project.AddTask("T", Dur("2d"));
        var document = ProjectDocumentMapper.ToDocument(project) with { SchemaVersion = 1, Resources = [], Assignments = [] };
        var restored = ProjectDocumentMapper.FromDocument(document);
        Assert.Empty(restored.Resources);
        Assert.Single(restored.Tasks);
    }

    // -------------------------------------------------------------- schedule intersection

    [Fact]
    public void Schedule_intersection_keeps_only_common_working_time()
    {
        var standard = WorkCalendar.CreateStandard();
        var nights = WorkCalendar.CreateNightShift();
        var both = new ScheduleIntersection(standard, nights);

        // Standard Monday: 8-12, 13-17. Night shift Monday starts at 23:00.
        var monday = new DateOnly(2026, 1, 5);
        Assert.False(both.GetDaySchedule(monday).IsWorking
            && both.GetDaySchedule(monday).WorkingMinutes == standard.GetDaySchedule(monday).WorkingMinutes);

        var halfDay = new WorkCalendar("Half", defaultWeek: WeeklyPattern.InheritAll
            .With(DayOfWeek.Monday, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(10, 0), new TimeOnly(15, 0)))));
        var overlap = new ScheduleIntersection(standard, halfDay).GetDaySchedule(monday);
        Assert.Equal(2, overlap.Intervals.Length); // 10-12 and 13-15
        Assert.Equal(240, overlap.WorkingMinutes);
    }
}
