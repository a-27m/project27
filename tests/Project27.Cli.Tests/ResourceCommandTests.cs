using Xunit;
using System.Text.Json;

namespace Project27.Cli.Tests;

public sealed class ResourceCommandTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _file;

    public ResourceCommandTests()
    {
        _file = _dir.File("plan.p27");
        Cli.Ok("init", "Plan", "--start", "2026-01-05", "--file", _file);
    }

    public void Dispose() => _dir.Dispose();

    private JsonElement Resource(string reference)
        => Cli.Ok("resource", "show", reference, "--json", "--file", _file).Json();

    [Fact]
    public void Add_work_resource_with_rate_and_max_units()
    {
        Cli.Ok(
            "resource", "add", "Dev", "--file", _file,
            "--rate", "50/h", "--overtime-rate", "75/h", "--cost-per-use", "10",
            "--max-units", "200%", "--initials", "DV", "--group", "Eng");
        var dev = Resource("Dev");
        Assert.Equal("work", dev.GetProperty("type").GetString());
        Assert.Equal("200%", dev.GetProperty("maxUnits").GetString());
        Assert.Equal("50/h", dev.GetProperty("rate").GetString());
        var tableA = dev.GetProperty("rateTables")[0];
        Assert.Equal("75/h", tableA.GetProperty("entries")[0].GetProperty("overtimeRate").GetString());
        Assert.Equal(10m, tableA.GetProperty("entries")[0].GetProperty("costPerUse").GetDecimal());
    }

    [Fact]
    public void Material_resources_take_per_unit_rates_only()
    {
        Cli.Ok("resource", "add", "Cement", "--type", "material", "--rate", "12.5", "--material-label", "t", "--file", _file);
        var cement = Resource("Cement");
        Assert.Equal("12.5", cement.GetProperty("rate").GetString());
        Assert.Equal("t", cement.GetProperty("materialLabel").GetString());

        Assert.Contains(
            "per unit",
            Cli.Fail("resource", "set", "Cement", "--rate", "10/h", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_names_are_rejected_case_insensitively()
    {
        Cli.Ok("resource", "add", "Dev", "--file", _file);
        Assert.Contains(
            "already exists",
            Cli.Fail("resource", "add", "dev", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Set_rate_adds_an_effective_dated_entry()
    {
        Cli.Ok("resource", "add", "Dev", "--rate", "50/h", "--file", _file);
        Cli.Ok("resource", "set-rate", "Dev", "--from", "2026-06-01", "--rate", "60/h", "--file", _file);
        var entries = Resource("Dev").GetProperty("rateTables")[0].GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, entries[0].GetProperty("from").ValueKind);
        Assert.Equal("60/h", entries[1].GetProperty("standardRate").GetString());

        Cli.Ok("resource", "remove-rate", "Dev", "--from", "2026-06-01", "--file", _file);
        Assert.Equal(1, Resource("Dev").GetProperty("rateTables")[0].GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void Cost_resources_have_no_rates()
    {
        Cli.Ok("resource", "add", "Travel", "--type", "cost", "--file", _file);
        Assert.Contains(
            "no rates",
            Cli.Fail("resource", "set", "Travel", "--rate", "5", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void List_shows_all_types()
    {
        Cli.Ok("resource", "add", "Dev", "--rate", "50/h", "--file", _file);
        Cli.Ok("resource", "add", "Cement", "--type", "material", "--rate", "12.5", "--file", _file);
        Cli.Ok("resource", "add", "Travel", "--type", "cost", "--file", _file);
        var list = Cli.Ok("resource", "list", "--json", "--file", _file).Json();
        Assert.Equal(3, list.GetArrayLength());

        var text = Cli.Ok("resource", "list", "--file", _file).Stdout;
        Assert.Contains("material", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_drops_assignments()
    {
        Cli.Ok("resource", "add", "Dev", "--file", _file);
        Cli.Ok("task", "add", "Build", "-d", "2d", "--file", _file);
        Cli.Ok("assign", "add", "1", "Dev", "--file", _file);
        Cli.Ok("resource", "remove", "Dev", "--file", _file);
        Assert.Contains("no assignments", Cli.Ok("assign", "list", "--file", _file).Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Resource_calendar_must_exist()
    {
        Cli.Ok("resource", "add", "Ann", "--file", _file);
        Assert.Contains(
            "no calendar",
            Cli.Fail("resource", "set", "Ann", "--calendar", "Nope", "--file", _file).Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Uid_reference_works()
    {
        Cli.Ok("resource", "add", "Dev", "--file", _file);
        Assert.Equal("Dev", Resource("uid:1").GetProperty("name").GetString());
        Assert.Contains("no resource", Cli.Fail("resource", "show", "Nope", "--file", _file).Stderr, StringComparison.Ordinal);
    }
}
