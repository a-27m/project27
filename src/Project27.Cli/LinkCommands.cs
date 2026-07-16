using System.CommandLine;
using Project27.Cli.Completion;
using System.Globalization;
using Project27.Core;

namespace Project27.Cli;

internal static class LinkCommands
{
    public static Command Command()
    {
        var command = new Command("link", "Manage task dependencies.");
        command.Add(Add());
        command.Add(Set());
        command.Add(Remove());
        command.Add(List());
        return command;
    }

    private static (Argument<string> Pred, Argument<string> Succ) RefArguments() => (
        new Argument<string>("predecessor") { Description = "Task reference: row id or uid:<n>." }
            .Suggests(CompletionValues.Tasks),
        new Argument<string>("successor") { Description = "Task reference: row id or uid:<n>." }
            .Suggests(CompletionValues.Tasks));

    private static Command Add()
    {
        var (predArg, succArg) = RefArguments();
        var typeOpt = new Option<string?>("--type") { HelpName = "fs|ss|ff|sf", Description = "Dependency type; default fs." }
            .Suggests(CompletionValues.DependencyTypes);
        var lagOpt = new Option<string?>("--lag") { HelpName = "lag", Description = "Lag: 2d, 4eh, 50%; leading - for lead." };
        var command = new Command("add", "Link two tasks.") { predArg, succArg, typeOpt, lagOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var predecessor = Parsers.TaskRef(project, parseResult.GetRequiredValue(predArg));
            var successor = Parsers.TaskRef(project, parseResult.GetRequiredValue(succArg));
            var dependency = project.Link(
                predecessor,
                successor,
                parseResult.GetValue(typeOpt) is { } type ? Parsers.DependencyTypeInput(type) : DependencyType.FinishToStart,
                parseResult.GetValue(lagOpt) is { } lag ? Parsers.LagInput(lag, settings) : Lag.Zero);
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForLink(dependency, settings),
                $"linked {predecessor.RowNumber} -> {successor.RowNumber} "
                + $"({Render.TypeAbbreviation(dependency.Type)}{Render.LagText(dependency.Lag, settings)})");
            return 0;
        }));
        return command;
    }

    private static Command Set()
    {
        var (predArg, succArg) = RefArguments();
        var typeOpt = new Option<string?>("--type") { HelpName = "fs|ss|ff|sf" }.Suggests(CompletionValues.DependencyTypes);
        var lagOpt = new Option<string?>("--lag") { HelpName = "lag" };
        var command = new Command("set", "Change an existing link's type or lag.") { predArg, succArg, typeOpt, lagOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var dependency = Find(project, parseResult.GetRequiredValue(predArg), parseResult.GetRequiredValue(succArg));
            if (parseResult.GetValue(typeOpt) is { } type)
            {
                dependency.Type = Parsers.DependencyTypeInput(type);
            }

            if (parseResult.GetValue(lagOpt) is { } lag)
            {
                dependency.Lag = Parsers.LagInput(lag, settings);
            }

            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForLink(dependency, settings),
                $"updated link {dependency.Predecessor.RowNumber} -> {dependency.Successor.RowNumber}");
            return 0;
        }));
        return command;
    }

    private static Command Remove()
    {
        var (predArg, succArg) = RefArguments();
        var command = new Command("remove", "Unlink two tasks.") { predArg, succArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var dependency = Find(project, parseResult.GetRequiredValue(predArg), parseResult.GetRequiredValue(succArg));
            project.Unlink(dependency);
            project.Recalculate();
            store.Save(project);
            context.Report(
                new RemovedJson("link", $"{dependency.Predecessor.Name} -> {dependency.Successor.Name}"),
                $"unlinked {dependency.Predecessor.RowNumber} -> {dependency.Successor.RowNumber}");
            return 0;
        }));
        return command;
    }

    private static Command List()
    {
        var command = new Command("list", "List all dependencies.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var links = project.Tasks
                .SelectMany(t => t.Successors)
                .OrderBy(d => d.Predecessor.RowNumber)
                .ThenBy(d => d.Successor.RowNumber)
                .ToList();
            if (context.Json)
            {
                context.WriteJson(links.Select(d => JsonShapes.ForLink(d, settings)).ToList());
                return 0;
            }

            if (links.Count == 0)
            {
                context.Out.WriteLine("no links");
                return 0;
            }

            Render.Table(
                context.Out,
                ["Pred", "Pred name", "Succ", "Succ name", "Type", "Lag"],
                [
                    .. links.Select(IReadOnlyList<string> (d) =>
                    [
                        d.Predecessor.RowNumber.ToString(CultureInfo.InvariantCulture),
                        d.Predecessor.Name,
                        d.Successor.RowNumber.ToString(CultureInfo.InvariantCulture),
                        d.Successor.Name,
                        Render.TypeAbbreviation(d.Type),
                        Render.LagText(d.Lag, settings) ?? "",
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static TaskDependency Find(Project project, string predecessorRef, string successorRef)
    {
        var predecessor = Parsers.TaskRef(project, predecessorRef);
        var successor = Parsers.TaskRef(project, successorRef);
        return successor.Predecessors.FirstOrDefault(d => ReferenceEquals(d.Predecessor, predecessor))
            ?? throw new CliException($"no link between {predecessor.RowNumber} and {successor.RowNumber}");
    }
}
