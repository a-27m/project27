using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Core.Persistence;
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
    public void Set_task_writes_and_clears_description()
    {
        var project = NewProject();
        var task = project.AddTask("T", Core.Time.Duration.Parse("1d"));

        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, Description = "Some notes." });
        Assert.Equal("Some notes.", task.Description);

        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, ClearDescription = true });
        Assert.Null(task.Description);
    }

    [Fact]
    public void Description_rejects_text_over_2000_characters()
    {
        var project = NewProject();
        var task = project.AddTask("T", Core.Time.Duration.Parse("1d"));

        task.Description = new string('x', 2000);
        Assert.Throws<ArgumentOutOfRangeException>(() => task.Description = new string('x', 2001));
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
    public void Blank_task_names_are_rejected()
    {
        var project = NewProject();
        Assert.Throws<ArgumentException>(() => project.AddTask("", Core.Time.Duration.Parse("1d")));
        Assert.Throws<ArgumentException>(() => project.AddTask("   ", Core.Time.Duration.Parse("1d")));

        CommandExecutor.Apply(project, new AddTaskCommand { Name = "Real" });
        Assert.Throws<CommandException>(() => CommandExecutor.Apply(
            project, new AddTaskCommand { Name = "" }));
        Assert.Throws<CommandException>(() => CommandExecutor.Apply(
            project, new SetTaskCommand { Uid = 1, Name = "" }));
    }

    [Fact]
    public void Space_after_is_cosmetic_and_collapses_to_null_at_zero()
    {
        var project = NewProject();
        var task = project.AddTask("A", Core.Time.Duration.Parse("1d"));

        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, SpaceAfter = 3 });
        Assert.Equal(3, task.Formatting?.SpaceAfter);

        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, SpaceAfter = 0 });
        Assert.Null(task.Formatting);
    }

    [Fact]
    public void Space_after_round_trips_through_the_document()
    {
        var project = NewProject();
        var task = project.AddTask("A", Core.Time.Duration.Parse("1d"));
        var untouched = project.AddTask("B", Core.Time.Duration.Parse("1d"));
        CommandExecutor.Apply(project, new SetTaskCommand { Uid = task.UniqueId, SpaceAfter = 2 });

        var restored = ProjectDocumentMapper.FromDocument(ProjectDocumentMapper.ToDocument(project));
        var restoredA = restored.Tasks.Single(t => t.Name == "A");
        var restoredB = restored.Tasks.Single(t => t.Name == "B");
        Assert.Equal(2, restoredA.Formatting?.SpaceAfter);
        Assert.Null(restoredB.Formatting);
        Assert.Equal(untouched.Formatting, restoredB.Formatting); // both null
    }

    [Fact]
    public void Full_surface_ops_build_a_staffed_tracked_project()
    {
        var project = NewProject();
        CommandExecutor.ApplyAll(project,
        [
            new AddCalendarCommand { Name = "Ops", BaseCalendar = "Standard" },
            new SetCalendarDayCommand { Calendar = "Ops", Day = DayOfWeek.Saturday, Intervals = [new CommandInterval("08:00", "12:00")] },
            new AddCalendarExceptionCommand { Calendar = "Standard", Name = "Holiday", From = new DateOnly(2026, 1, 6) },
            new AddResourceCommand { Name = "Dev", Rate = "50/h", MaxUnits = 2m, Group = "Eng" },
            new AddResourceCommand { Name = "Travel", Type = ResourceType.Cost },
            new SetResourceRateCommand { Resource = "Dev", Table = CostRateTableId.B, From = At("2026-06-01 00:00"), Rate = "80/h" },
            new AddTaskCommand { Name = "Build", Duration = "3d" },
            new AssignCommand { Uid = 1, Resource = "Dev" },
            new AssignCommand { Uid = 1, Resource = "Travel", Cost = 300m },
            new SetAssignmentCommand { Uid = 1, Resource = "Dev", Contour = WorkContour.Bell },
            new DefineCustomFieldCommand { Slot = "text1", Alias = "Phase" },
            new SetTaskCommand { Uid = 1, CustomValues = new Dictionary<string, string?> { ["Phase"] = "Rollout" } },
            new SetProjectCommand { StatusDate = At("2026-01-12 08:00"), MinutesPerDay = 420, DayStart = "07:00" },
        ]);
        project.Recalculate();

        var build = project.Tasks.Single();
        Assert.Equal("Ops", project.Calendars.Single(c => c.Name == "Ops").Name);
        Assert.Single(project.Calendar.Exceptions);
        Assert.Equal(2, project.Resources.Count);
        Assert.Equal(2, build.Assignments.Count);
        Assert.Equal(WorkContour.Bell, build.Assignments[0].Contour);
        Assert.Equal(80m, project.Resources[0].RateTable(CostRateTableId.B).RateAt(At("2026-07-01 00:00")).StandardRate.Amount);
        Assert.Equal("Rollout", build.GetCustomValue("text1"));
        Assert.Equal(420, project.TimeSettings.MinutesPerDay);
        Assert.Equal(new TimeOnly(7, 0), project.TimeSettings.DefaultStartTime);
        Assert.Equal(At("2026-01-12 08:00"), project.StatusDate);
        Assert.True(build.Cost > 300m);

        CommandExecutor.ApplyAll(project,
        [
            new UnassignCommand { Uid = 1, Resource = "Travel" },
            new RemoveResourceCommand { Resource = "Travel" },
            new RemoveCustomFieldCommand { Field = "Phase" },
            new RemoveCalendarCommand { Calendar = "Ops" },
        ]);
        Assert.Single(project.Resources);
        Assert.Single(build.Assignments);
        Assert.Empty(project.CustomFields);
    }

    [Fact]
    public void Recurring_and_reschedule_ops_work()
    {
        var project = NewProject();
        var uid = CommandExecutor.Apply(project, new AddRecurringTaskCommand
        {
            Name = "Standup",
            Duration = "30m",
            Recurrence = new CommandRecurrence { Kind = "weekly", Days = [DayOfWeek.Monday, DayOfWeek.Friday] },
            From = new DateOnly(2026, 1, 5),
            Times = 4,
        });
        project.Recalculate();
        var summary = project.Tasks.Single(t => t.UniqueId == uid);
        Assert.True(summary.IsRecurring);
        Assert.Equal(4, summary.Children.Count);

        var workUid = CommandExecutor.Apply(project, new AddTaskCommand { Name = "Work", Duration = "4d" })!.Value;
        CommandExecutor.ApplyAll(project,
        [
            new SetTaskCommand { Uid = workUid, PercentComplete = 25 },
            new RescheduleCommand { After = At("2026-01-08 08:00") },
        ]);
        var work = project.Tasks.Single(t => t.Name == "Work");
        Assert.True(work.IsSplit);
        Assert.Equal(At("2026-01-08 08:00"), work.Segments[1].Start);
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
