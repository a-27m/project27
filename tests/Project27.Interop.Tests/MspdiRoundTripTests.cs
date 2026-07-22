using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Project27.Interop;
using Xunit;

namespace Project27.Interop.Tests;

/// <summary>MSPDI export → import → recalculate keeps the schedule (phase 5b).</summary>
public sealed class MspdiRoundTripTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project SampleProject()
    {
        var project = new Project("Alpha", At("2026-01-05 08:00"));
        var mornings = new WorkCalendar("Mornings", project.Calendar);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            mornings.SetDay(day, DaySchedule.Working(TimeInterval.FromTimes(new TimeOnly(8, 0), new TimeOnly(12, 0))));
        }

        project.AddCalendar(mornings);
        project.Calendar.AddException(new CalendarException("Holiday", new DateOnly(2026, 1, 14), null, DaySchedule.NonWorking));

        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour), new Rate(75m, RateUnit.Hour), 10m);
        dev.MaxUnits = 2m;
        var cement = project.AddResource("Cement", ResourceType.Material);
        cement.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(12.5m, RateUnit.Hour));
        cement.MaterialLabel = "t";

        var phase = project.AddTask("Phase");
        var design = project.AddTask("Design", Dur("2d"), phase);
        var build = project.AddTask("Build", Dur("3d"), phase);
        var ship = project.AddMilestone("Ship");
        project.Link(design, build, DependencyType.FinishToStart, Lag.Restore(LagKind.Working, 480m));
        project.Link(build, ship);
        build.Deadline = At("2026-01-30 17:00");
        build.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-07 08:00"));
        design.PercentComplete = 50;
        project.Assign(build, dev, units: 0.5m);
        project.Assign(build, cement, units: 10m);
        project.Recalculate();
        project.SetBaseline();
        project.Recalculate();
        return project;
    }

    [Fact]
    public void Schedule_survives_the_round_trip()
    {
        var original = SampleProject();
        var xml = MspdiWriter.Write(original);
        var imported = MspdiReader.Read(xml);

        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.StartDate, imported.StartDate);
        Assert.Equal(original.Tasks.Count, imported.Tasks.Count);

        foreach (var (expected, actual) in original.Tasks.Zip(imported.Tasks))
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.OutlineLevel, actual.OutlineLevel);
            Assert.Equal(expected.Start, actual.Start);
            Assert.Equal(expected.Finish, actual.Finish);
            Assert.Equal(expected.IsMilestone, actual.IsMilestone);
            Assert.Equal(expected.IsSummary, actual.IsSummary);
            Assert.Equal(expected.PercentComplete, actual.PercentComplete);
        }

        Assert.Equal(original.FinishDate, imported.FinishDate);
    }

    [Fact]
    public void Links_constraints_and_lags_survive()
    {
        var imported = MspdiReader.Read(MspdiWriter.Write(SampleProject()));
        var build = imported.Tasks.Single(t => t.Name == "Build");
        var link = build.Predecessors.Single();
        Assert.Equal(DependencyType.FinishToStart, link.Type);
        Assert.Equal(480m, link.Lag.Value);
        Assert.Equal(LagKind.Working, link.Lag.Kind);
        Assert.Equal(ConstraintType.StartNoEarlierThan, build.Constraint);
        Assert.Equal(At("2026-01-07 08:00"), build.ConstraintDate);
        Assert.Equal(At("2026-01-30 17:00"), build.Deadline);
    }

    [Fact]
    public void Resources_rates_and_assignments_survive()
    {
        var original = SampleProject();
        var imported = MspdiReader.Read(MspdiWriter.Write(original));

        var dev = imported.Resources.Single(r => r.Name == "Dev");
        Assert.Equal(ResourceType.Work, dev.Type);
        Assert.Equal(2m, dev.MaxUnits);
        Assert.Equal(50m, dev.StandardRate.Amount);
        Assert.Equal(10m, dev.RateTable(CostRateTableId.A).Entries[0].CostPerUse);

        var cement = imported.Resources.Single(r => r.Name == "Cement");
        Assert.Equal(ResourceType.Material, cement.Type);
        Assert.Equal("t", cement.MaterialLabel);

        var build = imported.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(2, build.Assignments.Count);
        var devAssignment = build.Assignments.Single(a => a.Resource.Name == "Dev");
        Assert.Equal(0.5m, devAssignment.Units);
        Assert.Equal(
            original.Tasks.Single(t => t.Name == "Build").Assignments.Single(a => a.Resource.Name == "Dev").WorkMinutes,
            devAssignment.WorkMinutes);
        Assert.Equal(original.TotalCost, imported.TotalCost);
    }

    [Fact]
    public void Calendars_and_exceptions_survive()
    {
        var imported = MspdiReader.Read(MspdiWriter.Write(SampleProject()));
        Assert.Contains(imported.Calendars, c => c.Name == "Mornings");
        // The Wednesday 2026-01-14 holiday still pushes work out.
        var holiday = imported.Calendar.GetDaySchedule(new DateOnly(2026, 1, 14));
        Assert.False(holiday.IsWorking);
    }

    [Fact]
    public void Baseline_zero_survives()
    {
        var imported = MspdiReader.Read(MspdiWriter.Write(SampleProject()));
        var build = imported.Tasks.Single(t => t.Name == "Build");
        var baseline = build.Baseline();
        Assert.NotNull(baseline);
        Assert.Equal(At("2026-01-08 08:00"), baseline!.Value.Start);
        Assert.Equal(1440m, baseline.Value.DurationMinutes);
        Assert.True(baseline.Value.Cost > 0);
    }

    [Fact]
    public void Calendars_with_a_24_hour_working_day_survive()
    {
        var project = new Project("Alpha", At("2026-01-05 08:00"));
        var testLab = WorkCalendar.Create24Hours("TestLab");
        project.AddCalendar(testLab);

        var xml = MspdiWriter.Write(project);
        Assert.Contains("24:00:00", xml);

        var imported = MspdiReader.Read(xml);
        var calendar = imported.Calendars.Single(c => c.Name == "TestLab");
        var monday = calendar.DefaultWeek[DayOfWeek.Monday]!.Value;
        Assert.True(monday.IsWorking);
        var interval = Assert.Single(monday.Intervals);
        Assert.Equal(0, interval.StartMinute);
        Assert.Equal(TimeInterval.MinutesPerDay, interval.EndMinute);
    }

    [Fact]
    public void Calendars_with_intervals_ending_at_midnight_survive()
    {
        var project = new Project("Alpha", At("2026-01-05 08:00"));
        var nightShift = WorkCalendar.CreateNightShift("Night Shift");
        project.AddCalendar(nightShift);

        var xml = MspdiWriter.Write(project);
        var imported = MspdiReader.Read(xml);

        var calendar = imported.Calendars.Single(c => c.Name == "Night Shift");
        var tuesday = calendar.DefaultWeek[DayOfWeek.Tuesday]!.Value;
        Assert.True(tuesday.IsWorking);
        Assert.Contains(tuesday.Intervals, i => i.StartMinute == 23 * 60 && i.EndMinute == 24 * 60);
    }

    [Fact]
    public void Foreign_documents_are_read_leniently()
    {
        const string minimal = """
            <Project xmlns="http://schemas.microsoft.com/project">
              <Name>External</Name>
              <StartDate>2026-03-02T08:00:00</StartDate>
              <Tasks>
                <Task><UID>1</UID><ID>1</ID><Name>Only</Name><OutlineLevel>1</OutlineLevel>
                  <Duration>PT16H0M0S</Duration><UnknownElement>ignored</UnknownElement></Task>
              </Tasks>
            </Project>
            """;
        var imported = MspdiReader.Read(minimal);
        Assert.Equal("External", imported.Name);
        var task = imported.Tasks.Single();
        Assert.Equal(960m, task.DurationMinutes);
        Assert.Equal(At("2026-03-02 08:00"), task.Start);
        Assert.Throws<InvalidDataException>(() => MspdiReader.Read("<NotAProject/>"));
    }
}
