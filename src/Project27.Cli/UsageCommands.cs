using System.CommandLine;
using System.Globalization;
using Project27.Core;
using Project27.Core.Usage;
using Project27.Core.Views;

namespace Project27.Cli;

internal sealed record UsageBucketJson(DateOnly Date, decimal WorkMinutes, decimal Cost);

internal sealed record UsageRowJson(
    int Uid,
    int Id,
    string Name,
    string? Resource,
    IReadOnlyList<UsageBucketJson> Buckets,
    decimal TotalWorkMinutes,
    decimal TotalCost);

internal static class UsageCommands
{
    public static Command Command()
    {
        var fromOpt = new Option<string?>("--from") { HelpName = "date", Description = "First bucket; default: the project start." };
        var toOpt = new Option<string?>("--to") { HelpName = "date", Description = "Last bucket; default: the project finish." };
        var granularityOpt = new Option<string?>("--granularity", "-g") { HelpName = "day|week", Description = "Bucket size; default week." };
        var assignmentsOpt = new Option<bool>("--assignments") { Description = "Break each task down into its assignments." };
        var costOpt = new Option<bool>("--cost") { Description = "Show cost per bucket instead of work." };
        var filterOpt = new Option<string?>("--filter") { HelpName = "expr", Description = "Task filter, as in `view`." };
        var command = new Command("usage", "Time-phased work (or cost) per task across day or week buckets.")
        {
            fromOpt, toOpt, granularityOpt, assignmentsOpt, costOpt, filterOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var weekly = (parseResult.GetValue(granularityOpt) ?? "week").Trim().ToUpperInvariant() switch
            {
                "DAY" or "D" => false,
                "WEEK" or "W" => true,
                var other => throw new CliException($"invalid --granularity '{other}'; use day or week"),
            };
            var showCost = parseResult.GetValue(costOpt);
            var filter = parseResult.GetValue(filterOpt) is { } filterText
                ? ParseFilter(project, filterText)
                : null;

            var tasks = project.Tasks.Where(t => filter is null || filter.Matches(t)).ToList();
            var rows = new List<(ProjectTask Task, Assignment? Assignment, IReadOnlyList<TimephasedBucket> Buckets)>();
            foreach (var task in tasks)
            {
                rows.Add((task, null, Timephased.ForTask(task)));
                if (parseResult.GetValue(assignmentsOpt) && !task.IsSummary)
                {
                    rows.AddRange(task.Assignments.Select(a => (task, (Assignment?)a, Timephased.ForAssignment(a))));
                }
            }

            if (weekly)
            {
                rows = [.. rows.Select(r => (r.Task, r.Assignment, Timephased.ByWeek(r.Buckets, settings.WeekStartsOn)))];
            }

            var from = parseResult.GetValue(fromOpt) is { } fromText
                ? DateOnly.FromDateTime(Parsers.DateInput(fromText, settings, finishLike: false))
                : DateOnly.FromDateTime(project.StartDate);
            var to = parseResult.GetValue(toOpt) is { } toText
                ? DateOnly.FromDateTime(Parsers.DateInput(toText, settings, finishLike: true))
                : DateOnly.FromDateTime(project.FinishDate ?? project.StartDate);
            if (weekly)
            {
                from = from.AddDays(-(((int)from.DayOfWeek - (int)settings.WeekStartsOn + 7) % 7));
            }

            var columns = new List<DateOnly>();
            for (var day = from; day <= to; day = day.AddDays(weekly ? 7 : 1))
            {
                columns.Add(day);
            }

            if (columns.Count > 60)
            {
                throw new CliException($"{columns.Count} buckets would not fit; narrow --from/--to or use --granularity week");
            }

            if (context.Json)
            {
                context.WriteJson(rows.Select(r => new UsageRowJson(
                    r.Task.UniqueId,
                    r.Task.RowNumber,
                    r.Task.Name,
                    r.Assignment?.Resource.Name,
                    [.. r.Buckets.Where(b => b.Date >= from && b.Date <= to.AddDays(weekly ? 6 : 0))
                        .Select(b => new UsageBucketJson(b.Date, b.WorkMinutes, Math.Round(b.Cost, 2)))],
                    r.Buckets.Sum(b => b.WorkMinutes),
                    Math.Round(r.Buckets.Sum(b => b.Cost), 2))).ToList());
                return 0;
            }

            if (rows.Count == 0)
            {
                context.Out.WriteLine("no matching tasks");
                return 0;
            }

            string Cell(IReadOnlyList<TimephasedBucket> buckets, DateOnly column)
            {
                var match = buckets.FirstOrDefault(b => b.Date == column);
                if (match.Date != column)
                {
                    return "";
                }

                return showCost
                    ? Render.Num(Math.Round(match.Cost, 2))
                    : Render.Num(Math.Round(match.WorkMinutes / 60m, 2)) + "h";
            }

            Render.Table(
                context.Out,
                [
                    "ID",
                    "Name",
                    .. columns.Select(c => c.ToString(weekly ? "MM-dd" : "MM-dd", CultureInfo.InvariantCulture)),
                    "Total",
                ],
                [
                    .. rows.Select(IReadOnlyList<string> (r) =>
                    [
                        r.Assignment is null ? r.Task.RowNumber.ToString(CultureInfo.InvariantCulture) : "",
                        r.Assignment is null
                            ? new string(' ', 2 * r.Task.OutlineLevel) + r.Task.Name
                            : new string(' ', 2 * r.Task.OutlineLevel + 2) + r.Assignment.Resource.Name,
                        .. columns.Select(c => Cell(r.Buckets, c)),
                        showCost
                            ? Render.Num(Math.Round(r.Buckets.Sum(b => b.Cost), 2))
                            : Render.Num(Math.Round(r.Buckets.Sum(b => b.WorkMinutes) / 60m, 2)) + "h",
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static FilterNode ParseFilter(Project project, string text)
    {
        try
        {
            return FilterParser.Parse(project, text);
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException)
        {
            throw new CliException(exception.Message, exception);
        }
    }
}
