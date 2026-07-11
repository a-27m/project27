using System.CommandLine;
using Project27.Core;

namespace Project27.Cli;

internal static class BaselineCommands
{
    public static Command Command()
    {
        var command = new Command("baseline", "Capture or clear baseline plans (slots 0-10).");
        command.Add(Set());
        command.Add(Clear());
        return command;
    }

    private static Option<int> SlotOption()
        => new("--slot") { Description = "Baseline slot 0-10; default 0.", DefaultValueFactory = _ => 0 };

    private static Option<string[]> TasksOption()
        => new("--tasks")
        {
            Description = "Task references (row ids or uid:<n>); default: the whole project.",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore,
        };

    private static Command Set()
    {
        var slotOpt = SlotOption();
        var tasksOpt = TasksOption();
        var command = new Command("set", "Capture the current schedule into a baseline slot.") { slotOpt, tasksOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var slot = parseResult.GetValue(slotOpt);
            var scope = Scope(project, parseResult.GetValue(tasksOpt));
            project.SetBaseline(slot, scope);
            store.Save(project);
            context.Report(
                new { slot, tasks = scope?.Count ?? project.Tasks.Count },
                $"baseline {slot} set for {(scope is null ? "all" : Render.Num(scope.Count))} task(s)");
            return 0;
        }));
        return command;
    }

    private static Command Clear()
    {
        var slotOpt = SlotOption();
        var tasksOpt = TasksOption();
        var command = new Command("clear", "Remove a baseline slot's entries.") { slotOpt, tasksOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var slot = parseResult.GetValue(slotOpt);
            var scope = Scope(project, parseResult.GetValue(tasksOpt));
            project.ClearBaseline(slot, scope);
            store.Save(project);
            context.Report(
                new { slot, cleared = true },
                $"baseline {slot} cleared for {(scope is null ? "all" : Render.Num(scope.Count))} task(s)");
            return 0;
        }));
        return command;
    }

    /// <summary>Expands references to the referenced tasks plus their subtrees; null = all.</summary>
    private static List<ProjectTask>? Scope(Project project, string[]? references)
    {
        if (references is null || references.Length == 0)
        {
            return null;
        }

        return [.. references
            .Select(reference => Parsers.TaskRef(project, reference))
            .SelectMany(task => task.SelfAndDescendants())
            .Distinct()];
    }
}
