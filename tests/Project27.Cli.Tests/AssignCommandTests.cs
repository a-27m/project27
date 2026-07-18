using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class AssignCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public AssignCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
        Cli.Ok("resource", "add", "Dev", "--rate", "50/h", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "4d", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    private JsonElement Task1()
        => Cli.Ok("task", "show", "1", "--json", "--file", _file).Json();

    [Fact]
    public void Assign_defaults_to_full_units_and_duration_work()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        var task = Task1();
        Assert.Equal("32h", task.GetProperty("work").GetString());
        Assert.Equal(1600m, task.GetProperty("cost").GetDecimal());
        var assignment = task.GetProperty("assignments")[0];
        Assert.Equal("100%", assignment.GetProperty("units").GetString());
        Assert.Equal("2026-01-08T17:00:00", assignment.GetProperty("finish").GetString());
    }

    [Fact]
    public void Fixed_units_work_edit_stretches_duration()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("assign", "set", "1", "Dev", "--work", "80h", "--file", _file);
        var task = Task1();
        Assert.Equal("10d", task.GetProperty("duration").GetString());
        Assert.Equal("2026-01-16T17:00:00", task.GetProperty("finish").GetString());
    }

    [Fact]
    public void Effort_driven_add_splits_work()
    {
        Cli.Ok("resource", "add", "Helper", "--rate", "40/h", "--file", _file);
        Cli.Ok("task", "set", "1", "--effort-driven", "true", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("assign", "add", "1", "Helper", "--file", _file);
        var task = Task1();
        Assert.Equal("2d", task.GetProperty("duration").GetString());
        Assert.Equal("32h", task.GetProperty("work").GetString());
        Assert.All(
            task.GetProperty("assignments").EnumerateArray(),
            a => Assert.Equal("16h", a.GetProperty("work").GetString()));
    }

    [Fact]
    public void Fixed_work_type_via_cli_rejects_effort_driven_off()
    {
        Cli.Ok("task", "set", "1", "--type", "fixed-work", "--file", _file);
        Assert.Contains(
            "effort-driven",
            Cli.Fail("task", "set", "1", "--effort-driven", "false", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Contour_stretches_duration_and_roundtrips()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--contour", "back-loaded", "--file", _file);
        var task = Task1();
        Assert.Equal("backLoaded", task.GetProperty("assignments")[0].GetProperty("contour").GetString());
        // 32h at avg 60% utilization -> 6.67d
        Assert.Equal("6.67d", task.GetProperty("duration").GetString());
    }

    [Fact]
    public void Cost_resource_assignment_carries_its_amount()
    {
        Cli.Ok("resource", "add", "Travel", "--type", "cost", "--file", _file);
        Cli.Ok("assign", "add", "1", "Travel", "--cost", "300", "--file", _file);
        Assert.Equal(300m, Task1().GetProperty("cost").GetDecimal());

        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Assert.Contains(
            "computed from rates",
            Cli.Fail("assign", "set", "1", "Dev", "--cost", "5", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_cost_rolls_up_with_assignments()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("task", "set", "1", "--fixed-cost", "100", "--accrual", "end", "--file", _file);
        var task = Task1();
        Assert.Equal(1700m, task.GetProperty("cost").GetDecimal());
        Assert.Equal("end", task.GetProperty("fixedCostAccrual").GetString());

        var projectJson = Cli.Ok("project", "show", "--json", "--file", _file).Json();
        Assert.Equal(1700m, projectJson.GetProperty("cost").GetDecimal());
        Assert.Equal("32h", projectJson.GetProperty("work").GetString());
    }

    [Fact]
    public void Duplicate_assignment_fails()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Assert.Contains(
            "already assigned",
            Cli.Fail("assign", "add", "1", "Dev", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_reports_missing_assignment()
    {
        Assert.Contains(
            "not assigned",
            Cli.Fail("assign", "remove", "1", "Dev", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void List_scopes_to_a_task()
    {
        Cli.Ok("task", "add", "Test", "-d", "1d", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("resource", "add", "QA", "--file", _file);
        Cli.Ok("assign", "add", "2", "QA", "--file", _file);

        Assert.Equal(2, Cli.Ok("assign", "list", "--json", "--file", _file).Json().GetArrayLength());
        var scoped = Cli.Ok("assign", "list", "2", "--json", "--file", _file).Json();
        Assert.Equal(1, scoped.GetArrayLength());
        Assert.Equal("QA", scoped[0].GetProperty("resource").GetString());
    }

    [Fact]
    public void Resource_calendar_extends_the_finish_through_the_cli()
    {
        Cli.Ok("calendar", "add", "Mornings", "--base", "Standard", "--file", _file);
        foreach (var day in new[] { "mon", "tue", "wed", "thu", "fri" })
        {
            Cli.Ok("calendar", "set-day", "Mornings", day, "08:00-12:00", "--file", _file);
        }

        Cli.Ok("resource", "set", "Dev", "--calendar", "Mornings", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        // 32h at 4h per morning = 8 mornings: Mon 2026-01-05 .. Wed 2026-01-14 12:00.
        Assert.Equal("2026-01-14T12:00:00", Task1().GetProperty("finish").GetString());
    }

    [Fact]
    public void Variable_material_consumption_scales_with_duration()
    {
        Cli.Ok("resource", "add", "Fuel", "--type", "material", "--rate", "5", "--file", _file);
        Cli.Ok("assign", "add", "1", "Fuel", "--units", "10", "--per", "d", "--file", _file);
        var assignment = Task1().GetProperty("assignments").EnumerateArray()
            .Single(a => a.GetProperty("resource").GetString() == "Fuel");
        Assert.Equal("10/d", assignment.GetProperty("units").GetString());
        Assert.Equal(40m, assignment.GetProperty("quantity").GetDecimal()); // 10/day × 4d
        Assert.Equal(200m, assignment.GetProperty("cost").GetDecimal());    // 40 × 5

        Cli.Ok("assign", "set", "1", "Fuel", "--per", "none", "--file", _file);
        var fixedQuantity = Task1().GetProperty("assignments").EnumerateArray()
            .Single(a => a.GetProperty("resource").GetString() == "Fuel");
        Assert.Equal("10", fixedQuantity.GetProperty("units").GetString());
        Assert.Equal(50m, fixedQuantity.GetProperty("cost").GetDecimal());
    }

    [Fact]
    public void Explicit_actuals_override_the_derived_values()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("task", "set", "1", "--percent-complete", "50", "--file", _file);
        Cli.Ok("assign", "set", "1", "Dev", "--actual-work", "10h", "--actual-cost", "700", "--file", _file);

        var assignment = Task1().GetProperty("assignments")[0];
        Assert.Equal("10h", assignment.GetProperty("actualWork").GetString());
        Assert.Equal(700m, assignment.GetProperty("actualCost").GetDecimal());

        var view = Cli.Ok("view", "--fields", "name,actualWork,remainingWork,actualCost", "--json", "--file", _file).Json();
        var values = view.GetProperty("groups")[0].GetProperty("rows")[0].GetProperty("values");
        Assert.Equal(600m, values.GetProperty("actualWork").GetDecimal());       // minutes
        Assert.Equal(1320m, values.GetProperty("remainingWork").GetDecimal());   // 32h − 10h
        Assert.Equal(700m, values.GetProperty("actualCost").GetDecimal());

        Cli.Ok("assign", "set", "1", "Dev", "--actual-work", "none", "--actual-cost", "none", "--file", _file);
        var cleared = Task1().GetProperty("assignments")[0];
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("actualWork").ValueKind);
    }

    [Fact]
    public void Delay_shifts_the_assignment()
    {
        Cli.Ok("assign", "add", "1", "Dev", "--delay", "1d", "--file", _file);
        var assignment = Task1().GetProperty("assignments")[0];
        Assert.Equal("8h", assignment.GetProperty("delay").GetString());
        Assert.Equal("2026-01-06T08:00:00", assignment.GetProperty("start").GetString());
        Assert.Equal("2026-01-09T17:00:00", assignment.GetProperty("finish").GetString());
    }
}
