using System.CommandLine;
using Project27.Core;

namespace Project27.Cli;

internal static class ResourceCommands
{
    public static Command Command()
    {
        var command = new Command("resource", "Manage work, material, and cost resources.");
        command.Add(Add());
        command.Add(List());
        command.Add(Show());
        command.Add(Set());
        command.Add(Remove());
        command.Add(SetRate());
        command.Add(RemoveRate());
        return command;
    }

    private static Argument<string> ResourceArg()
        => new("resource") { Description = "Resource name (case-insensitive) or uid:<n>." };

    private static Command Add()
    {
        var nameArg = new Argument<string>("name");
        var typeOpt = new Option<string?>("--type") { HelpName = "work|material|cost", Description = "Default work." };
        var maxUnitsOpt = new Option<string?>("--max-units") { HelpName = "units", Description = "Peak availability, e.g. 200%." };
        var rateOpt = new Option<string?>("--rate") { HelpName = "rate", Description = "Standard rate, e.g. 50/h (per unit for material)." };
        var overtimeOpt = new Option<string?>("--overtime-rate") { HelpName = "rate" };
        var costPerUseOpt = new Option<string?>("--cost-per-use") { HelpName = "amount" };
        var labelOpt = new Option<string?>("--material-label") { HelpName = "label", Description = "Unit of measure for material resources." };
        var calendarOpt = new Option<string?>("--calendar") { HelpName = "name", Description = "Availability calendar (work resources)." };
        var initialsOpt = new Option<string?>("--initials");
        var groupOpt = new Option<string?>("--group");
        var command = new Command("add", "Add a resource.")
        {
            nameArg, typeOpt, maxUnitsOpt, rateOpt, overtimeOpt, costPerUseOpt, labelOpt, calendarOpt, initialsOpt, groupOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var type = parseResult.GetValue(typeOpt) is { } typeText ? Parsers.ResourceTypeInput(typeText) : ResourceType.Work;
            var resource = project.AddResource(parseResult.GetRequiredValue(nameArg), type);
            ApplyFields(
                project, resource, parseResult,
                maxUnitsOpt, rateOpt, overtimeOpt, costPerUseOpt, labelOpt, calendarOpt, initialsOpt, groupOpt, accrualOpt: null);
            store.Save(project);
            context.Report(JsonShapes.ForResource(resource), $"added {Text(type)} resource '{resource.Name}' (uid {resource.UniqueId})");
            return 0;
        }));
        return command;
    }

    private static Command List()
    {
        var command = new Command("list", "List resources.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            if (context.Json)
            {
                context.WriteJson(project.Resources.Select(JsonShapes.ForResource).ToList());
                return 0;
            }

            if (project.Resources.Count == 0)
            {
                context.Out.WriteLine("no resources");
                return 0;
            }

            Render.Table(
                context.Out,
                ["UID", "Name", "Type", "Max", "Rate", "Cost/Use", "Calendar", "Asgn"],
                [
                    .. project.Resources.Select(IReadOnlyList<string> (r) =>
                    [
                        Render.Num(r.UniqueId),
                        r.Name,
                        Text(r.Type),
                        r.Type == ResourceType.Work ? Render.Num(r.MaxUnits * 100m) + "%" : "",
                        r.Type == ResourceType.Cost ? "" : Render.RateText(r.StandardRate, r.Type),
                        r.Type == ResourceType.Cost ? "" : Render.Num(r.RateTable(CostRateTableId.A).Entries[0].CostPerUse),
                        r.Calendar?.Name ?? "",
                        Render.Num(r.Assignments.Count),
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static Command Show()
    {
        var resourceArg = ResourceArg();
        var command = new Command("show", "Print a resource's fields, rate tables, and assignments.") { resourceArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            if (context.Json)
            {
                context.WriteJson(JsonShapes.ForResource(resource));
                return 0;
            }

            var output = context.Out;
            Render.KeyValues(output,
            [
                ("uid", Render.Num(resource.UniqueId)),
                ("name", resource.Name),
                ("type", Text(resource.Type)),
                ("initials", resource.Initials ?? ""),
                ("group", resource.Group ?? ""),
                .. resource.Type == ResourceType.Work
                    ? new (string, string)[]
                    {
                        ("max units", Render.Num(resource.MaxUnits * 100m) + "%"),
                        ("calendar", resource.Calendar?.Name ?? "(task/project)"),
                    }
                    : [],
                .. resource.Type == ResourceType.Material
                    ? new (string, string)[] { ("material label", resource.MaterialLabel ?? "") }
                    : [],
                ("accrual", Text(resource.Accrual)),
            ]);
            if (resource.Type != ResourceType.Cost)
            {
                foreach (var id in Enum.GetValues<CostRateTableId>())
                {
                    var table = resource.RateTable(id);
                    var interesting = table.Entries.Count > 1
                        || !table.Entries[0].StandardRate.IsZero
                        || !table.Entries[0].OvertimeRate.IsZero
                        || table.Entries[0].CostPerUse != 0m;
                    if (!interesting && id != CostRateTableId.A)
                    {
                        continue;
                    }

                    foreach (var entry in table.Entries)
                    {
                        var from = entry.EffectiveFrom == DateTime.MinValue ? "base" : Render.Date(entry.EffectiveFrom);
                        output.WriteLine(
                            $"rates[{id}] {from}: std {Render.RateText(entry.StandardRate, resource.Type)}"
                            + $"  ovt {Render.RateText(entry.OvertimeRate, resource.Type)}"
                            + $"  per-use {Render.Num(entry.CostPerUse)}");
                    }
                }
            }

            foreach (var assignment in resource.Assignments)
            {
                output.WriteLine(
                    $"assigned: task {assignment.Task.RowNumber} '{assignment.Task.Name}'"
                    + $"  {Render.Units(assignment)}"
                    + (assignment.Resource.Type == ResourceType.Work ? $"  {Render.WorkHours(assignment.WorkMinutes)}" : "")
                    + $"  cost {Render.Num(assignment.Cost)}");
            }

            return 0;
        }));
        return command;
    }

    private static Command Set()
    {
        var resourceArg = ResourceArg();
        var nameOpt = new Option<string?>("--name");
        var maxUnitsOpt = new Option<string?>("--max-units") { HelpName = "units" };
        var rateOpt = new Option<string?>("--rate") { HelpName = "rate" };
        var overtimeOpt = new Option<string?>("--overtime-rate") { HelpName = "rate" };
        var costPerUseOpt = new Option<string?>("--cost-per-use") { HelpName = "amount" };
        var labelOpt = new Option<string?>("--material-label") { HelpName = "label" };
        var calendarOpt = new Option<string?>("--calendar") { HelpName = "name|none" };
        var initialsOpt = new Option<string?>("--initials");
        var groupOpt = new Option<string?>("--group");
        var accrualOpt = new Option<string?>("--accrual") { HelpName = "start|prorated|end" };
        var command = new Command("set", "Change resource fields.")
        {
            resourceArg, nameOpt, maxUnitsOpt, rateOpt, overtimeOpt, costPerUseOpt, labelOpt, calendarOpt, initialsOpt, groupOpt, accrualOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            if (parseResult.GetValue(nameOpt) is { } name)
            {
                resource.Name = name;
            }

            ApplyFields(project, resource, parseResult, maxUnitsOpt, rateOpt, overtimeOpt, costPerUseOpt, labelOpt, calendarOpt, initialsOpt, groupOpt, accrualOpt);
            project.Recalculate();
            store.Save(project);
            context.Report(JsonShapes.ForResource(resource), $"updated resource '{resource.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command Remove()
    {
        var resourceArg = ResourceArg();
        var command = new Command("remove", "Remove a resource (drops its assignments).") { resourceArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            project.RemoveResource(resource);
            project.Recalculate();
            store.Save(project);
            context.Report(new RemovedJson("resource", resource.Name, resource.UniqueId), $"removed resource '{resource.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command SetRate()
    {
        var resourceArg = ResourceArg();
        var fromOpt = new Option<string>("--from") { HelpName = "date", Required = true, Description = "Effective date of the entry." };
        var tableOpt = new Option<string?>("--table") { HelpName = "A..E", Description = "Rate table; default A." };
        var rateOpt = new Option<string?>("--rate") { HelpName = "rate" };
        var overtimeOpt = new Option<string?>("--overtime-rate") { HelpName = "rate" };
        var costPerUseOpt = new Option<string?>("--cost-per-use") { HelpName = "amount" };
        var command = new Command("set-rate", "Add or update an effective-dated rate entry.")
        {
            resourceArg, fromOpt, tableOpt, rateOpt, overtimeOpt, costPerUseOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            EnsureRated(resource);
            var table = parseResult.GetValue(tableOpt) is { } tableText ? Parsers.RateTableInput(tableText) : CostRateTableId.A;
            resource.RateTable(table).SetRate(
                Parsers.DateInput(parseResult.GetRequiredValue(fromOpt), project.TimeSettings, finishLike: false),
                parseResult.GetValue(rateOpt) is { } rate ? Parsers.RateInput(rate, resource.Type) : null,
                parseResult.GetValue(overtimeOpt) is { } overtime ? Parsers.RateInput(overtime, resource.Type) : null,
                parseResult.GetValue(costPerUseOpt) is { } perUse ? Parsers.MoneyInput(perUse) : null);
            project.Recalculate();
            store.Save(project);
            context.Report(JsonShapes.ForResource(resource), $"updated rate table {table} of '{resource.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command RemoveRate()
    {
        var resourceArg = ResourceArg();
        var fromOpt = new Option<string>("--from") { HelpName = "date", Required = true };
        var tableOpt = new Option<string?>("--table") { HelpName = "A..E" };
        var command = new Command("remove-rate", "Remove the rate entry effective at an exact date.") { resourceArg, fromOpt, tableOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var resource = Parsers.ResourceRef(project, parseResult.GetRequiredValue(resourceArg));
            EnsureRated(resource);
            var table = parseResult.GetValue(tableOpt) is { } tableText ? Parsers.RateTableInput(tableText) : CostRateTableId.A;
            var from = Parsers.DateInput(parseResult.GetRequiredValue(fromOpt), project.TimeSettings, finishLike: false);
            if (!resource.RateTable(table).RemoveRate(from))
            {
                throw new CliException($"no rate entry effective at {Render.Date(from)} in table {table}");
            }

            project.Recalculate();
            store.Save(project);
            context.Report(JsonShapes.ForResource(resource), $"removed rate entry from table {table} of '{resource.Name}'");
            return 0;
        }));
        return command;
    }

    private static void ApplyFields(
        Project project,
        Resource resource,
        System.CommandLine.ParseResult parseResult,
        Option<string?> maxUnitsOpt,
        Option<string?> rateOpt,
        Option<string?> overtimeOpt,
        Option<string?> costPerUseOpt,
        Option<string?> labelOpt,
        Option<string?> calendarOpt,
        Option<string?> initialsOpt,
        Option<string?> groupOpt,
        Option<string?>? accrualOpt)
    {
        if (parseResult.GetValue(maxUnitsOpt) is { } maxUnits)
        {
            if (resource.Type != ResourceType.Work)
            {
                throw new CliException($"--max-units applies to work resources; '{resource.Name}' is {Text(resource.Type)}");
            }

            resource.MaxUnits = Parsers.UnitsInput(maxUnits);
        }

        var rate = parseResult.GetValue(rateOpt);
        var overtime = parseResult.GetValue(overtimeOpt);
        var perUse = parseResult.GetValue(costPerUseOpt);
        if (rate is not null || overtime is not null || perUse is not null)
        {
            EnsureRated(resource);
            resource.RateTable(CostRateTableId.A).SetRate(
                DateTime.MinValue,
                rate is null ? null : Parsers.RateInput(rate, resource.Type),
                overtime is null ? null : Parsers.RateInput(overtime, resource.Type),
                perUse is null ? null : Parsers.MoneyInput(perUse));
        }

        if (parseResult.GetValue(labelOpt) is { } label)
        {
            if (resource.Type != ResourceType.Material)
            {
                throw new CliException($"--material-label applies to material resources; '{resource.Name}' is {Text(resource.Type)}");
            }

            resource.MaterialLabel = label;
        }

        if (parseResult.GetValue(calendarOpt) is { } calendar)
        {
            resource.Calendar = string.Equals(calendar.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : Parsers.CalendarByName(project, calendar);
        }

        if (parseResult.GetValue(initialsOpt) is { } initials)
        {
            resource.Initials = initials;
        }

        if (parseResult.GetValue(groupOpt) is { } group)
        {
            resource.Group = group;
        }

        if (accrualOpt is not null && parseResult.GetValue(accrualOpt) is { } accrual)
        {
            resource.Accrual = Parsers.AccrualInput(accrual);
        }
    }

    private static void EnsureRated(Resource resource)
    {
        if (resource.Type == ResourceType.Cost)
        {
            throw new CliException($"cost resource '{resource.Name}' has no rates; set the amount per assignment (assign … --cost)");
        }
    }

    internal static string Text(ResourceType type) => type switch
    {
        ResourceType.Work => "work",
        ResourceType.Material => "material",
        ResourceType.Cost => "cost",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string Text(CostAccrual accrual) => accrual switch
    {
        CostAccrual.Start => "start",
        CostAccrual.Prorated => "prorated",
        CostAccrual.End => "end",
        _ => throw new ArgumentOutOfRangeException(nameof(accrual)),
    };
}
