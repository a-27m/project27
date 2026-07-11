using System.CommandLine;
using System.Globalization;
using Project27.Core.Scheduling;

namespace Project27.Cli;

internal sealed record LevelingJson(
    IReadOnlyList<LevelingDelayJson> Delays,
    IReadOnlyList<OverallocationJson> RemainingOverallocations);

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
        var command = new Command("run", "Clear previous delays, then level all work resources.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var result = project.Level();
            store.Save(project);
            if (context.Json)
            {
                context.WriteJson(ToJson(project, result));
                return 0;
            }

            if (result.Delays.Count == 0)
            {
                context.Out.WriteLine("no overallocations; nothing to level");
            }
            else
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

    private static LevelingJson ToJson(Core.Project project, LevelingResult result) => new(
        [
            .. result.Delays.Select(d => new LevelingDelayJson(
                d.Task.UniqueId,
                d.Task.RowNumber,
                d.Task.Name,
                Render.MinutesAsDays(d.DelayMinutes, project.TimeSettings)!)),
        ],
        [
            .. result.RemainingOverallocations.Select(o => new OverallocationJson(
                o.Resource.Name, o.Day, o.DemandMinutes, o.CapacityMinutes)),
        ]);
}
