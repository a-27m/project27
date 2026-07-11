using System.Globalization;
using Project27.Core;
using Project27.Core.Persistence;
using Project27.Core.Scheduling;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Resource leveling and task drivers (phase 10). Standard calendar, 480 min/day.</summary>
public sealed class LevelingTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static (Project Project, Resource Dev) NewProject()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        return (project, dev);
    }

    [Fact]
    public void Parallel_tasks_on_one_resource_get_serialized()
    {
        var (project, dev) = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Assign(a, dev);
        project.Assign(b, dev);

        var result = project.Level();

        // Both start Monday at 200% demand; one of them moves out.
        Assert.Single(result.Delays);
        Assert.Empty(result.RemainingOverallocations);
        Assert.Equal(At("2026-01-05 08:00"), a.Start);
        Assert.Equal(At("2026-01-07 08:00"), b.Start); // after A's two days
        Assert.Equal(At("2026-01-08 17:00"), project.FinishDate);
    }

    [Fact]
    public void Lower_priority_tasks_are_delayed_first()
    {
        var (project, dev) = NewProject();
        var important = project.AddTask("Important", Dur("2d"));
        var lowly = project.AddTask("Lowly", Dur("2d"));
        important.Priority = 900;
        lowly.Priority = 100;
        project.Assign(important, dev);
        project.Assign(lowly, dev);

        var result = project.Level();
        Assert.Equal("Lowly", result.Delays.Single().Task.Name);
        Assert.Equal(At("2026-01-05 08:00"), important.Start);
    }

    [Fact]
    public void Priority_1000_is_never_leveled()
    {
        var (project, dev) = NewProject();
        var locked1 = project.AddTask("Locked1", Dur("2d"));
        var locked2 = project.AddTask("Locked2", Dur("2d"));
        locked1.Priority = 1000;
        locked2.Priority = 1000;
        project.Assign(locked1, dev);
        project.Assign(locked2, dev);

        var result = project.Level();
        Assert.Empty(result.Delays);
        Assert.NotEmpty(result.RemainingOverallocations); // reported, untouched
    }

    [Fact]
    public void Raised_max_units_absorb_parallel_demand()
    {
        var (project, dev) = NewProject();
        dev.MaxUnits = 2m;
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Assign(a, dev);
        project.Assign(b, dev);

        var result = project.Level();
        Assert.Empty(result.Delays);
        Assert.Equal(a.Start, b.Start);
    }

    [Fact]
    public void Clear_leveling_restores_the_unleveled_schedule()
    {
        var (project, dev) = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Assign(a, dev);
        project.Assign(b, dev);
        project.Level();
        Assert.NotEqual(a.Start, b.Start);

        project.ClearLeveling();
        Assert.Equal(a.Start, b.Start);
        Assert.Equal(0m, b.LevelingDelayMinutes);
    }

    [Fact]
    public void Leveling_respects_dependencies_of_the_delayed_task()
    {
        var (project, dev) = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        var after = project.AddTask("After", Dur("1d"));
        project.Assign(a, dev);
        project.Assign(b, dev);
        project.Link(b, after);

        project.Level();
        // A has slack, B drives a successor: the leveler moves A and leaves B's chain alone.
        Assert.Equal(At("2026-01-05 08:00"), b.Start);
        Assert.Equal(At("2026-01-07 08:00"), a.Start);
        Assert.Equal(At("2026-01-07 08:00"), after.Start);
        Assert.Empty(project.FindOverallocations());
    }

    [Fact]
    public void Leveling_delay_round_trips_through_the_document()
    {
        var (project, dev) = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("2d"));
        project.Assign(a, dev);
        project.Assign(b, dev);
        project.Level();

        var restored = ProjectDocumentMapper.FromDocument(ProjectDocumentMapper.ToDocument(project));
        restored.Recalculate();
        var restoredB = restored.Tasks.Single(t => t.Name == "B");
        Assert.Equal(b.LevelingDelayMinutes, restoredB.LevelingDelayMinutes);
        Assert.Equal(b.Start, restoredB.Start);
    }

    // ---------------------------------------------------------------- drivers

    [Fact]
    public void Drivers_identify_the_binding_predecessor()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var a = project.AddTask("A", Dur("2d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Link(a, b);
        project.Recalculate();

        var drivers = TaskDrivers.Explain(b);
        var binding = drivers.Where(d => d.Binding).ToList();
        Assert.Single(binding);
        Assert.Equal(TaskDriverKind.Predecessor, binding[0].Kind);
        Assert.Equal(a.UniqueId, binding[0].PredecessorUid);

        var first = TaskDrivers.Explain(a);
        Assert.Equal(TaskDriverKind.ProjectStart, first.Single(d => d.Binding).Kind);
    }

    [Fact]
    public void Drivers_report_constraints_actuals_and_leveling()
    {
        var (project, dev) = NewProject();
        var a = project.AddTask("A", Dur("2d"));
        var pinned = project.AddTask("Pinned", Dur("1d"));
        pinned.SetConstraint(ConstraintType.StartNoEarlierThan, At("2026-01-12 08:00"));
        project.Recalculate();
        Assert.Equal(
            TaskDriverKind.Constraint,
            TaskDrivers.Explain(pinned).Single(d => d.Binding).Kind);

        pinned.ActualStart = At("2026-01-14 08:00");
        project.Recalculate();
        Assert.Equal(
            TaskDriverKind.ActualStart,
            TaskDrivers.Explain(pinned).Single(d => d.Binding).Kind);

        var b = project.AddTask("B", Dur("2d"));
        project.Assign(a, dev);
        project.Assign(b, dev);
        project.Level();
        var leveled = project.Tasks.Single(t => t.LevelingDelayMinutes > 0);
        Assert.Contains(TaskDrivers.Explain(leveled), d => d.Kind == TaskDriverKind.LevelingDelay && d.Binding);
    }

    // ----------------------------------------------------------------- import

    [Fact]
    public void Resource_import_copies_definitions_and_skips_clashes()
    {
        var (target, _) = NewProject(); // already has "Dev"
        var source = new Project("Pool", At("2026-01-05 08:00"));
        var dev = source.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(99m, RateUnit.Hour));
        var qa = source.AddResource("QA");
        qa.MaxUnits = 2m;
        qa.RateTable(CostRateTableId.B).SetRate(DateTime.MinValue, new Rate(40m, RateUnit.Hour));

        var skipped = target.ImportResources(source);

        Assert.Equal(["Dev"], skipped);
        Assert.Equal(2, target.Resources.Count);
        var importedQa = target.Resources.Single(r => r.Name == "QA");
        Assert.Equal(2m, importedQa.MaxUnits);
        Assert.Equal(40m, importedQa.RateTable(CostRateTableId.B).Entries[0].StandardRate.Amount);
        // The pre-existing Dev kept its (zero) rate; the source's 99/h was not copied.
        Assert.Equal(0m, target.Resources.Single(r => r.Name == "Dev").StandardRate.Amount);
    }
}
