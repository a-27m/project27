using System.Globalization;
using Project27.Core;
using Project27.Core.Fields;
using Project27.Core.Time;
using Project27.Core.Views;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Field catalog and view engine (phase 9a).</summary>
public sealed class ViewTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project SampleProject()
    {
        var project = new Project("Test", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var phase = project.AddTask("Phase");
        var design = project.AddTask("Design", Dur("2d"), phase);
        var build = project.AddTask("Build", Dur("3d"), phase);
        var polish = project.AddTask("Polish", Dur("1d"));
        project.Link(design, build);
        project.Assign(build, dev);
        polish.PercentComplete = 50;
        project.Recalculate();
        return project;
    }

    [Fact]
    public void Catalog_covers_the_core_field_families()
    {
        var project = SampleProject();
        var build = project.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(1440m, FieldCatalog.Resolve(project, "duration").Accessor(build));
        Assert.Equal(At("2026-01-07 08:00"), FieldCatalog.Resolve(project, "start").Accessor(build));
        Assert.Equal("2", FieldCatalog.Resolve(project, "predecessors").Accessor(build));
        Assert.Equal("Dev", FieldCatalog.Resolve(project, "resourceNames").Accessor(build));
        Assert.Equal(1200m, FieldCatalog.Resolve(project, "cost").Accessor(build)); // 24h × 50
        Assert.Equal(true, FieldCatalog.Resolve(project, "critical").Accessor(build));
        Assert.Throws<KeyNotFoundException>(() => FieldCatalog.Resolve(project, "nonsense"));
    }

    [Fact]
    public void Variance_fields_compare_against_baseline_zero()
    {
        var project = SampleProject();
        project.SetBaseline();
        var build = project.Tasks.Single(t => t.Name == "Build");
        build.Duration = Dur("5d");
        project.Recalculate();

        Assert.Equal(960m, FieldCatalog.Resolve(project, "durationVariance").Accessor(build)); // +2d
        Assert.Equal(960m, FieldCatalog.Resolve(project, "finishVariance").Accessor(build));
        Assert.Equal(0m, FieldCatalog.Resolve(project, "startVariance").Accessor(build));
    }

    [Fact]
    public void Formatting_follows_the_field_kind()
    {
        var settings = new TimeSettings();
        Assert.Equal("2.5d", FieldCatalog.Format(FieldKind.Duration, 1200m, settings));
        Assert.Equal("16h", FieldCatalog.Format(FieldKind.Work, 960m, settings));
        Assert.Equal("50%", FieldCatalog.Format(FieldKind.Percent, 50, settings));
        Assert.Equal("yes", FieldCatalog.Format(FieldKind.Flag, true, settings));
        Assert.Equal("2026-01-05 08:00", FieldCatalog.Format(FieldKind.Date, At("2026-01-05 08:00"), settings));
        Assert.Equal("", FieldCatalog.Format(FieldKind.Cost, null, settings));
        Assert.Equal("0.33", FieldCatalog.Format(FieldKind.Number, 1m / 3m, settings));
    }

    [Fact]
    public void Filters_parse_and_match()
    {
        var project = SampleProject();
        var filter = FilterParser.Parse(project, "critical = true and (duration > 2d or name ~ \"des\")");
        var names = project.Tasks.Where(filter.Matches).Select(t => t.Name).ToList();
        Assert.Contains("Build", names);   // critical, 3d
        Assert.Contains("Design", names);  // critical, name match
        Assert.DoesNotContain("Polish", names);

        var reopened = FilterParser.Parse(project, "not percentComplete = 0");
        Assert.Equal(["Polish"], project.Tasks.Where(t => !t.IsSummary && reopened.Matches(t)).Select(t => t.Name));

        Assert.Throws<FormatException>(() => FilterParser.Parse(project, "critical ="));
        Assert.Throws<KeyNotFoundException>(() => FilterParser.Parse(project, "bogus = 1"));
        Assert.Throws<FormatException>(() => FilterParser.Parse(project, "duration > banana"));
    }

    [Fact]
    public void Unsorted_views_keep_outline_order_with_summaries()
    {
        var project = SampleProject();
        var result = TaskView.Evaluate(project, new ViewDefinition(TaskView.Tables["entry"]));
        var rows = result.Groups.Single().Rows;
        Assert.Equal(["Phase", "Design", "Build", "Polish"], rows.Select(r => r.Task.Name));
        Assert.Equal("2d", rows[1].Cells.Single(c => c.Field == "duration").Text);
    }

    [Fact]
    public void Sorting_flattens_to_leaves_and_orders_by_kind()
    {
        var project = SampleProject();
        var result = TaskView.Evaluate(project, new ViewDefinition(
            ["id", "name", "duration"],
            Sorts: TaskView.ParseSorts("duration desc, name")));
        var rows = result.Groups.Single().Rows;
        Assert.Equal(["Build", "Design", "Polish"], rows.Select(r => r.Task.Name)); // no summary rows
    }

    [Fact]
    public void Grouping_buckets_by_formatted_value()
    {
        var project = SampleProject();
        var result = TaskView.Evaluate(project, new ViewDefinition(
            ["id", "name"],
            GroupBy: "critical"));
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal("Critical: no", result.Groups[0].Heading);
        Assert.Equal(["Polish"], result.Groups[0].Rows.Select(r => r.Task.Name));
        Assert.Equal(["Design", "Build"], result.Groups[1].Rows.Select(r => r.Task.Name));
    }

    [Fact]
    public void Filter_applies_before_grouping_and_sorting()
    {
        var project = SampleProject();
        var result = TaskView.Evaluate(project, new ViewDefinition(
            ["id", "name"],
            FilterParser.Parse(project, "duration >= 2d"),
            TaskView.ParseSorts("name"),
            GroupBy: null));
        Assert.Equal(["Build", "Design"], result.Groups.Single().Rows.Select(r => r.Task.Name));
    }

    [Fact]
    public void Every_builtin_table_resolves()
    {
        var project = SampleProject();
        project.SetBaseline();
        project.Recalculate();
        foreach (var table in TaskView.Tables)
        {
            var result = TaskView.Evaluate(project, new ViewDefinition(table.Value));
            Assert.Equal(table.Value.Count, result.Fields.Count);
        }
    }
}
