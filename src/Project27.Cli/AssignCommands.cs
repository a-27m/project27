using System.CommandLine;
using Project27.Cli.Completion;
using System.Globalization;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

internal static class AssignCommands
{
    public static Command Command()
    {
        var command = new Command("assign", "Manage resource assignments.");
        command.Add(Add());
        command.Add(List());
        command.Add(Set());
        command.Add(Remove());
        return command;
    }

    private static Argument<string> TaskArg()
        => new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." }
            .Suggests(CompletionValues.Tasks);

    private static Argument<string> ResourceArg()
        => new Argument<string>("resource") { Description = "Resource name (case-insensitive) or uid:<n>." }
            .Suggests(CompletionValues.Resources);

    private static Command Add()
    {
        var taskArg = TaskArg();
        var resourceArg = ResourceArg();
        var unitsOpt = new Option<string?>("--units") { HelpName = "units", Description = "Assignment units (50%) or material quantity." };
        var workOpt = new Option<string?>("--work") { HelpName = "duration", Description = "Work, e.g. 20h; default duration × units." };
        var contourOpt = new Option<string?>("--contour") { HelpName = "name" }.Suggests(CompletionValues.Contours);
        var delayOpt = new Option<string?>("--delay") { HelpName = "duration" };
        var tableOpt = new Option<string?>("--table") { HelpName = "A..E", Description = "Cost rate table; default A." }
            .Suggests(CompletionValues.RateTables);
        var costOpt = new Option<string?>("--cost") { HelpName = "amount", Description = "Expense amount (cost resources)." };
        var perOpt = new Option<string?>("--per")
        {
            HelpName = "unit",
            Description = "Material consumption per time unit (h, d, w, mo, y): --units 10 --per d = 10/day.",
        };
        var command = new Command("add", "Assign a resource to a task.")
        {
            taskArg, resourceArg, unitsOpt, workOpt, contourOpt, delayOpt, tableOpt, costOpt, perOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(taskArg));
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            var assignment = project.Assign(
                task,
                resource,
                parseResult.GetValue(unitsOpt) is { } units ? Parsers.UnitsInput(units) : null,
                parseResult.GetValue(workOpt) is { } work ? Parsers.DurationInput(work) : null);
            if (parseResult.GetValue(perOpt) is { } per)
            {
                assignment.MaterialRateUnit = Parsers.RateUnitInput(per);
            }

            ApplyExtras(assignment, parseResult, contourOpt, delayOpt, tableOpt, costOpt, project.TimeSettings);
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForAssignment(assignment),
                $"assigned '{resource.Name}' to task {task.RowNumber} '{task.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command List()
    {
        var taskArg = new Argument<string?>("task")
        {
            Description = "Optional task reference; default: all assignments.",
            Arity = ArgumentArity.ZeroOrOne,
        }.Suggests(CompletionValues.Tasks);
        var command = new Command("list", "List assignments.") { taskArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var scope = parseResult.GetValue(taskArg) is { } taskRef ? Parsers.TaskRef(project, taskRef) : null;
            var assignments = (scope is null ? project.Tasks : [scope])
                .SelectMany(t => t.Assignments)
                .ToList();
            if (context.Json)
            {
                context.WriteJson(assignments.Select(JsonShapes.ForAssignment).ToList());
                return 0;
            }

            if (assignments.Count == 0)
            {
                context.Out.WriteLine("no assignments");
                return 0;
            }

            Render.Table(
                context.Out,
                ["Task", "Name", "Resource", "Units", "Work", "Start", "Finish", "Cost"],
                [
                    .. assignments.Select(IReadOnlyList<string> (a) =>
                    [
                        a.Task.RowNumber.ToString(CultureInfo.InvariantCulture),
                        a.Task.Name,
                        a.Resource.Name,
                        Render.Units(a),
                        a.Resource.Type == ResourceType.Work ? Render.WorkHours(a.WorkMinutes) : "",
                        Render.Date(a.Start),
                        Render.Date(a.Finish),
                        Render.Num(a.Cost),
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static Command Set()
    {
        var taskArg = TaskArg();
        var resourceArg = ResourceArg();
        var unitsOpt = new Option<string?>("--units") { HelpName = "units" };
        var workOpt = new Option<string?>("--work") { HelpName = "duration" };
        var contourOpt = new Option<string?>("--contour") { HelpName = "name" }.Suggests(CompletionValues.Contours);
        var delayOpt = new Option<string?>("--delay") { HelpName = "duration" };
        var tableOpt = new Option<string?>("--table") { HelpName = "A..E" }.Suggests(CompletionValues.RateTables);
        var costOpt = new Option<string?>("--cost") { HelpName = "amount" };
        var perOpt = new Option<string?>("--per")
        {
            HelpName = "unit|none",
            Description = "Material consumption per time unit (h, d, w, mo, y); 'none' = fixed quantity.",
        };
        var actualWorkOpt = new Option<string?>("--actual-work")
        {
            HelpName = "duration|none",
            Description = "Explicit actual work; 'none' = derive from % complete.",
        };
        var actualCostOpt = new Option<string?>("--actual-cost")
        {
            HelpName = "amount|none",
            Description = "Explicit actual cost; 'none' = derive from % complete.",
        };
        var command = new Command("set", "Change an assignment; the task type decides what recalculates.")
        {
            taskArg, resourceArg, unitsOpt, workOpt, contourOpt, delayOpt, tableOpt, costOpt, perOpt, actualWorkOpt, actualCostOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var assignment = Find(project, parseResult.GetRequiredValue(taskArg), parseResult.GetRequiredValue(resourceArg));
            if (parseResult.GetValue(unitsOpt) is { } units)
            {
                assignment.SetUnits(Parsers.UnitsInput(units));
            }

            if (parseResult.GetValue(workOpt) is { } work)
            {
                assignment.SetWork(Parsers.DurationInput(work));
            }

            if (parseResult.GetValue(perOpt) is { } per)
            {
                assignment.MaterialRateUnit = IsNone(per) ? null : Parsers.RateUnitInput(per);
            }

            if (parseResult.GetValue(actualWorkOpt) is { } actualWork)
            {
                assignment.ActualWorkMinutes = IsNone(actualWork)
                    ? null
                    : Parsers.DurationInput(actualWork).ToMinutes(project.TimeSettings);
            }

            if (parseResult.GetValue(actualCostOpt) is { } actualCost)
            {
                assignment.ActualCost = IsNone(actualCost) ? null : Parsers.MoneyInput(actualCost);
            }

            ApplyExtras(assignment, parseResult, contourOpt, delayOpt, tableOpt, costOpt, project.TimeSettings);
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForAssignment(assignment),
                $"updated assignment of '{assignment.Resource.Name}' on task {assignment.Task.RowNumber}");
            return 0;
        }));
        return command;
    }

    private static Command Remove()
    {
        var taskArg = TaskArg();
        var resourceArg = ResourceArg();
        var command = new Command("remove", "Remove an assignment.") { taskArg, resourceArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var assignment = Find(project, parseResult.GetRequiredValue(taskArg), parseResult.GetRequiredValue(resourceArg));
            project.Unassign(assignment);
            project.Recalculate();
            store.Save(project);
            context.Report(
                new RemovedJson("assignment", $"{assignment.Resource.Name} on task {assignment.Task.RowNumber}"),
                $"unassigned '{assignment.Resource.Name}' from task {assignment.Task.RowNumber}");
            return 0;
        }));
        return command;
    }

    private static bool IsNone(string text) => string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase);

    private static Assignment Find(Project project, string taskRef, string resourceRef)
    {
        var task = Parsers.TaskRef(project, taskRef);
        var resource = Parsers.ResourceRef(project, resourceRef);
        return task.Assignments.FirstOrDefault(a => ReferenceEquals(a.Resource, resource))
            ?? throw new CliException($"'{resource.Name}' is not assigned to task {task.RowNumber}");
    }

    private static void ApplyExtras(
        Assignment assignment,
        System.CommandLine.ParseResult parseResult,
        Option<string?> contourOpt,
        Option<string?> delayOpt,
        Option<string?> tableOpt,
        Option<string?> costOpt,
        TimeSettings settings)
    {
        if (parseResult.GetValue(contourOpt) is { } contour)
        {
            assignment.SetContour(Parsers.ContourInput(contour));
        }

        if (parseResult.GetValue(delayOpt) is { } delay)
        {
            assignment.DelayMinutes = Parsers.DurationInput(delay).ToMinutes(settings);
        }

        if (parseResult.GetValue(tableOpt) is { } table)
        {
            assignment.RateTable = Parsers.RateTableInput(table);
        }

        if (parseResult.GetValue(costOpt) is { } cost)
        {
            assignment.CostInput = Parsers.MoneyInput(cost);
        }
    }
}
