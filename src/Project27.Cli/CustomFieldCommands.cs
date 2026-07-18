using System.CommandLine;
using Project27.Cli.Completion;
using Project27.Core;
using Project27.Core.Fields;
using Project27.Core.Views;

namespace Project27.Cli;

internal sealed record CustomFieldJson(
    string Id,
    string Kind,
    string? Alias,
    string? Formula,
    IReadOnlyList<string> Indicators);

internal static class CustomFieldCommands
{
    public static Command Command()
    {
        var command = new Command("customfield", "Custom field slots: aliases, formulas, indicators.");
        command.Add(Define());
        command.Add(List());
        command.Add(Remove());
        return command;
    }

    private static Command Define()
    {
        var idArg = new Argument<string>("slot") { Description = "Slot id: text1..text30, number1..number20, cost1..cost10, date1..date10, flag1..flag20, duration1..duration10." }
            .Suggests(CompletionValues.CustomFieldSlots);
        var aliasOpt = new Option<string?>("--alias") { HelpName = "name", Description = "Unique display name usable as a field key." };
        var formulaOpt = new Option<string?>("--formula") { HelpName = "expr", Description = "e.g. \"IIf([totalSlack] < 1d, 100, 0)\"." };
        var indicatorOpt = new Option<string[]>("--indicator")
        {
            HelpName = "rule",
            Description = "\"when >= 100 then red-flag\"; repeatable, first match wins.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command("define", "Define or redefine a custom field slot.") { idArg, aliasOpt, formulaOpt, indicatorOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var slot = parseResult.GetRequiredValue(idArg);
            var kind = CustomFieldDefinition.KindOfSlot(slot);
            var rules = (parseResult.GetValue(indicatorOpt) ?? [])
                .Select(rule => ParseIndicator(rule, kind, project))
                .ToList();
            var definition = project.DefineCustomField(
                slot,
                parseResult.GetValue(aliasOpt),
                parseResult.GetValue(formulaOpt),
                rules);
            store.Save(project);
            context.Report(ToJson(definition), $"defined {definition.Id} ({definition.Kind})"
                + (definition.Alias is { } alias ? $" as '{alias}'" : ""));
            return 0;
        }));
        return command;
    }

    private static Command List()
    {
        var command = new Command("list", "List custom field definitions.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var fields = project.CustomFields.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
            if (context.Json)
            {
                context.WriteJson(fields.Select(ToJson).ToList());
                return 0;
            }

            if (fields.Count == 0)
            {
                context.Out.WriteLine("no custom fields");
                return 0;
            }

            Render.Table(
                context.Out,
                ["Slot", "Kind", "Alias", "Formula", "Indicators"],
                [
                    .. fields.Select(IReadOnlyList<string> (f) =>
                    [
                        f.Id,
                        f.Kind.ToString(),
                        f.Alias ?? "",
                        f.Formula ?? "",
                        string.Join("; ", f.Indicators.Select(IndicatorText)),
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static Command Remove()
    {
        var idArg = new Argument<string>("field") { Description = "Slot id or alias." }.Suggests(CompletionValues.DefinedCustomFields);
        var command = new Command("remove", "Remove a definition and its stored values.") { idArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var reference = parseResult.GetRequiredValue(idArg);
            var definition = project.FindCustomField(reference)
                ?? throw new CliException($"no custom field '{reference}'");
            project.RemoveCustomField(reference);
            store.Save(project);
            context.Report(new RemovedJson("customfield", definition.Id), $"removed {definition.Id}");
            return 0;
        }));
        return command;
    }

    /// <summary>Parses "when &lt;op&gt; &lt;value&gt; then &lt;icon&gt;".</summary>
    internal static IndicatorRule ParseIndicator(string rule, FieldKind kind, Project project)
    {
        var tokens = rule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var thenIndex = Array.FindIndex(tokens, t => t.Equals("then", StringComparison.OrdinalIgnoreCase));
        if (tokens.Length < 5 - 1 || !tokens[0].Equals("when", StringComparison.OrdinalIgnoreCase)
            || thenIndex < 3 || thenIndex != tokens.Length - 2)
        {
            throw new CliException($"invalid indicator '{rule}'; use \"when >= 100 then red-flag\"");
        }

        var op = tokens[1] switch
        {
            "=" or "==" => FilterOperator.Equals,
            "!=" or "<>" => FilterOperator.NotEquals,
            ">" => FilterOperator.GreaterThan,
            ">=" => FilterOperator.GreaterOrEqual,
            "<" => FilterOperator.LessThan,
            "<=" => FilterOperator.LessOrEqual,
            "~" => FilterOperator.Contains,
            var other => throw new CliException($"invalid indicator operator '{other}'"),
        };
        var valueText = string.Join(' ', tokens[2..thenIndex]).Trim('"', '\'');
        object value;
        if (op == FilterOperator.Contains)
        {
            value = valueText;
        }
        else
        {
            try
            {
                value = FieldCatalog.ParseLiteral(kind, valueText, project.TimeSettings);
            }
            catch (FormatException exception)
            {
                throw new CliException(exception.Message, exception);
            }
        }

        return new IndicatorRule(op, value, tokens[^1]);
    }

    private static string IndicatorText(IndicatorRule rule)
    {
        var op = rule.Operator switch
        {
            FilterOperator.Equals => "=",
            FilterOperator.NotEquals => "!=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterOrEqual => ">=",
            FilterOperator.LessThan => "<",
            FilterOperator.LessOrEqual => "<=",
            _ => "~",
        };
        return $"when {op} {rule.Value} then {rule.Icon}";
    }

    private static CustomFieldJson ToJson(CustomFieldDefinition definition) => new(
        definition.Id,
        definition.Kind.ToString(),
        definition.Alias,
        definition.Formula,
        [.. definition.Indicators.Select(IndicatorText)]);
}
