using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>
/// Golden scenarios for the CPM engine. Project starts Monday 2026-01-05 08:00 on the
/// Standard calendar (8-12, 13-17): one day = 480 working minutes.
/// </summary>
public sealed class SchedulerTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    [Fact]
    public void Chain_of_finish_to_start_tasks()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Link(a, b);
        project.Recalculate();

        Assert.Equal(At("2026-01-05 08:00"), a.Start);
        Assert.Equal(At("2026-01-07 17:00"), a.Finish);
        Assert.Equal(At("2026-01-08 08:00"), b.Start);
        Assert.Equal(At("2026-01-09 17:00"), b.Finish);
        Assert.Equal(At("2026-01-09 17:00"), project.FinishDate);
        Assert.True(a.IsCritical);
        Assert.True(b.IsCritical);
        Assert.Equal(0m, a.TotalSlackMinutes);
    }

    [Fact]
    public void Parallel_branch_gets_slack()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("1d"));
        var c = project.AddTask("C", Dur("1d"));
        project.Link(a, c);
        project.Link(b, c);
        project.Recalculate();

        Assert.Equal(At("2026-01-08 08:00"), c.Start);
        Assert.Equal(960m, b.TotalSlackMinutes);  // two days
        Assert.Equal(960m, b.FreeSlackMinutes);
        Assert.False(b.IsCritical);
        Assert.True(a.IsCritical);
        Assert.True(c.IsCritical);
    }

    [Fact]
    public void Start_to_start_with_working_lag()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b, DependencyType.StartToStart, Lag.OfMinutes(480));
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), b.Start);
    }

    [Fact]
    public void Finish_to_finish_aligns_finishes()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b, DependencyType.FinishToFinish);
        project.Recalculate();

        Assert.Equal(At("2026-01-07 08:00"), b.Start);
        Assert.Equal(At("2026-01-07 17:00"), b.Finish);
    }

    [Fact]
    public void Start_to_finish_from_milestone()
    {
        var project = NewProject();
        var milestone = project.AddMilestone("M");
        milestone.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-07 17:00"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(milestone, b, DependencyType.StartToFinish);
        project.Recalculate();

        Assert.Equal(At("2026-01-07 17:00"), milestone.Start);
        Assert.Equal(At("2026-01-07 08:00"), b.Start);
        Assert.Equal(At("2026-01-07 17:00"), b.Finish);
    }

    [Fact]
    public void Negative_lag_is_lead()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Link(a, b, lag: Lag.OfMinutes(-480));
        project.Recalculate();

        Assert.Equal(At("2026-01-07 08:00"), b.Start); // overlaps A's last day
    }

    [Fact]
    public void Elapsed_lag_uses_clock_time()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("1d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b, lag: Lag.ElapsedMinutes(1440));
        project.Recalculate();

        // Mon 17:00 + 24 h clock = Tue 17:00 → next working time Wed 08:00.
        Assert.Equal(At("2026-01-07 08:00"), b.Start);
    }

    [Fact]
    public void Percent_lag_scales_with_predecessor_duration()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b, lag: Lag.Percent(50));
        project.Recalculate();

        // 50% of 2d = 1d after Tue 17:00 → Wed 17:00 → start Thu 08:00.
        Assert.Equal(At("2026-01-08 08:00"), b.Start);
    }

    [Fact]
    public void Milestone_sits_at_predecessor_finish_point()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("5d"));
        var m = project.AddMilestone("Done");
        project.Link(a, m);
        project.Recalculate();

        Assert.Equal(At("2026-01-09 17:00"), m.Start);
        Assert.Equal(m.Start, m.Finish);
        Assert.True(m.IsMilestone);
    }

    [Fact]
    public void Snet_constraint_delays_task()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("1d"));
        a.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-08 08:00"));
        project.Recalculate();

        Assert.Equal(At("2026-01-08 08:00"), a.Start);
        Assert.Equal(At("2026-01-08 17:00"), a.Finish);
    }

    [Fact]
    public void Must_start_on_overrides_dependencies_and_creates_negative_slack()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b);
        b.SetConstraint(ConstraintType.MustStartOn, At("2026-01-06 08:00"));
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), b.Start);          // pinned before A finishes
        Assert.Equal(-960m, a.TotalSlackMinutes);               // A is two days too late
        Assert.True(a.IsCritical);
    }

    [Fact]
    public void Alap_task_moves_to_its_late_dates()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        a.SetConstraint(ConstraintType.AsLateAsPossible);
        var b = project.AddTask("B", Dur("5d"));
        project.Recalculate();

        Assert.Equal(At("2026-01-09 17:00"), project.FinishDate);
        Assert.Equal(At("2026-01-07 08:00"), a.Start);
        Assert.Equal(At("2026-01-09 17:00"), a.Finish);
        Assert.Equal(0m, a.TotalSlackMinutes);
    }

    [Fact]
    public void Deadline_reduces_slack_without_moving_dates()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        a.Deadline = At("2026-01-06 17:00");
        project.AddTask("B", Dur("5d")); // drives project finish to Friday
        project.Recalculate();

        Assert.Equal(At("2026-01-05 08:00"), a.Start);          // dates unmoved
        Assert.Equal(-480m, a.TotalSlackMinutes);               // one day over deadline
        Assert.True(a.IsCritical);
    }

    [Fact]
    public void Summary_rolls_up_children_and_inherits_incoming_links()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("1d"));
        var summary = project.AddTask("S");
        var c1 = project.AddTask("C1", Dur("2d"), summary);
        var c2 = project.AddTask("C2", Dur("1d"), summary);
        project.Link(a, summary);
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), c1.Start);  // both children wait for A
        Assert.Equal(At("2026-01-06 08:00"), c2.Start);
        Assert.Equal(At("2026-01-06 08:00"), summary.Start);
        Assert.Equal(At("2026-01-07 17:00"), summary.Finish);
        Assert.True(summary.IsSummary);
        Assert.Equal(960m, summary.DurationMinutes);
    }

    [Fact]
    public void Link_from_summary_waits_for_all_children()
    {
        var project = NewProject();
        var summary = project.AddTask("S");
        project.AddTask("C1", Dur("2d"), summary);
        var c2 = project.AddTask("C2", Dur("4d"), summary);
        var b = project.AddTask("B", Dur("1d"));
        project.Link(summary, b);
        project.Recalculate();

        Assert.Equal(At("2026-01-09 08:00"), b.Start);   // after the 4d child
        Assert.True(c2.IsCritical);
        Assert.Equal(0m, c2.TotalSlackMinutes);
    }

    [Fact]
    public void Finish_to_finish_into_summary_binds_every_leaf()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var summary = project.AddTask("S");
        var c1 = project.AddTask("C1", Dur("1d"), summary);
        var c2 = project.AddTask("C2", Dur("3d"), summary);
        project.Link(a, summary, DependencyType.FinishToFinish);
        project.Recalculate();

        Assert.Equal(At("2026-01-06 08:00"), c1.Start);  // pushed so its finish meets A's
        Assert.Equal(At("2026-01-06 17:00"), c1.Finish);
        Assert.Equal(At("2026-01-05 08:00"), c2.Start);  // already finishes later
        Assert.Equal(At("2026-01-07 17:00"), c2.Finish);
    }

    [Fact]
    public void Manual_task_keeps_its_dates_and_drives_successors()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("5d"));
        var b = project.AddTask("B", Dur("1d"));
        b.Mode = TaskMode.Manual;
        b.ManualStart = At("2026-01-07 08:00");
        b.ManualFinish = At("2026-01-08 17:00");
        var c = project.AddTask("C", Dur("1d"));
        project.Link(a, b); // would push b to next week, but manual wins
        project.Link(b, c);
        project.Recalculate();

        Assert.Equal(At("2026-01-07 08:00"), b.Start);
        Assert.Equal(At("2026-01-08 17:00"), b.Finish);
        Assert.Equal(960m, b.DurationMinutes);           // recomputed from dates
        Assert.Equal(At("2026-01-09 08:00"), c.Start);
    }

    [Fact]
    public void Inactive_task_is_scheduled_but_drives_nothing()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("3d"));
        a.IsActive = false;
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b);
        var summary = project.AddTask("S");
        var inactiveChild = project.AddTask("C1", Dur("5d"), summary);
        inactiveChild.IsActive = false;
        project.AddTask("C2", Dur("1d"), summary);
        project.Recalculate();

        Assert.Equal(At("2026-01-05 08:00"), b.Start);           // ignores inactive predecessor
        Assert.Equal(At("2026-01-05 08:00"), a.Start);           // still gets display dates
        Assert.Equal(At("2026-01-05 17:00"), summary.Finish);    // rollup skips inactive child
        Assert.Equal(At("2026-01-05 17:00"), project.FinishDate);
    }

    [Fact]
    public void Task_calendar_overrides_project_calendar()
    {
        var project = NewProject();
        var around = WorkCalendar.Create24Hours();
        project.AddCalendar(around);
        var a = project.AddTask("A", Dur("1d"));
        a.Calendar = around;
        project.Recalculate();

        Assert.Equal(At("2026-01-05 16:00"), a.Finish); // 480 clock minutes from 08:00
    }

    [Fact]
    public void Split_task_schedules_segments_around_the_gap()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        a.SplitAt(Dur("1d"), Dur("1d"));
        project.Recalculate();

        Assert.True(a.IsSplit);
        Assert.Equal(2, a.Segments.Count);
        Assert.Equal(new TaskSegment(At("2026-01-05 08:00"), At("2026-01-05 17:00")), a.Segments[0]);
        Assert.Equal(new TaskSegment(At("2026-01-07 08:00"), At("2026-01-07 17:00")), a.Segments[1]);
        Assert.Equal(At("2026-01-07 17:00"), a.Finish);
    }

    [Fact]
    public void Changing_duration_clears_splits()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        a.SplitAt(Dur("1d"), Dur("1d"));
        a.Duration = Dur("3d");

        Assert.False(a.IsSplit);
    }

    [Fact]
    public void Recurring_task_generates_pinned_occurrences()
    {
        var project = NewProject();
        var recurring = project.AddRecurringTask(
            "Standup",
            Dur("1h"),
            new WeeklyRecurrence(1, DayOfWeekSet.Monday),
            new DateOnly(2026, 1, 5),
            occurrences: 4);
        project.Recalculate();

        Assert.True(recurring.IsRecurring);
        Assert.Equal(4, recurring.Children.Count);
        Assert.Equal(At("2026-01-05 08:00"), recurring.Children[0].Start);
        Assert.Equal(At("2026-01-12 08:00"), recurring.Children[1].Start);
        Assert.Equal(At("2026-01-26 09:00"), recurring.Children[3].Finish);
        Assert.All(recurring.Children, c => Assert.Equal(ConstraintType.StartNoEarlierThan, c.Constraint));
    }

    [Fact]
    public void Schedule_from_finish_computes_project_start()
    {
        var project = NewProject();
        project.ScheduleFrom = ScheduleFrom.ProjectFinish;
        project.FinishDate = At("2026-01-09 17:00");
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b);
        project.Recalculate();

        Assert.Equal(ConstraintType.AsLateAsPossible, a.Constraint); // SFF default
        Assert.Equal(At("2026-01-09 08:00"), b.Start);
        Assert.Equal(At("2026-01-09 17:00"), b.Finish);
        Assert.Equal(At("2026-01-07 08:00"), a.Start);
        Assert.Equal(At("2026-01-08 17:00"), a.Finish);
        Assert.Equal(At("2026-01-07 08:00"), project.StartDate);
    }

    [Fact]
    public void Backward_pass_tightens_slack_through_summary_links()
    {
        var project = NewProject();
        var summary = project.AddTask("S");
        var c1 = project.AddTask("C1", Dur("2d"), summary);
        var b = project.AddTask("B", Dur("1d"));
        project.Link(summary, b);
        project.Recalculate();

        Assert.Equal(0m, c1.TotalSlackMinutes);  // drives the successor through the summary
        Assert.True(c1.IsCritical);
        Assert.Equal(At("2026-01-07 08:00"), b.Start);
    }
}
