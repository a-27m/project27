using System.Globalization;
using System.Net;
using System.Text;
using Project27.Core.Fields;
using Project27.Core.Scheduling;
using Project27.Core.Time;

namespace Project27.Core.Reports;

/// <summary>
/// The built-in report set (docs/spec/11-reports.md): self-contained HTML with
/// inline CSS/SVG, print-friendly. Reports only re-present existing projections;
/// recalculate before rendering.
/// </summary>
public static class ReportBuilder
{
    /// <summary>Report names and titles, in menu order.</summary>
    public static IReadOnlyList<(string Name, string Title)> Available { get; } =
    [
        ("overview", "Project overview"),
        ("critical", "Critical tasks"),
        ("late", "Late tasks"),
        ("resources", "Resource overview"),
        ("costs", "Cost overview"),
        ("upcoming", "Upcoming tasks"),
    ];

    public static string Render(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var body = name.Trim().ToUpperInvariant() switch
        {
            "OVERVIEW" => Overview(project),
            "CRITICAL" => Critical(project),
            "LATE" => Late(project),
            "RESOURCES" => Resources(project),
            "COSTS" => Costs(project),
            "UPCOMING" => Upcoming(project),
            _ => throw new KeyNotFoundException(
                $"Unknown report '{name}'; available: {string.Join(", ", Available.Select(r => r.Name))}."),
        };
        var title = Available.First(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Title;
        return Document(project, title, body);
    }

    // ---------------------------------------------------------------- reports

    private static string Overview(Project project)
    {
        var settings = project.TimeSettings;
        var html = new StringBuilder();
        var leaves = project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        var evm = EarnedValue.ForProject(project);
        var hasBaseline = project.Tasks.Any(t => t.Baseline() is not null);

        html.Append("<section class=\"cards\">");
        Card(html, "Start", Date(project.StartDate));
        Card(html, "Finish", Date(project.FinishDate));
        Card(html, "Tasks", leaves.Count.ToString(CultureInfo.InvariantCulture));
        Card(html, "% complete", ProjectPercent(project) + "%");
        Card(html, "Work", Hours(project.TotalWorkMinutes));
        Card(html, "Cost", Num(project.TotalCost));
        if (hasBaseline)
        {
            Card(html, "SPI", evm.Spi is { } spi ? Num(Math.Round(spi, 2)) : "–", Health(evm.Spi));
            Card(html, "CPI", evm.Cpi is { } cpi ? Num(Math.Round(cpi, 2)) : "–", Health(evm.Cpi));
        }

        html.Append("</section>");

        var milestones = project.Tasks.Where(t => t.IsMilestone && t.IsActive).OrderBy(t => t.Finish).ToList();
        if (milestones.Count > 0)
        {
            html.Append("<h2>Milestones</h2>");
            TaskTable(html, project, milestones, ["id", "name", "finish", "percentComplete", "predecessors"]);
        }

        var critical = leaves.Where(t => t.IsCritical).ToList();
        if (critical.Count > 0)
        {
            html.Append("<h2>Critical path</h2>");
            TaskTable(html, project, critical, ["id", "name", "duration", "start", "finish", "resourceNames"]);
        }

        _ = settings;
        return html.ToString();
    }

    private static string Critical(Project project)
    {
        var html = new StringBuilder();
        var critical = project.Tasks.Where(t => !t.IsSummary && t.IsActive && t.IsCritical).ToList();
        if (critical.Count == 0)
        {
            return "<p class=\"empty\">No critical tasks — the schedule has slack everywhere.</p>";
        }

        TaskTable(html, project, critical, ["id", "name", "duration", "start", "finish", "totalSlack", "percentComplete", "resourceNames"]);
        return html.ToString();
    }

    private static string Late(Project project)
    {
        var late = project.Tasks
            .Where(t => !t.IsSummary && t.IsActive && t.Baseline() is not null)
            .Select(t => (Task: t, Variance: FieldCatalog.Resolve(project, "finishVariance").Accessor(t) as decimal?))
            .Where(pair => pair.Variance is > 0)
            .OrderByDescending(pair => pair.Variance)
            .Select(pair => pair.Task)
            .ToList();
        if (!project.Tasks.Any(t => t.Baseline() is not null))
        {
            return "<p class=\"empty\">No baseline captured — set one (`baseline set`) to track slippage.</p>";
        }

        if (late.Count == 0)
        {
            return "<p class=\"empty\">Nothing finishes later than the baseline plan.</p>";
        }

        var html = new StringBuilder();
        TaskTable(html, project, late, ["id", "name", "baselineFinish", "finish", "finishVariance", "percentComplete"]);
        return html.ToString();
    }

    private static string Resources(Project project)
    {
        if (project.Resources.Count == 0)
        {
            return "<p class=\"empty\">No resources defined.</p>";
        }

        var overallocations = project.FindOverallocations()
            .GroupBy(o => o.Resource)
            .ToDictionary(g => g.Key, g => g.Count());
        var html = new StringBuilder();
        html.Append("<table><thead><tr><th>Resource</th><th>Type</th><th class=\"num\">Assignments</th>")
            .Append("<th class=\"num\">Work</th><th class=\"num\">Cost</th><th class=\"num\">Overallocated days</th></tr></thead><tbody>");
        foreach (var resource in project.Resources)
        {
            var work = resource.Assignments.Where(a => a.Resource.Type == ResourceType.Work).Sum(a => a.WorkMinutes);
            var cost = resource.Assignments.Sum(a => a.Cost);
            var overallocated = overallocations.GetValueOrDefault(resource);
            html.Append("<tr")
                .Append(overallocated > 0 ? " class=\"bad\"" : "")
                .Append("><td>").Append(Escape(resource.Name))
                .Append("</td><td>").Append(resource.Type.ToString().ToLowerInvariant())
                .Append("</td><td class=\"num\">").Append(resource.Assignments.Count)
                .Append("</td><td class=\"num\">").Append(Hours(work))
                .Append("</td><td class=\"num\">").Append(Num(Math.Round(cost, 2)))
                .Append("</td><td class=\"num\">").Append(overallocated == 0 ? "" : overallocated.ToString(CultureInfo.InvariantCulture))
                .Append("</td></tr>");
        }

        html.Append("</tbody></table>");
        return html.ToString();
    }

    private static string Costs(Project project)
    {
        var topLevel = project.Tasks.Where(t => t.OutlineLevel == 0 && t.IsActive).ToList();
        if (topLevel.Count == 0)
        {
            return "<p class=\"empty\">No tasks.</p>";
        }

        var html = new StringBuilder();
        html.Append("<h2>Cost by top-level task</h2>");
        BarChart(html, topLevel.Select(t => (t.Name, t.Cost)).ToList());
        TaskTable(html, project, topLevel, ["id", "name", "fixedCost", "cost", "baselineCost", "costVariance"]);

        var expensive = project.Tasks
            .Where(t => !t.IsSummary && t.IsActive && t.Cost > 0)
            .OrderByDescending(t => t.Cost)
            .Take(10)
            .ToList();
        if (expensive.Count > 0)
        {
            html.Append("<h2>Most expensive tasks</h2>");
            TaskTable(html, project, expensive, ["id", "name", "work", "cost", "resourceNames"]);
        }

        return html.ToString();
    }

    private static string Upcoming(Project project)
    {
        var anchor = project.StatusDate ?? DateTime.Now;
        var horizon = anchor.AddDays(14);
        var upcoming = project.Tasks
            .Where(t => !t.IsSummary && t.IsActive && t.PercentComplete < 100)
            .Where(t => (t.Start >= anchor && t.Start <= horizon) || (t.Finish >= anchor && t.Finish <= horizon))
            .OrderBy(t => t.Start)
            .ToList();
        if (upcoming.Count == 0)
        {
            return $"<p class=\"empty\">Nothing starts or finishes between {Date(anchor)} and {Date(horizon)}.</p>";
        }

        var html = new StringBuilder();
        html.Append("<p class=\"muted\">Window: ").Append(Date(anchor)).Append(" – ").Append(Date(horizon)).Append("</p>");
        TaskTable(html, project, upcoming, ["id", "name", "start", "finish", "percentComplete", "resourceNames"]);
        return html.ToString();
    }

    // ---------------------------------------------------------------- helpers

    private static void TaskTable(StringBuilder html, Project project, IReadOnlyList<ProjectTask> tasks, string[] fields)
    {
        var definitions = fields.Select(f => FieldCatalog.Resolve(project, f)).ToList();
        html.Append("<table><thead><tr>");
        foreach (var definition in definitions)
        {
            html.Append(NumericKinds(definition.Kind) ? "<th class=\"num\">" : "<th>").Append(Escape(definition.Caption)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var task in tasks)
        {
            html.Append("<tr>");
            foreach (var definition in definitions)
            {
                var text = FieldCatalog.Format(definition.Kind, definition.Accessor(task), project.TimeSettings);
                html.Append(NumericKinds(definition.Kind) ? "<td class=\"num\">" : "<td>").Append(Escape(text)).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");

        static bool NumericKinds(FieldKind kind)
            => kind is FieldKind.Number or FieldKind.Cost or FieldKind.Work or FieldKind.Duration or FieldKind.Percent or FieldKind.WholeNumber;
    }

    private static void BarChart(StringBuilder html, List<(string Label, decimal Value)> bars)
    {
        var max = bars.Max(b => b.Value);
        if (max <= 0)
        {
            return;
        }

        const int barHeight = 22;
        const int gap = 6;
        const int labelWidth = 180;
        const int chartWidth = 420;
        var height = bars.Count * (barHeight + gap);
        html.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {labelWidth + chartWidth + 90} {height}\" role=\"img\" class=\"chart\">");
        for (var i = 0; i < bars.Count; i++)
        {
            var y = i * (barHeight + gap);
            var width = (int)(bars[i].Value / max * chartWidth);
            html.Append(CultureInfo.InvariantCulture, $"<text x=\"{labelWidth - 8}\" y=\"{y + 15}\" text-anchor=\"end\" class=\"label\">{Escape(Truncate(bars[i].Label, 26))}</text>")
                .Append(CultureInfo.InvariantCulture, $"<rect x=\"{labelWidth}\" y=\"{y}\" width=\"{Math.Max(2, width)}\" height=\"{barHeight}\" rx=\"3\" class=\"bar\"/>")
                .Append(CultureInfo.InvariantCulture, $"<text x=\"{labelWidth + Math.Max(2, width) + 6}\" y=\"{y + 15}\" class=\"value\">{Num(Math.Round(bars[i].Value, 0))}</text>");
        }

        html.Append("</svg>");
    }

    private static void Card(StringBuilder html, string label, string value, string? modifier = null)
        => html.Append("<div class=\"card").Append(modifier is null ? "" : " " + modifier).Append("\"><div class=\"value\">")
            .Append(Escape(value)).Append("</div><div class=\"label\">").Append(Escape(label)).Append("</div></div>");

    private static string? Health(decimal? index)
        => index is null ? null : index >= 1m ? "good" : index >= 0.9m ? "warn" : "bad";

    private static int ProjectPercent(Project project)
    {
        decimal total = 0m, completed = 0m;
        foreach (var task in project.Tasks.Where(t => !t.IsSummary && t.IsActive))
        {
            total += task.DurationMinutes;
            completed += task.CompletedMinutes;
        }

        return total == 0 ? 0 : (int)Math.Round(completed / total * 100m);
    }

    private static string Document(Project project, string title, string body)
        => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>{{Escape(project.Name)}} — {{Escape(title)}}</title>
        <style>
        body { font: 14px/1.45 -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; color: #24292f; margin: 32px auto; max-width: 900px; padding: 0 16px; }
        h1 { font-size: 22px; margin-bottom: 2px; } h2 { font-size: 16px; margin-top: 28px; }
        .subtitle { color: #6e7781; margin-top: 0; }
        table { border-collapse: collapse; width: 100%; margin: 12px 0; }
        th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #d8dee4; }
        th { color: #6e7781; font-weight: 600; } th.num, td.num { text-align: right; }
        tr.bad td { background: #fff1f0; }
        .cards { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
        .card { border: 1px solid #d8dee4; border-radius: 8px; padding: 10px 16px; min-width: 110px; }
        .card .value { font-size: 20px; font-weight: 600; } .card .label { color: #6e7781; font-size: 12px; }
        .card.good .value { color: #1a7f37; } .card.warn .value { color: #9a6700; } .card.bad .value { color: #cf222e; }
        .empty, .muted { color: #6e7781; }
        .chart { max-width: 100%; } .chart .bar { fill: #4f83cc; } .chart .label, .chart .value { font-size: 12px; fill: #24292f; }
        @media print { body { margin: 0; max-width: none; } h2 { break-after: avoid; } table { break-inside: auto; } tr { break-inside: avoid; } }
        </style>
        </head>
        <body>
        <h1>{{Escape(title)}}</h1>
        <p class="subtitle">{{Escape(project.Name)}} · generated {{Date(DateTime.Now)}}{{(
            project.StatusDate is { } status ? " · status date " + Date(status) : "")}}</p>
        {{body}}
        </body>
        </html>
        """;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");

    private static string Date(DateTime? date)
        => date is { } d ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hours(decimal minutes) => Num(Math.Round(minutes / 60m, 1)) + "h";
}
