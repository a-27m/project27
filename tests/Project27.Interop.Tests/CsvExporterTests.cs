using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Project27.Core.Views;
using Project27.Interop;
using Xunit;

namespace Project27.Interop.Tests;

public sealed class CsvExporterTests
{
    private static Project SampleProject()
    {
        var project = new Project("Test", DateTime.Parse("2026-01-05 08:00", CultureInfo.InvariantCulture));
        var design = project.AddTask("Design, then \"build\"", Duration.Parse("2d"));
        var build = project.AddTask("Build", Duration.Parse("3d"));
        project.Link(design, build);
        project.Recalculate();
        return project;
    }

    [Fact]
    public void Writes_rfc4180_with_quoting_and_crlf()
    {
        var project = SampleProject();
        var csv = CsvExporter.Write(project, new ViewDefinition(["id", "name", "duration", "predecessors"]));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("ID,Name,Duration,Predecessors", lines[0]);
        Assert.Equal("1,\"Design, then \"\"build\"\"\",2d,", lines[1]);
        Assert.Equal("2,Build,3d,1", lines[2]);
        Assert.EndsWith("\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Grouped_views_emit_a_group_column()
    {
        var project = SampleProject();
        var csv = CsvExporter.Write(project, new ViewDefinition(["name"], GroupBy: "critical"));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Group,Name", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("Critical: ", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Filters_and_sorts_apply()
    {
        var project = SampleProject();
        var csv = CsvExporter.Write(project, new ViewDefinition(
            ["name", "duration"],
            FilterParser.Parse(project, "duration >= 3d")));
        Assert.DoesNotContain("Design", csv, StringComparison.Ordinal);
        Assert.Contains("Build", csv, StringComparison.Ordinal);
    }
}
