using System.Globalization;
using Project27.Core;
using Project27.Core.Persistence;
using Project27.Core.Scheduling;
using Project27.Core.Time;
using Project27.Core.Usage;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>
/// Tracking-parity epic: deviations #13, #14, #16, #17, #19, #20, #23, #28, #29.
/// Standard calendar, 480 min/day, week starts Monday 2026-01-05.
/// </summary>
public sealed class TrackingParityTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    // ------------------------------------------------------------- #14 contours

    [Fact]
    public void Contour_averages_are_locked_to_the_decile_tables()
    {
        foreach (var contour in Enum.GetValues<WorkContour>())
        {
            Assert.Equal(contour.Deciles().Sum() / 1000m, contour.AverageUtilization());
        }

        // Golden values: changing a decile table must break this loudly.
        Assert.Equal(0.60m, WorkContour.BackLoaded.AverageUtilization());
        Assert.Equal(0.50m, WorkContour.Bell.AverageUtilization());
        Assert.Equal(0.70m, WorkContour.Turtle.AverageUtilization());
    }

    [Fact]
    public void Contoured_distribution_exactly_fills_the_scheduled_span()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        var task = project.AddTask("Build", Dur("4d"));
        var assignment = project.Assign(task, dev);
        assignment.SetContour(WorkContour.LatePeak);
        project.Recalculate();

        var buckets = Timephased.ForAssignment(assignment);
        Assert.Equal(assignment.WorkMinutes, buckets.Sum(b => b.WorkMinutes));
        Assert.Equal(DateOnly.FromDateTime(assignment.Start!.Value), buckets[0].Date);
        Assert.Equal(DateOnly.FromDateTime(assignment.Finish!.Value), buckets[^1].Date);
    }

    // ------------------------------------------------------- #17 rate-band costs

    [Fact]
    public void Rate_band_changes_mid_assignment_price_each_day_by_its_band()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        dev.RateTable(CostRateTableId.A).SetRate(At("2026-01-07 00:00"), new Rate(120m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("5d")); // Mon 01-05 .. Fri 01-09
        var assignment = project.Assign(task, dev);
        project.Recalculate();

        // Mon+Tue at 60/h, Wed..Fri at 120/h — not the whole span at the start band.
        Assert.Equal((2m * 480m) + (3m * 960m), assignment.Cost);
        Assert.Equal(assignment.Cost, Timephased.ForAssignment(assignment).Sum(b => b.Cost));
    }

    // -------------------------------------------------- #13 variable consumption

    [Fact]
    public void Variable_material_consumption_accrues_per_working_time()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var fuel = project.AddResource("Fuel", ResourceType.Material);
        fuel.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(5m, RateUnit.Hour));
        var task = project.AddTask("Haul", Dur("3d"));
        var assignment = project.Assign(task, fuel, units: 10m); // 10 per day
        assignment.MaterialRateUnit = RateUnit.Day;
        project.Recalculate();

        Assert.Equal(30m, assignment.MaterialQuantity); // 10/day × 3d
        Assert.Equal(150m, assignment.Cost);            // 30 × 5

        var buckets = Timephased.ForAssignment(assignment);
        Assert.Equal(3, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(50m, b.Cost));
        Assert.All(buckets, b => Assert.Equal(0m, b.WorkMinutes));
    }

    [Fact]
    public void Variable_consumption_is_material_only()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("1d"));
        var assignment = project.Assign(task, dev);
        Assert.Throws<InvalidOperationException>(() => assignment.MaterialRateUnit = RateUnit.Day);
    }

    // ------------------------------------------- #16 split/manual assignment dates

    [Fact]
    public void Split_task_usage_skips_the_gap_days()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        var task = project.AddTask("Build", Dur("2d"));
        project.Assign(task, dev);
        task.SplitAt(Dur("1d"), Dur("1d")); // Mon | gap Tue | Wed
        project.Recalculate();

        var assignment = task.Assignments[0];
        var buckets = Timephased.ForAssignment(assignment);
        Assert.Equal(assignment.WorkMinutes, buckets.Sum(b => b.WorkMinutes));
        Assert.DoesNotContain(buckets, b => b.Date == new DateOnly(2026, 1, 6) && b.WorkMinutes > 0);
        Assert.Equal(task.Start, assignment.Start);
        Assert.Equal(task.Finish, assignment.Finish);
    }

    [Fact]
    public void Resource_calendar_narrows_split_task_assignment_dates()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var mornings = new WorkCalendar("Mornings", project.Calendar);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            mornings.SetDay(day, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        }

        project.AddCalendar(mornings);
        var ann = project.AddResource("Ann");
        ann.Calendar = mornings;
        var task = project.AddTask("Job", Dur("2d"));
        project.Assign(task, ann);
        task.SplitAt(Dur("1d"), Dur("1d"));
        project.Recalculate();

        var assignment = task.Assignments[0];
        // The assignment now lives on Ann's mornings inside the segments, not the raw task span.
        Assert.Equal(At("2026-01-05 08:00"), assignment.Start);
        Assert.Equal(At("2026-01-07 12:00"), assignment.Finish);
        Assert.DoesNotContain(Timephased.ForAssignment(assignment), b => b.Date == new DateOnly(2026, 1, 6) && b.WorkMinutes > 0);
    }

    // --------------------------------------------------------- #20 scalar actuals

    [Fact]
    public void Actual_work_and_cost_derive_from_percent_until_entered()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("2d"));
        var assignment = project.Assign(task, dev);
        task.PercentComplete = 50;
        project.Recalculate();

        Assert.Equal(480m, task.ActualWorkMinutes); // derived: 960 × 50%
        Assert.Equal(480m, task.ActualCost);
        Assert.Equal(480m, task.RemainingWorkMinutes);

        assignment.ActualWorkMinutes = 600m;
        assignment.ActualCost = 700m;
        Assert.Equal(600m, task.ActualWorkMinutes);
        Assert.Equal(700m, task.ActualCost);
        Assert.Equal(360m, task.RemainingWorkMinutes);

        assignment.ActualWorkMinutes = null;
        Assert.Equal(480m, task.ActualWorkMinutes); // back to derived
    }

    [Fact]
    public void Explicit_actual_cost_feeds_acwp()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("2d"));
        var assignment = project.Assign(task, dev);
        project.Recalculate();
        project.SetBaseline();
        task.PercentComplete = 50;
        project.StatusDate = At("2026-01-06 17:00");
        project.Recalculate();

        Assert.Equal(480m, EarnedValue.ForTask(task).Acwp); // derived
        assignment.ActualCost = 800m;
        Assert.Equal(800m, EarnedValue.ForTask(task).Acwp); // explicit actual
    }

    // ----------------------------------------------------- #19 BCWS by accrual

    [Fact]
    public void Bcws_places_assignment_baseline_cost_by_accrual()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("4d"));
        project.Assign(task, dev);
        project.Recalculate();
        project.SetBaseline();
        project.StatusDate = At("2026-01-06 17:00"); // 2 of 4 days in
        project.Recalculate();

        var bac = EarnedValue.ForTask(task).Bac;
        Assert.Equal(bac / 2m, EarnedValue.ForTask(task).Bcws); // prorated (default)

        dev.Accrual = CostAccrual.End;
        Assert.Equal(0m, EarnedValue.ForTask(task).Bcws);

        dev.Accrual = CostAccrual.Start;
        Assert.Equal(bac, EarnedValue.ForTask(task).Bcws);
    }

    // ------------------------------------------------- #23 reschedule split tasks

    [Fact]
    public void Reschedule_moves_remaining_segments_of_split_started_tasks()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var task = project.AddTask("Build", Dur("4d"));
        task.SplitAt(Dur("1d"), Dur("1d")); // Mon | gap Tue | Wed..Fri
        task.PercentComplete = 25;          // exactly the first segment
        project.Recalculate();

        project.RescheduleUncompletedWork(At("2026-01-12 08:00"));

        Assert.Equal(2, task.Segments.Count);
        Assert.Equal(At("2026-01-05 08:00"), task.Segments[0].Start); // completed work untouched
        Assert.Equal(At("2026-01-05 17:00"), task.Segments[0].Finish);
        Assert.Equal(At("2026-01-12 08:00"), task.Segments[1].Start); // remaining block at the cutoff
        Assert.Equal(At("2026-01-14 17:00"), task.Segments[1].Finish);
    }

    // ------------------------------------------------------ #28/#29 leveling

    private static (Project Project, Resource Resource) LevelingSetup()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        return (project, dev);
    }

    [Fact]
    public void Leveling_order_id_only_ignores_priorities()
    {
        var (project, dev) = LevelingSetup();
        var a = project.AddTask("A", Dur("1d"));
        var b = project.AddTask("B", Dur("1d"));
        a.Priority = 100; // standard order would delay A
        b.Priority = 900;
        project.Assign(a, dev);
        project.Assign(b, dev);

        var byPriority = project.Level();
        Assert.Equal("A", Assert.Single(byPriority.Delays).Task.Name);

        var byId = project.Level(new LevelingOptions { Order = LevelingOrder.IdOnly });
        Assert.Equal("B", Assert.Single(byId.Delays).Task.Name);
        Assert.Empty(byId.RemainingOverallocations);
    }

    [Fact]
    public void Minute_granularity_levels_by_the_exact_excess()
    {
        var (project, dev) = LevelingSetup();
        dev.MaxUnits = 1.5m; // capacity 720 min/day
        var a = project.AddTask("A", Dur("1d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Assign(a, dev);
        project.Assign(b, dev);

        var result = project.Level(new LevelingOptions { Granularity = LevelingGranularity.Minute });
        Assert.Empty(result.RemainingOverallocations);
        Assert.Equal(240m, Assert.Single(result.Delays).DelayMinutes); // the excess, not a whole day
    }

    [Fact]
    public void Leveling_splits_remaining_work_of_started_tasks_when_allowed()
    {
        var (project, dev) = LevelingSetup();
        var a = project.AddTask("A", Dur("4d"));
        var b = project.AddTask("B", Dur("2d"));
        b.Priority = 1000; // protected: only A can move
        b.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-06 08:00"));
        project.Assign(a, dev);
        project.Assign(b, dev);
        a.ActualStart = At("2026-01-05 08:00");
        a.PercentComplete = 25; // Monday done; Tue..Thu remaining collide with B
        project.Recalculate();

        var untouched = project.Level();
        Assert.Empty(untouched.Delays);
        Assert.NotEmpty(untouched.RemainingOverallocations); // started task is untouchable by default

        var split = project.Level(new LevelingOptions { SplitInProgress = true });
        Assert.Empty(split.RemainingOverallocations);
        Assert.Equal("A", Assert.Single(split.SplitTasks).Name);
        Assert.Equal(At("2026-01-05 08:00"), a.Segments[0].Start); // completed work never moves
        Assert.Equal(At("2026-01-05 17:00"), a.Segments[0].Finish);
        Assert.True(a.IsSplit);
        Assert.Equal(At("2026-01-05 08:00"), a.ActualStart);
    }

    // ------------------------------------------------------- persistence roundtrip

    [Fact]
    public void Schema_7_roundtrips_material_rate_unit_and_actuals()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        var fuel = project.AddResource("Fuel", ResourceType.Material);
        var task = project.AddTask("Build", Dur("2d"));
        var work = project.Assign(task, dev);
        var material = project.Assign(task, fuel, units: 4m);
        material.MaterialRateUnit = RateUnit.Day;
        work.ActualWorkMinutes = 120m;
        work.ActualCost = 45.5m;
        project.Recalculate();

        var document = ProjectDocumentMapper.ToDocument(project);
        Assert.Equal(7, document.SchemaVersion);
        var restored = ProjectDocumentMapper.FromDocument(document);
        var restoredTask = restored.Tasks.Single(t => t.Name == "Build");
        var restoredWork = restoredTask.Assignments.Single(a => a.Resource.Name == "Dev");
        var restoredMaterial = restoredTask.Assignments.Single(a => a.Resource.Name == "Fuel");
        Assert.Equal(RateUnit.Day, restoredMaterial.MaterialRateUnit);
        Assert.Equal(120m, restoredWork.ActualWorkMinutes);
        Assert.Equal(45.5m, restoredWork.ActualCost);
    }
}
