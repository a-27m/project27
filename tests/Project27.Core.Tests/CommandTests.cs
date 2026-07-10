using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Core;
using Project27.Core.Commands;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Command layer: execution semantics and JSON polymorphism.</summary>
public sealed class CommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    [Fact]
    public void Add_link_and_set_commands_build_a_schedule()
    {
        var project = NewProject();
        var uids = CommandExecutor.ApplyAll(project,
        [
            new AddTaskCommand { Name = "Design", Duration = "2d" },
            new AddTaskCommand { Name = "Build", Duration = "3d" },
            new LinkCommand { PredecessorUid = 1, SuccessorUid = 2 },
            new SetTaskCommand { Uid = 2, Priority = 700 },
        ]);
        project.Recalculate();

        Assert.Equal([1, 2, null, null], uids);
        var build = project.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(At("2026-01-07 08:00"), build.Start);
        Assert.Equal(700, build.Priority);
    }

    [Fact]
    public void Commands_round_trip_through_polymorphic_json()
    {
        var batch = """
            [
              {"op": "addTask", "name": "Design", "duration": "2d"},
              {"op": "addTask", "name": "Ship", "milestone": true},
              {"op": "link", "predecessorUid": 1, "successorUid": 2, "type": "finishToStart",
               "lag": {"kind": "working", "value": 480}},
              {"op": "setTask", "uid": 1, "deadline": "2026-01-30T17:00:00"},
              {"op": "setProject", "name": "Renamed"}
            ]
            """;
        var commands = JsonSerializer.Deserialize<List<ProjectCommand>>(batch, JsonOptions)!;
        var project = NewProject();
        CommandExecutor.ApplyAll(project, commands);
        project.Recalculate();

        Assert.Equal("Renamed", project.Name);
        var ship = project.Tasks.Single(t => t.Name == "Ship");
        Assert.True(ship.IsMilestone);
        Assert.Equal(At("2026-01-07 17:00"), ship.Finish); // 2d + 1d lag
        Assert.Equal(At("2026-01-30 17:00"), project.Tasks[0].Deadline);

        var serialized = JsonSerializer.Serialize(commands, JsonOptions);
        Assert.Contains("\"op\":", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_flags_reset_optional_fields()
    {
        var project = NewProject();
        var task = project.AddTask("T", Core.Time.Duration.Parse("1d"));
        task.Deadline = At("2026-02-01 17:00");
        task.CustomWbs = "X.1";

        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, ClearDeadline = true, ClearWbs = true });
        Assert.Null(task.Deadline);
        Assert.Null(task.CustomWbs);
    }

    [Fact]
    public void Set_link_replaces_type_and_lag()
    {
        var project = NewProject();
        var a = project.AddTask("A", Core.Time.Duration.Parse("1d"));
        var b = project.AddTask("B", Core.Time.Duration.Parse("1d"));
        project.Link(a, b);

        CommandExecutor.Apply(project, new SetLinkCommand
        {
            PredecessorUid = a.UniqueId,
            SuccessorUid = b.UniqueId,
            Type = DependencyType.StartToStart,
            Lag = new CommandLag(LagKind.Percent, 50m),
        });

        var link = b.Predecessors.Single();
        Assert.Equal(DependencyType.StartToStart, link.Type);
        Assert.Equal(LagKind.Percent, link.Lag.Kind);
        Assert.Equal(50m, link.Lag.Value);
    }

    [Fact]
    public void Outline_commands_move_subtrees()
    {
        var project = NewProject();
        CommandExecutor.ApplyAll(project,
        [
            new AddTaskCommand { Name = "Phase" },
            new AddTaskCommand { Name = "Step" },
            new IndentTaskCommand { Uid = 2 },
            new AddTaskCommand { Name = "Loose" },
            new MoveTaskCommand { Uid = 3, ParentUid = 1, At = 1 },
        ]);

        var loose = project.Tasks.Single(t => t.Name == "Loose");
        Assert.Equal(1, loose.OutlineLevel);
        Assert.Equal("1.2", loose.Wbs);

        CommandExecutor.Apply(project, new OutdentTaskCommand { Uid = 3 });
        Assert.Equal(0, loose.OutlineLevel);
    }

    [Fact]
    public void Engine_violations_surface_as_command_exceptions()
    {
        var project = NewProject();
        CommandExecutor.ApplyAll(project,
        [
            new AddTaskCommand { Name = "A", Duration = "1d" },
            new AddTaskCommand { Name = "B", Duration = "1d" },
            new LinkCommand { PredecessorUid = 1, SuccessorUid = 2 },
        ]);

        Assert.Throws<CommandException>(() => CommandExecutor.Apply(
            project, new LinkCommand { PredecessorUid = 2, SuccessorUid = 1 })); // cycle
        Assert.Throws<CommandException>(() => CommandExecutor.Apply(
            project, new SetTaskCommand { Uid = 99, Name = "X" })); // unknown uid
        Assert.Throws<CommandException>(() => CommandExecutor.Apply(
            project, new AddTaskCommand { Name = "C", Duration = "banana" })); // bad duration
    }

    [Fact]
    public void Remove_and_split_commands_work()
    {
        var project = NewProject();
        CommandExecutor.ApplyAll(project,
        [
            new AddTaskCommand { Name = "Work", Duration = "4d" },
            new SplitTaskCommand { Uid = 1, At = "1d", Gap = "1d" },
        ]);
        project.Recalculate();
        Assert.Equal(2, project.Tasks[0].Segments.Count);

        CommandExecutor.Apply(project, new UnsplitTaskCommand { Uid = 1 });
        CommandExecutor.Apply(project, new RemoveTaskCommand { Uid = 1 });
        Assert.Empty(project.Tasks);
    }
}
