using System.Globalization;
using Project27.Core;
using Project27.Core.Persistence;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>
/// Baselines, actuals, rescheduling, and EVM. Project starts Monday 2026-01-05
/// 08:00 on the Standard calendar (480 min/day).
/// </summary>
public sealed class TrackingTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    // -------------------------------------------------------------- baselines

    [Fact]
    public void Baseline_captures_dates_work_and_cost()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("2d"));
        var assignment = project.Assign(task, dev);
        project.Recalculate();
        project.SetBaseline();

        var baseline = task.Baseline()!.Value;
        Assert.Equal(At("2026-01-05 08:00"), baseline.Start);
        Assert.Equal(At("2026-01-06 17:00"), baseline.Finish);
        Assert.Equal(960m, baseline.DurationMinutes);
        Assert.Equal(960m, baseline.WorkMinutes);
        Assert.Equal(800m, baseline.Cost); // 16h × 50
        Assert.Equal(800m, assignment.Baseline()!.Value.Cost);

        // The live plan can drift; the baseline stays.
        task.Duration = Dur("4d");
        project.Recalculate();
        Assert.Equal(960m, task.Baseline()!.Value.DurationMinutes);

        project.ClearBaseline();
        Assert.Null(task.Baseline());
    }

    [Fact]
    public void Baseline_slots_are_independent_and_validated()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("1d"));
        project.Recalculate();
        project.SetBaseline(0);
        task.Duration = Dur("3d");
        project.Recalculate();
        project.SetBaseline(5);

        Assert.Equal(480m, task.Baseline(0)!.Value.DurationMinutes);
        Assert.Equal(1440m, task.Baseline(5)!.Value.DurationMinutes);
        Assert.Null(task.Baseline(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.SetBaseline(11));
    }

    // ---------------------------------------------------------------- actuals

    [Fact]
    public void Actual_start_pins_the_task_over_dependencies()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Link(a, b);
        b.ActualStart = At("2026-01-06 08:00"); // started a day before A finishes
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), b.Start);
        Assert.Equal(At("2026-01-07 17:00"), b.Finish);
    }

    [Fact]
    public void Percent_complete_backfills_the_actual_start()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("2d"));
        task.PercentComplete = 50;
        project.Recalculate();

        Assert.Equal(At("2026-01-05 08:00"), task.ActualStart);
        Assert.Null(task.ActualFinish);
        Assert.Equal(480m, task.CompletedMinutes);
        Assert.Equal(480m, task.RemainingMinutes);
    }

    [Fact]
    public void Actual_finish_completes_the_task_and_rewrites_duration()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("2d"));
        task.ActualStart = At("2026-01-05 08:00");
        task.ActualFinish = At("2026-01-08 17:00"); // took 4 days, not 2
        project.Recalculate();

        Assert.Equal(100, task.PercentComplete);
        Assert.Equal(At("2026-01-08 17:00"), task.Finish);
        Assert.Equal(1920m, task.DurationMinutes);

        task.PercentComplete = 60; // reopening clears the actual finish
        Assert.Null(task.ActualFinish);
    }

    [Fact]
    public void Remaining_duration_edit_extends_the_total_and_rederives_percent()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("4d"));
        task.PercentComplete = 50; // 2d done
        task.SetRemainingDuration(Dur("6d"));

        Assert.Equal(8m * 480m, task.DurationMinutes); // 2d done + 6d remaining
        Assert.Equal(25, task.PercentComplete);
    }

    [Fact]
    public void Summary_progress_rolls_up_from_leaves()
    {
        var project = NewProject();
        var phase = project.AddTask("Phase");
        var one = project.AddTask("One", Dur("1d"), phase);
        var three = project.AddTask("Three", Dur("3d"), phase);
        one.PercentComplete = 100;
        three.PercentComplete = 0;
        project.Recalculate();

        Assert.Equal(25, phase.PercentComplete); // 480 of 1920 minutes done
        Assert.Equal(one.ActualStart, phase.ActualStart);
        Assert.Null(phase.ActualFinish); // not all children finished
        Assert.Throws<InvalidOperationException>(() => phase.PercentComplete = 10);
    }

    // ------------------------------------------------------------- reschedule

    [Fact]
    public void Reschedule_splits_started_work_and_pushes_unstarted_tasks()
    {
        var project = NewProject();
        var started = project.AddTask("Started", Dur("4d"));
        var unstarted = project.AddTask("Unstarted", Dur("2d"));
        started.PercentComplete = 25; // 1d done: Mon
        project.StatusDate = At("2026-01-08 08:00"); // Thu morning

        project.RescheduleUncompletedWork();

        // 1d done Mon; the remaining 3d resume Thu..Mon.
        Assert.True(started.IsSplit);
        Assert.Equal(2, started.Segments.Count);
        Assert.Equal(At("2026-01-05 17:00"), started.Segments[0].Finish);
        Assert.Equal(At("2026-01-08 08:00"), started.Segments[1].Start);
        Assert.Equal(At("2026-01-12 17:00"), started.Finish);

        // The unstarted task moved wholesale behind the status date.
        Assert.Equal(ConstraintType.StartNoEarlierThan, unstarted.Constraint);
        Assert.Equal(At("2026-01-08 08:00"), unstarted.Start);
    }

    [Fact]
    public void Reschedule_needs_a_cutoff()
    {
        var project = NewProject();
        project.AddTask("T", Dur("1d"));
        Assert.Throws<InvalidOperationException>(() => project.RescheduleUncompletedWork());
    }

    // -------------------------------------------------------------------- EVM

    [Fact]
    public void Earned_value_at_the_status_date()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("4d")); // 32h × 50 = 1600
        project.Assign(task, dev);
        project.Recalculate();
        project.SetBaseline();

        // Half the baseline window elapsed, but only a quarter of the work done.
        task.PercentComplete = 25;
        project.StatusDate = At("2026-01-07 08:00"); // 2 of 4 days
        project.Recalculate();

        var evm = EarnedValue.ForTask(task);
        Assert.Equal(1600m, evm.Bac);
        Assert.Equal(800m, evm.Bcws);   // 50% planned
        Assert.Equal(400m, evm.Bcwp);   // 25% earned
        Assert.Equal(400m, evm.Acwp);   // derived actuals
        Assert.Equal(-400m, evm.Sv);
        Assert.Equal(0m, evm.Cv);
        Assert.Equal(0.5m, evm.Spi);
        Assert.Equal(1m, evm.Cpi);
        Assert.Equal(1600m, evm.Eac);
        Assert.Equal(0m, evm.Vac);

        var projectEvm = EarnedValue.ForProject(project);
        Assert.Equal(evm.Bcws, projectEvm.Bcws);
    }

    [Fact]
    public void Cost_overrun_shows_in_cpi_and_eac()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("2d"));
        var assignment = project.Assign(task, dev); // 800 planned
        project.Recalculate();
        project.SetBaseline();

        assignment.SetWork(Dur("32h")); // scope doubled after baselining: cost 1600
        task.PercentComplete = 50;
        project.StatusDate = At("2026-01-09 17:00"); // past the baseline finish
        project.Recalculate();

        var evm = EarnedValue.ForTask(task);
        Assert.Equal(800m, evm.Bac);
        Assert.Equal(800m, evm.Bcws);   // fully planned by now
        Assert.Equal(400m, evm.Bcwp);
        Assert.Equal(800m, evm.Acwp);   // 50% of the doubled cost
        Assert.Equal(0.5m, evm.Cpi);
        Assert.Equal(1600m, evm.Eac);   // BAC / CPI
        Assert.Equal(-800m, evm.Vac);
    }

    [Fact]
    public void Tasks_without_a_baseline_contribute_zero()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("2d"));
        task.PercentComplete = 50;
        project.Recalculate();
        Assert.Equal(EarnedValueData.Zero, EarnedValue.ForTask(task));
    }

    // ------------------------------------------------------------ persistence

    [Fact]
    public void Tracking_round_trips_through_the_document()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur("4d"));
        var assignment = project.Assign(task, dev);
        project.StatusDate = At("2026-01-07 08:00");
        project.Recalculate();
        project.SetBaseline(0);
        project.SetBaseline(3);
        task.PercentComplete = 40;
        task.ActualStart = At("2026-01-05 09:00");
        project.Recalculate();

        var restored = ProjectDocumentMapper.FromDocument(ProjectDocumentMapper.ToDocument(project));
        restored.Recalculate();

        var restoredTask = restored.Tasks.Single();
        Assert.Equal(40, restoredTask.PercentComplete);
        Assert.Equal(At("2026-01-05 09:00"), restoredTask.ActualStart);
        Assert.Equal(At("2026-01-07 08:00"), restored.StatusDate);
        Assert.Equal(task.Baseline(0), restoredTask.Baseline(0));
        Assert.Equal(task.Baseline(3), restoredTask.Baseline(3));
        Assert.Equal(assignment.Baseline(0), restoredTask.Assignments.Single().Baseline(0));
        Assert.Equal(EarnedValue.ForTask(task), EarnedValue.ForTask(restoredTask));
    }
}
