using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Project27.Core.Usage;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Time-phased work/cost distribution (phase 9c). Standard calendar, 480 min/day.</summary>
public sealed class TimephasedTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static (Project Project, Assignment Assignment) Setup(string duration, WorkContour contour = WorkContour.Flat)
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        var task = project.AddTask("Build", Dur(duration));
        var assignment = project.Assign(task, dev);
        if (contour != WorkContour.Flat)
        {
            assignment.SetContour(contour);
        }

        project.Recalculate();
        return (project, assignment);
    }

    [Fact]
    public void Flat_assignments_fill_daily_capacity()
    {
        var (_, assignment) = Setup("3d");
        var buckets = Timephased.ForAssignment(assignment);

        Assert.Equal(3, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(480m, b.WorkMinutes));
        Assert.All(buckets, b => Assert.Equal(480m, b.Cost)); // 8h × 60
        Assert.Equal(new DateOnly(2026, 1, 5), buckets[0].Date);
        Assert.Equal(assignment.WorkMinutes, buckets.Sum(b => b.WorkMinutes));
        Assert.Equal(assignment.Cost, Math.Round(buckets.Sum(b => b.Cost), 2));
    }

    [Fact]
    public void Weekends_get_no_buckets()
    {
        var (_, assignment) = Setup("7d"); // Mon 01-05 .. Tue 01-13
        var buckets = Timephased.ForAssignment(assignment);

        Assert.Equal(7, buckets.Count);
        Assert.DoesNotContain(buckets, b => b.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 1, 13), buckets[^1].Date);
    }

    [Fact]
    public void Contours_shape_the_distribution_but_conserve_work()
    {
        var (_, assignment) = Setup("5d", WorkContour.BackLoaded);
        // Back-loaded stretches 5d of work to 5d/0.6 span; buckets ramp up.
        var buckets = Timephased.ForAssignment(assignment);
        Assert.Equal(assignment.WorkMinutes, Math.Round(buckets.Sum(b => b.WorkMinutes), 6));
        Assert.True(buckets[0].WorkMinutes < buckets[^2].WorkMinutes);
        Assert.Equal(Math.Round(assignment.Cost, 4), Math.Round(buckets.Sum(b => b.Cost), 4));
    }

    [Fact]
    public void Per_use_cost_lands_at_the_accrual_point()
    {
        var (project, assignment) = Setup("3d");
        assignment.Resource.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, costPerUse: 90m);
        project.Recalculate();

        var start = Timephased.ForAssignment(assignment);
        Assert.Equal(480m + 30m, start.First(b => b.Date == new DateOnly(2026, 1, 5)).Cost, 2); // prorated over 3 days

        assignment.Resource.Accrual = CostAccrual.End;
        var atEnd = Timephased.ForAssignment(assignment);
        Assert.Equal(480m + 90m, atEnd[^1].Cost, 2);
        Assert.Equal(480m, atEnd[0].Cost, 2);
    }

    [Fact]
    public void Material_and_cost_resources_contribute_cost_only()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var cement = project.AddResource("Cement", ResourceType.Material);
        cement.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(10m, RateUnit.Hour));
        cement.Accrual = CostAccrual.Start;
        var travel = project.AddResource("Travel", ResourceType.Cost);
        var task = project.AddTask("Build", Dur("2d"));
        var materialAssignment = project.Assign(task, cement, units: 5m);   // 50
        var costAssignment = project.Assign(task, travel);
        costAssignment.CostInput = 300m;
        project.Recalculate();

        var material = Timephased.ForAssignment(materialAssignment);
        Assert.Equal(50m, material.Single().Cost);
        Assert.Equal(0m, material.Single().WorkMinutes);

        var expense = Timephased.ForAssignment(costAssignment);
        Assert.Equal(300m, expense.Sum(b => b.Cost), 2);

        var taskBuckets = Timephased.ForTask(task);
        Assert.Equal(task.Cost, Math.Round(taskBuckets.Sum(b => b.Cost), 2));
    }

    [Fact]
    public void Summaries_merge_children_and_weeks_aggregate_days()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(60m, RateUnit.Hour));
        var phase = project.AddTask("Phase");
        var a = project.AddTask("A", Dur("3d"), phase);
        var b = project.AddTask("B", Dur("3d"), phase);
        project.Assign(a, dev);
        project.Assign(b, dev);
        project.Recalculate();

        var daily = Timephased.ForTask(phase);
        Assert.Equal(960m, daily[0].WorkMinutes); // both tasks on Monday

        var weekly = Timephased.ByWeek(daily, DayOfWeek.Monday);
        Assert.Single(weekly);
        Assert.Equal(new DateOnly(2026, 1, 5), weekly[0].Date);
        Assert.Equal(2880m, weekly[0].WorkMinutes); // 2 × 3d
    }

    [Fact]
    public void Restrictive_resource_calendars_shape_the_buckets()
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
        var task = project.AddTask("Job", Dur("1d"));
        var assignment = project.Assign(task, ann); // 480 min at 4h/day = 2 mornings
        project.Recalculate();

        var buckets = Timephased.ForAssignment(assignment);
        Assert.Equal(2, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(240m, b.WorkMinutes));
    }

    [Fact]
    public void Unscheduled_assignments_produce_no_buckets()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Dur("1d"));
        var assignment = project.Assign(task, dev);
        Assert.Empty(Timephased.ForAssignment(assignment)); // never recalculated
    }
}
