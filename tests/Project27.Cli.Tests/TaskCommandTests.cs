using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class TaskCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public TaskCommandTests()
    {
        _dir = new TempDir();
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    private JsonElement TaskJson(string reference)
        => Cli.Ok("task", "show", reference, "--json", "--file", _file).Json();

    [Fact]
    public void Golden_chain_schedules_like_the_engine()
    {
        // Mon 2026-01-05 start; Design 2d, Build 3d after FS+1d lag, Ship milestone.
        Cli.Ok("task", "add", "Design", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", _file);
        Cli.Ok("task", "add", "Ship", "--milestone", "--file", _file);
        Cli.Ok("link", "add", "1", "2", "--lag", "1d", "--file", _file);
        Cli.Ok("link", "add", "2", "3", "--file", _file);

        var build = TaskJson("2");
        Assert.Equal("2026-01-08T08:00:00", build.GetProperty("start").GetString());
        Assert.Equal("2026-01-12T17:00:00", build.GetProperty("finish").GetString());
        Assert.True(build.GetProperty("critical").GetBoolean());
        Assert.Equal("0d", build.GetProperty("totalSlack").GetString());

        var ship = TaskJson("3");
        Assert.True(ship.GetProperty("milestone").GetBoolean());
        Assert.Equal("2026-01-12T17:00:00", ship.GetProperty("finish").GetString());

        var list = Cli.Ok("task", "list", "--file", _file).Stdout;
        Assert.Contains("1FS+1d", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Indent_makes_a_summary_with_rolled_up_duration()
    {
        Cli.Ok("task", "add", "Phase", "--file", _file);
        Cli.Ok("task", "add", "Step A", "-d", "2d", "--file", _file);
        Cli.Ok("task", "add", "Step B", "-d", "3d", "--file", _file);
        Cli.Ok("task", "indent", "2", "3", "--file", _file);

        var phase = TaskJson("1");
        Assert.True(phase.GetProperty("summary").GetBoolean());
        Assert.Equal("3d", phase.GetProperty("duration").GetString()); // parallel children
        Assert.Equal(1, TaskJson("2").GetProperty("outlineLevel").GetInt32());
        Assert.Equal("1.2", TaskJson("3").GetProperty("wbs").GetString());

        Cli.Ok("task", "outdent", "3", "--file", _file);
        Assert.Equal(0, TaskJson("3").GetProperty("outlineLevel").GetInt32());
    }

    [Fact]
    public void Set_changes_fields_and_none_clears_them()
    {
        Cli.Ok("task", "add", "Work", "-d", "2d", "--file", _file);
        Cli.Ok(
            "task", "set", "1", "--file", _file,
            "--name", "Work!", "--priority", "700", "--deadline", "2026-01-30",
            "--constraint", "snet", "--constraint-date", "2026-01-07", "--mode", "manual", "--active", "false");

        var task = TaskJson("1");
        Assert.Equal("Work!", task.GetProperty("name").GetString());
        Assert.Equal(700, task.GetProperty("priority").GetInt32());
        Assert.Equal("2026-01-30T17:00:00", task.GetProperty("deadline").GetString()); // date-only -> default end time
        Assert.Equal("startNoEarlierThan", task.GetProperty("constraint").GetString());
        Assert.Equal("2026-01-07T08:00:00", task.GetProperty("constraintDate").GetString());
        Assert.Equal("manual", task.GetProperty("mode").GetString());
        Assert.False(task.GetProperty("active").GetBoolean());

        Cli.Ok("task", "set", "1", "--deadline", "none", "--file", _file);
        Assert.Equal(JsonValueKind.Null, TaskJson("1").GetProperty("deadline").ValueKind);
    }

    [Fact]
    public void Set_description_and_none_clears_it()
    {
        Cli.Ok("task", "add", "Work", "-d", "2d", "--file", _file);
        Assert.Equal(JsonValueKind.Null, TaskJson("1").GetProperty("description").ValueKind);

        Cli.Ok("task", "set", "1", "--description", "Needs review before ship.", "--file", _file);
        Assert.Equal("Needs review before ship.", TaskJson("1").GetProperty("description").GetString());
        Assert.Contains("Needs review before ship.", Cli.Ok("task", "show", "1", "--file", _file).Stdout, StringComparison.Ordinal);

        Cli.Ok("task", "set", "1", "--description", "none", "--file", _file);
        Assert.Equal(JsonValueKind.Null, TaskJson("1").GetProperty("description").ValueKind);
    }

    [Fact]
    public void Constraint_requiring_date_fails_without_one()
    {
        Cli.Ok("task", "add", "Work", "--file", _file);
        Assert.Contains(
            "--constraint-date",
            Cli.Fail("task", "set", "1", "--constraint", "mso", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_duration_is_not_editable()
    {
        Cli.Ok("task", "add", "Phase", "--file", _file);
        Cli.Ok("task", "add", "Step", "--file", _file);
        Cli.Ok("task", "indent", "2", "--file", _file);
        Assert.Contains(
            "rolled up",
            Cli.Fail("task", "set", "1", "--duration", "5d", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Split_and_unsplit_roundtrip()
    {
        Cli.Ok("task", "add", "Work", "-d", "4d", "--file", _file);
        Cli.Ok("task", "split", "1", "--at", "1d", "--gap", "1d", "--file", _file);
        Assert.Equal(2, TaskJson("1").GetProperty("segments").GetArrayLength());

        Cli.Ok("task", "unsplit", "1", "--file", _file);
        Assert.Equal(1, TaskJson("1").GetProperty("segments").GetArrayLength());
    }

    [Fact]
    public void Recurring_task_creates_one_child_per_occurrence()
    {
        Cli.Ok(
            "task", "add-recurring", "Standup", "--file", _file,
            "-d", "30m", "--recur", "weekly:mon,fri", "--from", "2026-01-05", "--times", "4");
        var summary = TaskJson("1");
        Assert.True(summary.GetProperty("recurring").GetBoolean());
        Assert.True(summary.GetProperty("summary").GetBoolean());

        var list = Cli.Ok("task", "list", "--json", "--file", _file).Json();
        Assert.Equal(5, list.GetArrayLength()); // summary + 4 occurrences
    }

    [Fact]
    public void Uid_references_survive_renumbering()
    {
        Cli.Ok("task", "add", "First", "--file", _file);
        Cli.Ok("task", "add", "Second", "--file", _file);
        Cli.Ok("task", "remove", "1", "--file", _file);

        var second = TaskJson("uid:2");
        Assert.Equal("Second", second.GetProperty("name").GetString());
        Assert.Equal(1, second.GetProperty("id").GetInt32()); // renumbered row
    }

    [Fact]
    public void Move_reparents_a_subtree()
    {
        Cli.Ok("task", "add", "Phase", "--file", _file);
        Cli.Ok("task", "add", "Child", "--file", _file);
        Cli.Ok("task", "indent", "2", "--file", _file);
        Cli.Ok("task", "add", "Loose", "--file", _file);
        Cli.Ok("task", "move", "3", "--parent", "1", "--file", _file);

        var moved = TaskJson("uid:3");
        Assert.Equal(1, moved.GetProperty("outlineLevel").GetInt32());
        Assert.Equal("1.2", moved.GetProperty("wbs").GetString());

        Cli.Ok("task", "move", "3", "--parent", "top", "--at", "0", "--file", _file);
        Assert.Equal(1, TaskJson("uid:3").GetProperty("id").GetInt32());
    }

    [Fact]
    public void Unknown_reference_is_a_clean_error()
    {
        Assert.Contains("no task with id 9", Cli.Fail("task", "show", "9", "--file", _file).Stderr, StringComparison.Ordinal);
        Assert.Contains("invalid task reference", Cli.Fail("task", "show", "x", "--file", _file).Stderr, StringComparison.Ordinal);
    }
}
