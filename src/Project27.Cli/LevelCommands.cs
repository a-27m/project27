using System.CommandLine;
using System.Globalization;
using Project27.Core.Scheduling;

namespace Project27.Cli;

internal sealed record LevelingJson(
    IReadOnlyList<LevelingDelayJson> Delays,
    IReadOnlyList<LevelingSplitJson> SplitTasks,
    IReadOnlyList<OverallocationJson> RemainingOverallocations);

internal sealed record LevelingSplitJson(int Uid, int Id, string Name);

internal sealed record LevelingDelayJson(int Uid, int Id, string Name, string Delay);

internal sealed record OverallocationJson(string Resource, DateOnly Day, decimal DemandMinutes, decimal CapacityMinutes);

internal static class LevelCommands
{
    public static Command Command()
    {
        var command = new Command("level", "Resource leveling: delay tasks until no work resource is overallocated.");
        command.Add(Run());
        command.Add(Clear());
        return command;
    }

    private static Command Run()
    {
        var orderOpt = new Option<string?>("--order")
        {
            HelpName = "id|standard|priority",
            Description = "Victim order; default priority (priority, then the standard order).",
        };
        var granularityOpt = new Option<string?>("--granularity")
        {
            HelpName = "day|minute",
            Description = "Delay step: whole working days (default) or the exact excess in minutes.",
        };
        var splitInProgressOpt = new Option<bool>("--split-in-progress")
        {
            Description = "Allow splitting the remaining work of started tasks (deviations #28/#29).",
        };
        var command = new Command("run", "Clear previous delays, then level all work resources.")
        {
            orderOpt, granularityOpt, splitInProgressOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var options = new LevelingOptions
            {
                Order = parseResult.GetValue(orderOpt) is { } order ? ParseOrder(order) : LevelingOrder.PriorityStandard,
                Granularity = parseResult.GetValue(granularityOpt) is { } granularity
                    ? ParseGranularity(granularity)
                    : LevelingGranularity.Day,
                SplitInProgress = parseResult.GetValue(splitInProgressOpt),
            };
            var result = project.Level(options);
            store.Save(project);
            if (context.Json)
            {
                context.WriteJson(ToJson(project, result));
                return 0;
            }

            if (result.Delays.Count == 0 && result.SplitTasks.Count == 0)
            {
                context.Out.WriteLine("no overallocations; nothing to level");
            }
            else if (result.Delays.Count > 0)
            {
                Render.Table(
                    context.Out,
                    ["ID", "Name", "Delay", "New start"],
                    [
                        .. result.Delays.Select(IReadOnlyList<string> (d) =>
                        [
                            d.Task.RowNumber.ToString(CultureInfo.InvariantCulture),
                            d.Task.Name,
                            Render.MinutesAsDays(d.DelayMinutes, project.TimeSettings)!,
                            Render.Date(d.Task.Start),
                        ]),
                    ]);
            }

            foreach (var task in result.SplitTasks)
            {
                context.Out.WriteLine($"split remaining work: {task.RowNumber} '{task.Name}'");
            }

            foreach (var remaining in result.RemainingOverallocations)
            {
                context.Out.WriteLine(
                    $"still overallocated: {remaining.Resource.Name} on {Render.DateOnly(remaining.Day)} "
                    + $"({Render.Num(remaining.DemandMinutes / 60m)}h of {Render.Num(remaining.CapacityMinutes / 60m)}h)");
            }

            return 0;
        }));
        return command;
    }

    private static Command Clear()
    {
        var command = new Command("clear", "Remove every leveling delay.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            project.ClearLeveling();
            store.Save(project);
            context.Report(new { cleared = true }, "leveling delays cleared");
            return 0;
        }));
        return command;
    }

    private static LevelingOrder ParseOrder(string text) => text.Trim().ToUpperInvariant() switch
    {
        "ID" => LevelingOrder.IdOnly,
        "STANDARD" => LevelingOrder.Standard,
        "PRIORITY" => LevelingOrder.PriorityStandard,
        _ => throw new CliException($"invalid leveling order '{text}'; use id, standard, or priority"),
    };

    private static LevelingGranularity ParseGranularity(string text) => text.Trim().ToUpperInvariant() switch
    {
        "DAY" => LevelingGranularity.Day,
        "MINUTE" => LevelingGranularity.Minute,
        _ => throw new CliException($"invalid leveling granularity '{text}'; use day or minute"),
    };

    private static LevelingJson ToJson(Core.Project project, LevelingResult result) => new(
        [
            .. result.Delays.Select(d => new LevelingDelayJson(
                d.Task.UniqueId,
                d.Task.RowNumber,
                d.Task.Name,
                Render.MinutesAsDays(d.DelayMinutes, project.TimeSettings)!)),
        ],
        [.. result.SplitTasks.Select(t => new LevelingSplitJson(t.UniqueId, t.RowNumber, t.Name))],
        [
            .. result.RemainingOverallocations.Select(o => new OverallocationJson(
                o.Resource.Name, o.Day, o.DemandMinutes, o.CapacityMinutes)),
        ]);
}
