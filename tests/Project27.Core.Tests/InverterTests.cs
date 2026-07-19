using System.Globalization;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Undo via command inverses (phase 12b).</summary>
public sealed class InverterTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    private static void RoundTrip(Project project, ProjectCommand command)
    {
        var (_, inverse) = CommandInverter.ApplyWithInverse(project, command);
        Assert.NotNull(inverse);
        CommandExecutor.Apply(project, inverse!);
    }

    [Fact]
    public void Set_task_inverse_restores_every_touched_field()
    {
        var project = NewProject();
        var task = project.AddTask("Original", Duration.Parse("2d"));
        task.Deadline = At("2026-02-01 17:00");
        project.Recalculate();

        RoundTrip(project, new SetTaskCommand
        {
            Uid = task.UniqueId,
            Name = "Changed",
            Duration = "5d",
            Priority = 900,
            SpaceAfter = 4,
            ClearDeadline = true,
            PercentComplete = 60,
        });

        Assert.Equal("Original", task.Name);
        Assert.Equal(960m, task.DurationMinutes);
        Assert.Equal(500, task.Priority);
        Assert.Null(task.Formatting);
        Assert.Equal(At("2026-02-01 17:00"), task.Deadline);
        Assert.Equal(0, task.PercentComplete);
    }

    [Fact]
    public void Description_undo_redo_round_trips_through_a_null_baseline()
    {
        var project = NewProject();
        var task = project.AddTask("A", Duration.Parse("1d"));
        project.Recalculate();

        var setCommand = new SetTaskCommand { Uid = task.UniqueId, Description = "Notes." };
        var (_, undoSet) = CommandInverter.ApplyWithInverse(project, setCommand);
        Assert.Equal("Notes.", task.Description);

        CommandExecutor.Apply(project, undoSet!); // undo: back to no description
        Assert.Null(task.Description);

        CommandExecutor.Apply(project, setCommand); // redo
        Assert.Equal("Notes.", task.Description);
    }

    [Fact]
    public void Space_after_undo_redo_round_trips_through_a_null_baseline()
    {
        var project = NewProject();
        var task = project.AddTask("A", Duration.Parse("1d"));
        project.Recalculate();

        var setCommand = new SetTaskCommand { Uid = task.UniqueId, SpaceAfter = 5 };
        var (_, undoSet) = CommandInverter.ApplyWithInverse(project, setCommand);
        Assert.Equal(5, task.Formatting?.SpaceAfter);

        CommandExecutor.Apply(project, undoSet!); // undo: back to no formatting
        Assert.Null(task.Formatting);

        CommandExecutor.Apply(project, setCommand); // redo
        Assert.Equal(5, task.Formatting?.SpaceAfter);
    }

    [Fact]
    public void Add_task_inverse_removes_it_and_removal_is_a_barrier()
    {
        var project = NewProject();
        var (uid, inverse) = CommandInverter.ApplyWithInverse(project, new AddTaskCommand { Name = "New" });
        Assert.NotNull(uid);
        Assert.IsType<RemoveTaskCommand>(inverse);
        CommandExecutor.Apply(project, inverse!);
        Assert.Empty(project.Tasks);

        project.AddTask("Doomed");
        var (_, removalInverse) = CommandInverter.ApplyWithInverse(
            project, new RemoveTaskCommand { Uid = project.Tasks[0].UniqueId });
        Assert.Null(removalInverse); // destructive → undo barrier
    }

    [Fact]
    public void Link_and_unlink_invert_with_type_and_lag()
    {
        var project = NewProject();
        var a = project.AddTask("A", Duration.Parse("1d"));
        var b = project.AddTask("B", Duration.Parse("1d"));
        project.Link(a, b, DependencyType.StartToStart, Lag.Restore(LagKind.Percent, 50m));

        RoundTrip(project, new UnlinkCommand { PredecessorUid = a.UniqueId, SuccessorUid = b.UniqueId });
        var restored = b.Predecessors.Single();
        Assert.Equal(DependencyType.StartToStart, restored.Type);
        Assert.Equal(50m, restored.Lag.Value);

        var c = project.AddTask("C", Duration.Parse("1d"));
        RoundTrip(project, new LinkCommand { PredecessorUid = b.UniqueId, SuccessorUid = c.UniqueId });
        Assert.Empty(c.Predecessors); // the link inverse removed it again
    }

    [Fact]
    public void Outline_moves_invert_to_the_original_position()
    {
        var project = NewProject();
        project.AddTask("Phase");
        var step = project.AddTask("Step");
        RoundTrip(project, new IndentTaskCommand { Uid = step.UniqueId });
        Assert.Equal(0, step.OutlineLevel);
        Assert.Equal(2, step.RowNumber);
    }

    [Fact]
    public void Unassign_inverse_restores_the_assignment_shape()
    {
        var project = NewProject();
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var task = project.AddTask("T", Duration.Parse("4d"));
        var assignment = project.Assign(task, dev, units: 0.5m);
        var originalWork = assignment.WorkMinutes;

        RoundTrip(project, new UnassignCommand { Uid = task.UniqueId, Resource = "Dev" });
        var restored = task.Assignments.Single();
        Assert.Equal(0.5m, restored.Units);
        Assert.Equal(originalWork, restored.WorkMinutes);
    }

    [Fact]
    public void Set_assignment_inverse_restores_actuals_and_consumption()
    {
        var project = NewProject();
        var fuel = project.AddResource("Fuel", ResourceType.Material);
        var dev = project.AddResource("Dev");
        var task = project.AddTask("T", Duration.Parse("2d"));
        project.Assign(task, fuel, units: 10m);
        var work = project.Assign(task, dev);
        work.ActualWorkMinutes = 240m;

        RoundTrip(project, new SetAssignmentCommand { Uid = task.UniqueId, Resource = "Fuel", UnitsPer = RateUnit.Day });
        Assert.Null(task.Assignments[0].MaterialRateUnit);

        RoundTrip(project, new SetAssignmentCommand
        {
            Uid = task.UniqueId,
            Resource = "Dev",
            ActualWork = "8h",
            ActualCost = 99m,
        });
        Assert.Equal(240m, work.ActualWorkMinutes);
        Assert.Null(work.ActualCost);
    }

    [Fact]
    public void Set_project_inverse_restores_settings()
    {
        var project = NewProject();
        RoundTrip(project, new SetProjectCommand { Name = "Renamed", MinutesPerDay = 420, StatusDate = At("2026-02-01 08:00") });
        Assert.Equal("Test", project.Name);
        Assert.Equal(480, project.TimeSettings.MinutesPerDay);
        Assert.Null(project.StatusDate);
    }
}
