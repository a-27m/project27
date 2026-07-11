using System.Globalization;
using Project27.Core;
using Project27.Core.Reports;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>The HTML report set (phase 11).</summary>
public sealed class ReportTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project SampleProject()
    {
        var project = new Project("Alpha <One>", At("2026-01-05 08:00"));
        var dev = project.AddResource("Dev");
        dev.RateTable(CostRateTableId.A).SetRate(DateTime.MinValue, new Rate(50m, RateUnit.Hour));
        var design = project.AddTask("Design & Split", Dur("2d"));
        var build = project.AddTask("Build", Dur("3d"));
        var ship = project.AddMilestone("Ship");
        project.Link(design, build);
        project.Link(build, ship);
        project.Assign(build, dev);
        project.Recalculate();
        return project;
    }

    [Fact]
    public void Every_report_renders_self_contained_html()
    {
        var project = SampleProject();
        project.SetBaseline();
        project.Recalculate();
        foreach (var (name, title) in ReportBuilder.Available)
        {
            var html = ReportBuilder.Render(project, name);
            Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
            Assert.Contains(title, html, StringComparison.Ordinal);
            Assert.DoesNotContain("src=", html, StringComparison.Ordinal); // no external assets
            Assert.DoesNotContain("href=", html, StringComparison.Ordinal);
        }

        Assert.Throws<KeyNotFoundException>(() => ReportBuilder.Render(project, "bogus"));
    }

    [Fact]
    public void Overview_escapes_html_and_lists_milestones_and_critical_path()
    {
        var html = ReportBuilder.Render(SampleProject(), "overview");
        Assert.Contains("Alpha &lt;One&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Design &amp; Split", html, StringComparison.Ordinal);
        Assert.Contains("Milestones", html, StringComparison.Ordinal);
        Assert.Contains("Ship", html, StringComparison.Ordinal);
        Assert.Contains("Critical path", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Late_report_guides_when_no_baseline_and_lists_slippage_when_there_is()
    {
        var project = SampleProject();
        Assert.Contains("No baseline captured", ReportBuilder.Render(project, "late"), StringComparison.Ordinal);

        project.SetBaseline();
        var build = project.Tasks.Single(t => t.Name == "Build");
        build.Duration = Dur("5d"); // slips the finish
        project.Recalculate();
        var html = ReportBuilder.Render(project, "late");
        Assert.Contains("Build", html, StringComparison.Ordinal);
        Assert.Contains("2d", html, StringComparison.Ordinal); // finish variance
    }

    [Fact]
    public void Costs_report_draws_the_bar_chart()
    {
        var html = ReportBuilder.Render(SampleProject(), "costs");
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("1200", html, StringComparison.Ordinal); // Build: 24h × 50
        Assert.Contains("Most expensive tasks", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Resources_report_flags_overallocation()
    {
        var project = SampleProject();
        var dev = project.Resources.Single();
        var parallel = project.AddTask("Parallel", Dur("3d"));
        project.Assign(parallel, dev); // same days as Build
        project.Recalculate();

        var html = ReportBuilder.Render(project, "resources");
        Assert.Contains("class=\"bad\"", html, StringComparison.Ordinal);
        Assert.Contains("Dev", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Upcoming_uses_the_status_date_window()
    {
        var project = SampleProject();
        project.StatusDate = At("2026-01-05 08:00");
        var html = ReportBuilder.Render(project, "upcoming");
        Assert.Contains("Build", html, StringComparison.Ordinal);

        project.StatusDate = At("2026-03-01 08:00"); // far past everything
        Assert.Contains("Nothing starts or finishes", ReportBuilder.Render(project, "upcoming"), StringComparison.Ordinal);
    }
}
