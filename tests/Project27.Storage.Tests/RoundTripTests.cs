using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Project27.Storage;
using Xunit;

namespace Project27.Storage.Tests;

public sealed class RoundTripTests : IDisposable
{
    private readonly string _path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"p27-test-{Guid.NewGuid():N}.p27");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Project BuildRichProject()
    {
        var project = new Project("Roundtrip", At("2026-01-05 08:00"));
        project.TimeSettings.MinutesPerDay = 420;
        project.CriticalSlackThresholdMinutes = 60;

        var resourceCalendar = new WorkCalendar("Alice", baseCalendar: project.Calendar);
        resourceCalendar.SetDay(DayOfWeek.Friday, DaySchedule.NonWorking);
        resourceCalendar.AddException(new CalendarException(
            "Biweekly off",
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 6, 30),
            recurrence: new WeeklyRecurrence(2, DayOfWeekSet.Friday),
            occurrences: 5));
        resourceCalendar.AddWorkWeek(new WorkWeek(
            "Crunch",
            new DateOnly(2026, 2, 2),
            new DateOnly(2026, 2, 8),
            WeeklyPattern.InheritAll.With(DayOfWeek.Saturday, DaySchedule.Working(new TimeInterval(9 * 60, 13 * 60)))));
        project.AddCalendar(resourceCalendar);

        var summary = project.AddTask("Summary");
        var design = project.AddTask("Design", Duration.Parse("3d"), summary);
        design.Priority = 700;
        design.Deadline = At("2026-01-09 17:00");
        var build = project.AddTask("Build", Duration.Parse("4d?"), summary);
        build.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-07 08:00"));
        build.SplitAt(Duration.Parse("1d"), Duration.Parse("2d"));
        build.Calendar = resourceCalendar;
        var manual = project.AddTask("Review", Duration.Parse("1d"));
        manual.Mode = TaskMode.Manual;
        manual.ManualStart = At("2026-01-19 08:00");
        manual.ManualFinish = At("2026-01-20 17:00");
        manual.CustomWbs = "REV-1";
        var inactive = project.AddTask("Dropped", Duration.Parse("2d"));
        inactive.IsActive = false;
        var milestone = project.AddMilestone("Ship");

        project.Link(design, build, DependencyType.StartToStart, Lag.OfMinutes(480));
        project.Link(build, milestone, lag: Lag.Percent(25));
        project.Link(manual, milestone, DependencyType.FinishToFinish, Lag.ElapsedMinutes(720));

        project.AddRecurringTask(
            "Standup",
            Duration.Parse("1h"),
            new WeeklyRecurrence(1, DayOfWeekSet.Monday),
            new DateOnly(2026, 1, 5),
            occurrences: 3);

        project.Recalculate();
        return project;
    }

    [Fact]
    public void Project_survives_save_and_load_byte_for_byte_on_schedule()
    {
        var original = BuildRichProject();
        SqliteProjectStore.Create(_path, original);

        var loaded = SqliteProjectStore.Open(_path).Load();

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.StartDate, loaded.StartDate);
        Assert.Equal(original.FinishDate, loaded.FinishDate);
        Assert.Equal(420, loaded.TimeSettings.MinutesPerDay);
        Assert.Equal(60m, loaded.CriticalSlackThresholdMinutes);
        Assert.Equal(original.Calendars.Count, loaded.Calendars.Count);
        Assert.Equal(original.Tasks.Count, loaded.Tasks.Count);

        foreach (var (expected, actual) in original.Tasks.Zip(loaded.Tasks))
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.UniqueId, actual.UniqueId);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.OutlineNumber, actual.OutlineNumber);
            Assert.Equal(expected.Wbs, actual.Wbs);
            Assert.Equal(expected.Mode, actual.Mode);
            Assert.Equal(expected.IsActive, actual.IsActive);
            Assert.Equal(expected.Duration, actual.Duration);
            Assert.Equal(expected.Constraint, actual.Constraint);
            Assert.Equal(expected.ConstraintDate, actual.ConstraintDate);
            Assert.Equal(expected.Deadline, actual.Deadline);
            Assert.Equal(expected.Priority, actual.Priority);
            Assert.Equal(expected.Calendar?.Id, actual.Calendar?.Id);
            Assert.Equal(expected.IsRecurring, actual.IsRecurring);
            Assert.Equal(expected.IsSplit, actual.IsSplit);
            Assert.Equal(expected.Predecessors.Count, actual.Predecessors.Count);

            // Recomputed schedule must be identical.
            Assert.Equal(expected.Start, actual.Start);
            Assert.Equal(expected.Finish, actual.Finish);
            Assert.Equal(expected.LateStart, actual.LateStart);
            Assert.Equal(expected.TotalSlackMinutes, actual.TotalSlackMinutes);
            Assert.Equal(expected.IsCritical, actual.IsCritical);
            Assert.Equal(expected.Segments, actual.Segments);
        }

        var loadedBuild = loaded.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(2, loadedBuild.Segments.Count);
        var loadedDependency = loadedBuild.Predecessors.Single();
        Assert.Equal(DependencyType.StartToStart, loadedDependency.Type);
        Assert.Equal(LagKind.Working, loadedDependency.Lag.Kind);
        Assert.Equal(480m, loadedDependency.Lag.Value);

        var alice = loaded.Calendars.Single(c => c.Name == "Alice");
        Assert.Equal(loaded.Calendar.Id, alice.BaseCalendar?.Id);
        Assert.Single(alice.Exceptions);
        Assert.Single(alice.WorkWeeks);
        Assert.False(alice.GetDaySchedule(new DateOnly(2026, 1, 9)).IsWorking);   // Friday off
        Assert.True(alice.GetDaySchedule(new DateOnly(2026, 2, 7)).IsWorking);    // crunch Saturday
    }

    [Fact]
    public void Create_refuses_to_overwrite_and_open_requires_existence()
    {
        var project = new Project("P", At("2026-01-05 08:00"));
        SqliteProjectStore.Create(_path, project);

        Assert.Throws<IOException>(() => SqliteProjectStore.Create(_path, project));
        Assert.Throws<FileNotFoundException>(() => SqliteProjectStore.Open(_path + ".missing"));
    }

    [Fact]
    public void Save_overwrites_previous_snapshot()
    {
        var project = new Project("P", At("2026-01-05 08:00"));
        var store = SqliteProjectStore.Create(_path, project);
        project.AddTask("Later", Duration.Parse("2d"));
        project.Recalculate();
        store.Save(project);

        var loaded = SqliteProjectStore.Open(_path).Load();

        Assert.Single(loaded.Tasks);
        Assert.Equal("Later", loaded.Tasks[0].Name);
    }
}
