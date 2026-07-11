using System.CommandLine;
using System.Globalization;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

internal static class TaskCommands
{
    public static Command Command()
    {
        var command = new Command("task", "Manage tasks and the outline.");
        command.Add(Add());
        command.Add(AddRecurring());
        command.Add(List());
        command.Add(Show());
        command.Add(Set());
        command.Add(Remove());
        command.Add(Move());
        command.Add(IndentOutdent("indent", "Indent tasks one outline level (under the preceding sibling)."));
        command.Add(IndentOutdent("outdent", "Outdent tasks one outline level."));
        command.Add(Split());
        command.Add(Unsplit());
        command.Add(Evm());
        command.Add(Drivers());
        return command;
    }

    private static Command Add()
    {
        var nameArg = new Argument<string>("name");
        var durationOpt = new Option<string?>("--duration", "-d") { HelpName = "duration" };
        var parentOpt = new Option<string?>("--parent") { HelpName = "ref", Description = "Parent task; default: top level." };
        var atOpt = new Option<int?>("--at") { Description = "0-based child position under the parent; default: append." };
        var milestoneOpt = new Option<bool>("--milestone") { Description = "Add a zero-duration milestone." };
        var command = new Command("add", "Add a task.") { nameArg, durationOpt, parentOpt, atOpt, milestoneOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var parent = parseResult.GetValue(parentOpt) is { } parentRef ? Parsers.TaskRef(project, parentRef) : null;
            Duration? duration = parseResult.GetValue(durationOpt) is { } durationText
                ? Parsers.DurationInput(durationText)
                : null;
            if (parseResult.GetValue(milestoneOpt))
            {
                if (duration is { } explicitDuration && explicitDuration.ToMinutes(project.TimeSettings) != 0)
                {
                    throw new CliException("--milestone conflicts with a non-zero --duration");
                }

                duration = new Duration(0, DurationUnit.Days);
            }

            var task = project.AddTask(parseResult.GetRequiredValue(nameArg), duration, parent, parseResult.GetValue(atOpt));
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForTask(task, project.TimeSettings),
                $"added task {task.RowNumber} '{task.Name}' (uid {task.UniqueId})");
            return 0;
        }));
        return command;
    }

    private static Command AddRecurring()
    {
        var nameArg = new Argument<string>("name");
        var durationOpt = new Option<string>("--duration", "-d") { HelpName = "duration", Required = true };
        var recurOpt = new Option<string>("--recur") { HelpName = "spec", Required = true, Description = "Recurrence, e.g. weekly:mon,fri." };
        var fromOpt = new Option<string>("--from") { HelpName = "date", Required = true };
        var untilOpt = new Option<string?>("--until") { HelpName = "date" };
        var timesOpt = new Option<int?>("--times") { Description = "Number of occurrences (alternative to --until)." };
        var parentOpt = new Option<string?>("--parent") { HelpName = "ref" };
        var command = new Command("add-recurring", "Add a recurring task (a summary with one child per occurrence).")
        {
            nameArg, durationOpt, recurOpt, fromOpt, untilOpt, timesOpt, parentOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var parent = parseResult.GetValue(parentOpt) is { } parentRef ? Parsers.TaskRef(project, parentRef) : null;
            var task = project.AddRecurringTask(
                parseResult.GetRequiredValue(nameArg),
                Parsers.DurationInput(parseResult.GetRequiredValue(durationOpt)),
                Parsers.RecurrenceInput(parseResult.GetRequiredValue(recurOpt)),
                Parsers.DateOnlyInput(parseResult.GetRequiredValue(fromOpt)),
                parseResult.GetValue(untilOpt) is { } until ? Parsers.DateOnlyInput(until) : null,
                parseResult.GetValue(timesOpt),
                parent);
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForTask(task, project.TimeSettings),
                $"added recurring task {task.RowNumber} '{task.Name}' with {task.Children.Count} occurrences");
            return 0;
        }));
        return command;
    }

    private static Command List()
    {
        var criticalOpt = new Option<bool>("--critical") { Description = "Only critical tasks." };
        var command = new Command("list", "List tasks in outline order.") { criticalOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var tasks = project.Tasks.Where(t => !parseResult.GetValue(criticalOpt) || t.IsCritical).ToList();
            if (context.Json)
            {
                context.WriteJson(tasks.Select(t => JsonShapes.ForTask(t, settings)).ToList());
                return 0;
            }

            if (tasks.Count == 0)
            {
                context.Out.WriteLine("no tasks");
                return 0;
            }

            Render.Table(
                context.Out,
                ["ID", "Name", "Duration", "Start", "Finish", "Preds", "%", "Crit"],
                [
                    .. tasks.Select(IReadOnlyList<string> (t) =>
                    [
                        t.RowNumber.ToString(CultureInfo.InvariantCulture),
                        Indent(t) + t.Name + (t.IsActive ? "" : " (inactive)"),
                        Render.DurationText(t, settings),
                        Render.Date(t.Start),
                        Render.Date(t.Finish),
                        string.Join(",", t.Predecessors.Select(d => Render.PredecessorToken(d, settings))),
                        t.PercentComplete == 0 ? "" : Render.Num(t.PercentComplete) + "%",
                        t.IsCritical ? "*" : "",
                    ]),
                ]);
            return 0;

            static string Indent(ProjectTask task) => new(' ', 2 * task.OutlineLevel);
        }));
        return command;
    }

    private static Command Show()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var command = new Command("show", "Print every field of one task.") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            if (context.Json)
            {
                context.WriteJson(JsonShapes.ForTask(task, settings));
                return 0;
            }

            var flags = new[]
            {
                task.IsSummary ? "summary" : null,
                task.IsMilestone ? "milestone" : null,
                task.IsRecurring ? "recurring" : null,
                task.IsActive ? null : "inactive",
                task.IsCritical ? "critical" : null,
                task.IsSplit ? "split" : null,
            }.Where(f => f is not null);
            Render.KeyValues(context.Out,
            [
                ("id", Render.Num(task.RowNumber)),
                ("uid", Render.Num(task.UniqueId)),
                ("name", task.Name),
                ("wbs", task.Wbs),
                ("outline level", Render.Num(task.OutlineLevel)),
                ("mode", task.Mode == TaskMode.Auto ? "auto" : "manual"),
                ("flags", string.Join(", ", flags)),
                ("duration", Render.DurationText(task, settings) + (task.IsEstimated ? " (estimated)" : "")),
                ("start", Render.Date(task.Start)),
                ("finish", Render.Date(task.Finish)),
                ("early start", Render.Date(task.EarlyStart)),
                ("early finish", Render.Date(task.EarlyFinish)),
                ("late start", Render.Date(task.LateStart)),
                ("late finish", Render.Date(task.LateFinish)),
                ("total slack", Render.MinutesAsDays(task.TotalSlackMinutes, settings) ?? ""),
                ("free slack", Render.MinutesAsDays(task.FreeSlackMinutes, settings) ?? ""),
                ("% complete", Render.Num(task.PercentComplete) + "%"),
                ("actual start", Render.Date(task.ActualStart)),
                ("actual finish", Render.Date(task.ActualFinish)),
                ("baseline", task.Baseline() is { } baseline
                    ? $"{Render.Date(baseline.Start)} -> {Render.Date(baseline.Finish)}  cost {Render.Num(baseline.Cost)}"
                    : ""),
                ("type", Render.TaskTypeName(task.Type) + (task.IsEffortDriven ? ", effort-driven" : "")),
                ("work", Render.WorkHours(task.WorkMinutes)),
                ("cost", Render.Num(task.Cost)),
                ("fixed cost", Render.Num(task.FixedCost)),
                ("constraint", ConstraintText(task)),
                ("deadline", Render.Date(task.Deadline)),
                ("priority", Render.Num(task.Priority)),
                ("calendar", task.Calendar?.Name ?? "(project)"),
                ("manual start", Render.Date(task.ManualStart)),
                ("manual finish", Render.Date(task.ManualFinish)),
                ("segments", string.Join(", ", task.Segments.Select(s => $"{Render.Date(s.Start)} -> {Render.Date(s.Finish)}"))),
                ("predecessors", string.Join(",", task.Predecessors.Select(d => Render.PredecessorToken(d, settings)))),
                ("successors", string.Join(",", task.Successors.Select(d =>
                    d.Successor.RowNumber.ToString(CultureInfo.InvariantCulture)))),
                ("resources", string.Join(", ", task.Assignments.Select(a =>
                    a.Resource.Name + (a.Resource.Type == ResourceType.Work && a.Units != 1m
                        ? $"[{Render.Units(a)}]"
                        : "")))),
            ]);
            return 0;

            static string ConstraintText(ProjectTask task)
                => task.Constraint + (task.ConstraintDate is { } date ? " " + Render.Date(date) : "");
        }));
        return command;
    }

    private static Command Set()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var nameOpt = new Option<string?>("--name");
        var durationOpt = new Option<string?>("--duration", "-d") { HelpName = "duration" };
        var modeOpt = new Option<string?>("--mode") { HelpName = "auto|manual" };
        var activeOpt = new Option<string?>("--active") { HelpName = "bool" };
        var milestoneOpt = new Option<string?>("--milestone") { HelpName = "bool" };
        var priorityOpt = new Option<int?>("--priority") { Description = "0-1000, default 500." };
        var deadlineOpt = new Option<string?>("--deadline") { HelpName = "date|none" };
        var constraintOpt = new Option<string?>("--constraint") { HelpName = "asap|alap|snet|snlt|fnet|fnlt|mso|mfo" };
        var constraintDateOpt = new Option<string?>("--constraint-date") { HelpName = "date" };
        var calendarOpt = new Option<string?>("--calendar") { HelpName = "name|default" };
        var wbsOpt = new Option<string?>("--wbs") { HelpName = "code|auto" };
        var manualStartOpt = new Option<string?>("--manual-start") { HelpName = "date|none" };
        var manualFinishOpt = new Option<string?>("--manual-finish") { HelpName = "date|none" };
        var typeOpt = new Option<string?>("--type") { HelpName = "fixed-units|fixed-duration|fixed-work" };
        var effortOpt = new Option<string?>("--effort-driven") { HelpName = "bool" };
        var fixedCostOpt = new Option<string?>("--fixed-cost") { HelpName = "amount" };
        var accrualOpt = new Option<string?>("--accrual") { HelpName = "start|prorated|end" };
        var ignoreResCalOpt = new Option<string?>("--ignore-resource-calendars") { HelpName = "bool" };
        var percentOpt = new Option<int?>("--percent-complete") { Description = "0-100." };
        var actualStartOpt = new Option<string?>("--actual-start") { HelpName = "date|none" };
        var actualFinishOpt = new Option<string?>("--actual-finish") { HelpName = "date|none" };
        var remainingOpt = new Option<string?>("--remaining-duration") { HelpName = "duration" };
        var fieldOpt = new Option<string[]>("--field")
        {
            HelpName = "name=value",
            Description = "Set a custom field value ('none' clears); repeatable.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command("set", "Change task fields; recalculates and saves.")
        {
            refArg, nameOpt, durationOpt, modeOpt, activeOpt, milestoneOpt, priorityOpt, deadlineOpt,
            constraintOpt, constraintDateOpt, calendarOpt, wbsOpt, manualStartOpt, manualFinishOpt,
            typeOpt, effortOpt, fixedCostOpt, accrualOpt, ignoreResCalOpt,
            percentOpt, actualStartOpt, actualFinishOpt, remainingOpt, fieldOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var settings = project.TimeSettings;
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));

            if (parseResult.GetValue(nameOpt) is { } name)
            {
                task.Name = name;
            }

            // Type before duration: the type decides what a duration edit recalculates.
            if (parseResult.GetValue(typeOpt) is { } taskType)
            {
                task.Type = Parsers.TaskTypeInput(taskType);
            }

            if (parseResult.GetValue(effortOpt) is { } effort)
            {
                task.IsEffortDriven = Parsers.BoolInput(effort);
            }

            if (parseResult.GetValue(ignoreResCalOpt) is { } ignoreResCal)
            {
                task.IgnoresResourceCalendars = Parsers.BoolInput(ignoreResCal);
            }

            if (parseResult.GetValue(fixedCostOpt) is { } fixedCost)
            {
                task.FixedCost = Parsers.MoneyInput(fixedCost);
            }

            if (parseResult.GetValue(accrualOpt) is { } accrual)
            {
                task.FixedCostAccrual = Parsers.AccrualInput(accrual);
            }

            if (parseResult.GetValue(durationOpt) is { } duration)
            {
                task.Duration = Parsers.DurationInput(duration);
            }

            if (parseResult.GetValue(modeOpt) is { } mode)
            {
                task.Mode = mode.Trim().ToUpperInvariant() switch
                {
                    "AUTO" => TaskMode.Auto,
                    "MANUAL" => TaskMode.Manual,
                    _ => throw new CliException($"invalid --mode '{mode}'; use auto or manual"),
                };
            }

            if (parseResult.GetValue(activeOpt) is { } active)
            {
                task.IsActive = Parsers.BoolInput(active);
            }

            if (parseResult.GetValue(milestoneOpt) is { } milestone)
            {
                task.IsMilestone = Parsers.BoolInput(milestone);
            }

            if (parseResult.GetValue(priorityOpt) is { } priority)
            {
                task.Priority = priority;
            }

            if (parseResult.GetValue(deadlineOpt) is { } deadline)
            {
                task.Deadline = None(deadline) ? null : Parsers.DateInput(deadline, settings, finishLike: true);
            }

            var constraintText = parseResult.GetValue(constraintOpt);
            var constraintDateText = parseResult.GetValue(constraintDateOpt);
            if (constraintText is not null || constraintDateText is not null)
            {
                var type = constraintText is null ? task.Constraint : Parsers.ConstraintInput(constraintText);
                var needsDate = type is not (ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible);
                var finishLike = type
                    is ConstraintType.FinishNoEarlierThan
                    or ConstraintType.FinishNoLaterThan
                    or ConstraintType.MustFinishOn;
                var date = constraintDateText is not null
                    ? Parsers.DateInput(constraintDateText, settings, finishLike)
                    : needsDate ? task.ConstraintDate : null;
                if (needsDate && date is null)
                {
                    throw new CliException($"constraint '{constraintText}' requires --constraint-date");
                }

                task.SetConstraint(type, date);
            }

            if (parseResult.GetValue(calendarOpt) is { } calendar)
            {
                task.Calendar = string.Equals(calendar.Trim(), "default", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Parsers.CalendarByName(project, calendar);
            }

            if (parseResult.GetValue(wbsOpt) is { } wbs)
            {
                task.CustomWbs = string.Equals(wbs.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ? null : wbs;
            }

            if (parseResult.GetValue(percentOpt) is { } percent)
            {
                task.PercentComplete = percent;
            }

            if (parseResult.GetValue(actualStartOpt) is { } actualStart)
            {
                task.ActualStart = None(actualStart) ? null : Parsers.DateInput(actualStart, settings, finishLike: false);
            }

            if (parseResult.GetValue(actualFinishOpt) is { } actualFinish)
            {
                task.ActualFinish = None(actualFinish) ? null : Parsers.DateInput(actualFinish, settings, finishLike: true);
            }

            if (parseResult.GetValue(remainingOpt) is { } remaining)
            {
                task.SetRemainingDuration(Parsers.DurationInput(remaining));
            }

            foreach (var assignmentText in parseResult.GetValue(fieldOpt) ?? [])
            {
                var separator = assignmentText.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    throw new CliException($"invalid --field '{assignmentText}'; use name=value");
                }

                var fieldName = assignmentText[..separator].Trim();
                var valueText = assignmentText[(separator + 1)..].Trim();
                var customField = project.FindCustomField(fieldName)
                    ?? throw new CliException($"no custom field '{fieldName}'");
                object? value = string.Equals(valueText, "none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Core.Fields.FieldCatalog.ParseLiteral(customField.Kind, valueText, settings);
                task.SetCustomValue(customField, value);
            }

            if (parseResult.GetValue(manualStartOpt) is { } manualStart)
            {
                task.ManualStart = None(manualStart) ? null : Parsers.DateInput(manualStart, settings, finishLike: false);
            }

            if (parseResult.GetValue(manualFinishOpt) is { } manualFinish)
            {
                task.ManualFinish = None(manualFinish) ? null : Parsers.DateInput(manualFinish, settings, finishLike: true);
            }

            project.Recalculate();
            store.Save(project);
            context.Report(JsonShapes.ForTask(task, settings), $"updated task {task.RowNumber} '{task.Name}'");
            return 0;

            static bool None(string text) => string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        }));
        return command;
    }

    private static Command Remove()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var command = new Command("remove", "Remove a task (and its subtree).") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            project.RemoveTask(task);
            project.Recalculate();
            store.Save(project);
            context.Report(
                new RemovedJson("task", task.Name, task.UniqueId),
                $"removed task '{task.Name}' (uid {task.UniqueId})");
            return 0;
        }));
        return command;
    }

    private static Command Move()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var parentOpt = new Option<string>("--parent") { HelpName = "ref|top", Required = true };
        var atOpt = new Option<int?>("--at") { Description = "0-based child position; default: append." };
        var command = new Command("move", "Move a task (and its subtree) elsewhere in the outline.") { refArg, parentOpt, atOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            var parentText = parseResult.GetRequiredValue(parentOpt);
            var parent = string.Equals(parentText.Trim(), "top", StringComparison.OrdinalIgnoreCase)
                ? null
                : Parsers.TaskRef(project, parentText);
            var index = parseResult.GetValue(atOpt)
                ?? (parent?.Children.Count ?? project.Tasks.Count(t => t.OutlineLevel == 0));
            project.MoveTask(task, parent, index);
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForTask(task, project.TimeSettings),
                $"moved task {task.RowNumber} '{task.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command IndentOutdent(string verb, string description)
    {
        var refsArg = new Argument<string[]>("tasks")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Task references: row ids or uid:<n>.",
        };
        var command = new Command(verb, description) { refsArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            // Resolve every reference before mutating: outline edits renumber rows.
            var tasks = parseResult.GetRequiredValue(refsArg).Select(r => Parsers.TaskRef(project, r)).ToList();
            foreach (var task in tasks)
            {
                if (verb == "indent")
                {
                    project.Indent(task);
                }
                else
                {
                    project.Outdent(task);
                }
            }

            project.Recalculate();
            store.Save(project);
            context.Report(
                tasks.Select(t => JsonShapes.ForTask(t, project.TimeSettings)).ToList(),
                $"{verb}ed {tasks.Count} task(s)");
            return 0;
        }));
        return command;
    }

    private static Command Split()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var atOpt = new Option<string>("--at") { HelpName = "duration", Required = true, Description = "Working-time offset from the task start." };
        var gapOpt = new Option<string>("--gap") { HelpName = "duration", Required = true, Description = "Length of the interruption." };
        var command = new Command("split", "Split a task at a working-time offset.") { refArg, atOpt, gapOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            task.SplitAt(
                Parsers.DurationInput(parseResult.GetRequiredValue(atOpt)),
                Parsers.DurationInput(parseResult.GetRequiredValue(gapOpt)));
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForTask(task, project.TimeSettings),
                $"split task {task.RowNumber} into {task.Segments.Count} segments");
            return 0;
        }));
        return command;
    }

    private static Command Unsplit()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var command = new Command("unsplit", "Remove all splits from a task.") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            task.ClearSplits();
            project.Recalculate();
            store.Save(project);
            context.Report(
                JsonShapes.ForTask(task, project.TimeSettings),
                $"removed splits from task {task.RowNumber}");
            return 0;
        }));
        return command;
    }

    private static Command Evm()
    {
        var refArg = new Argument<string?>("task")
        {
            Description = "Optional task reference; default: every task plus the project row.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("evm", "Earned-value figures at the status date (baseline 0).") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var scope = parseResult.GetValue(refArg) is { } taskRef ? Parsers.TaskRef(project, taskRef) : null;
            if (context.Json)
            {
                context.WriteJson(scope is null
                    ? project.Tasks.Select(t => JsonShapes.ForEvm(t.RowNumber, t.Name, EarnedValue.ForTask(t)))
                        .Append(JsonShapes.ForEvm(0, project.Name, EarnedValue.ForProject(project)))
                        .ToList()
                    : [JsonShapes.ForEvm(scope.RowNumber, scope.Name, EarnedValue.ForTask(scope))]);
                return 0;
            }

            var rows = scope is null
                ? project.Tasks.Select(t => (Row: t.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Name, Data: EarnedValue.ForTask(t)))
                    .Append((Row: "", Name: project.Name, Data: EarnedValue.ForProject(project)))
                : [(Row: scope.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), scope.Name, Data: EarnedValue.ForTask(scope))];
            Render.Table(
                context.Out,
                ["ID", "Name", "BAC", "BCWS", "BCWP", "ACWP", "SV", "CV", "SPI", "CPI", "EAC", "VAC"],
                [
                    .. rows.Select(IReadOnlyList<string> (r) =>
                    [
                        r.Row,
                        r.Name,
                        Render.Num(r.Data.Bac),
                        Render.Num(r.Data.Bcws),
                        Render.Num(r.Data.Bcwp),
                        Render.Num(r.Data.Acwp),
                        Render.Num(r.Data.Sv),
                        Render.Num(r.Data.Cv),
                        r.Data.Spi is { } spi ? Render.Num(Math.Round(spi, 2)) : "",
                        r.Data.Cpi is { } cpi ? Render.Num(Math.Round(cpi, 2)) : "",
                        Render.Num(r.Data.Eac),
                        Render.Num(r.Data.Vac),
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static Command Drivers()
    {
        var refArg = new Argument<string>("task") { Description = "Task reference: row id or uid:<n>." };
        var command = new Command("drivers", "Explain what places the task where it is (inspector).") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var task = Parsers.TaskRef(project, parseResult.GetRequiredValue(refArg));
            var drivers = Core.Scheduling.TaskDrivers.Explain(task);
            if (context.Json)
            {
                context.WriteJson(drivers.Select(d => new
                {
                    kind = d.Kind.ToString(),
                    d.Description,
                    d.Binding,
                    d.Date,
                    d.PredecessorUid,
                }).ToList());
                return 0;
            }

            context.Out.WriteLine($"task {task.RowNumber} '{task.Name}': start {Render.Date(task.Start)}");
            foreach (var driver in drivers)
            {
                context.Out.WriteLine($"  {(driver.Binding ? "*" : " ")} {driver.Description}");
            }

            context.Out.WriteLine("  (* = binding)");
            return 0;
        }));
        return command;
    }
}