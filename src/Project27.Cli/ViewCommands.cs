using System.CommandLine;
using Project27.Cli.Completion;
using Project27.Core.Fields;
using Project27.Core.Views;

namespace Project27.Cli;

internal sealed record ViewRowJson(int Uid, int Id, Dictionary<string, object?> Values);

internal sealed record ViewGroupJson(string? Heading, IReadOnlyList<ViewRowJson> Rows);

internal sealed record ViewJson(
    IReadOnlyList<FieldJson> Fields,
    IReadOnlyList<ViewGroupJson> Groups);

internal sealed record FieldJson(string Key, string Caption, string Kind);

internal static class ViewCommands
{
    public static Command View()
    {
        var tableOpt = new Option<string?>("--table")
        {
            HelpName = string.Join("|", TaskView.Tables.Keys),
            Description = "Named field selection; default entry.",
        }.Suggests(CompletionValues.Tables);
        var fieldsOpt = new Option<string?>("--fields") { HelpName = "keys", Description = "Comma-separated field keys (see `field list`)." }
            .Suggests(CompletionValues.CommaList(CompletionValues.Fields));
        var filterOpt = new Option<string?>("--filter") { HelpName = "expr", Description = "e.g. \"critical = true and cost > 1000\"." };
        var sortOpt = new Option<string?>("--sort") { HelpName = "keys", Description = "e.g. \"finish desc,name\". Flattens the outline." };
        var groupByOpt = new Option<string?>("--group-by") { HelpName = "field", Description = "Group rows by a field. Flattens the outline." }
            .Suggests(CompletionValues.Fields);
        var command = new Command("view", "Tabular task views: tables, filters, sorts, groups.")
        {
            tableOpt, fieldsOpt, filterOpt, sortOpt, groupByOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var settings = project.TimeSettings;

            IReadOnlyList<string> fields;
            if (parseResult.GetValue(fieldsOpt) is { } fieldList)
            {
                fields = [.. fieldList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
            else
            {
                var tableName = parseResult.GetValue(tableOpt) ?? "entry";
                fields = TaskView.Tables.TryGetValue(tableName, out var tableFields)
                    ? tableFields
                    : throw new CliException($"unknown table '{tableName}'; use {string.Join(", ", TaskView.Tables.Keys)}");
            }

            ViewDefinition definition;
            try
            {
                definition = new ViewDefinition(
                    fields,
                    parseResult.GetValue(filterOpt) is { } filter ? FilterParser.Parse(project, filter) : null,
                    parseResult.GetValue(sortOpt) is { } sort ? TaskView.ParseSorts(sort) : null,
                    parseResult.GetValue(groupByOpt));
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException)
            {
                throw new CliException(exception.Message, exception);
            }

            ViewResult result;
            try
            {
                result = TaskView.Evaluate(project, definition);
            }
            catch (KeyNotFoundException exception)
            {
                throw new CliException(exception.Message, exception);
            }

            if (context.Json)
            {
                context.WriteJson(new ViewJson(
                    [.. result.Fields.Select(f => new FieldJson(f.Key, f.Caption, f.Kind.ToString()))],
                    [
                        .. result.Groups.Select(g => new ViewGroupJson(
                            g.Heading,
                            [
                                .. g.Rows.Select(r => new ViewRowJson(
                                    r.Task.UniqueId,
                                    r.Task.RowNumber,
                                    r.Cells.ToDictionary(c => c.Field, c => c.Raw))),
                            ])),
                    ]));
                return 0;
            }

            var any = false;
            foreach (var group in result.Groups)
            {
                if (group.Heading is { } heading)
                {
                    context.Out.WriteLine();
                    context.Out.WriteLine(heading);
                }

                if (group.Rows.Count == 0)
                {
                    continue;
                }

                any = true;
                Render.Table(
                    context.Out,
                    [.. result.Fields.Select(f => f.Caption)],
                    [
                        .. group.Rows.Select(IReadOnlyList<string> (row) =>
                        [
                            .. row.Cells.Select((cell, index) =>
                                result.Fields[index].Key == "name" && definition.GroupBy is null && definition.Sorts is null or { Count: 0 }
                                    ? new string(' ', 2 * row.Task.OutlineLevel) + cell.Text
                                    : cell.Text),
                        ]),
                    ]);
            }

            if (!any)
            {
                context.Out.WriteLine("no matching tasks");
            }

            return 0;
        }));
        return command;
    }

    public static Command FieldList()
    {
        var list = new Command("list", "List every field key available to views and filters.");
        list.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var fields = FieldCatalog.All.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
            if (context.Json)
            {
                context.WriteJson(fields.Select(f => new FieldJson(f.Key, f.Caption, f.Kind.ToString())).ToList());
                return 0;
            }

            Render.Table(
                context.Out,
                ["Key", "Caption", "Kind"],
                [.. fields.Select(IReadOnlyList<string> (f) => [f.Key, f.Caption, f.Kind.ToString()])]);
            return 0;
        }));
        return new Command("field", "Field catalog.") { list };
    }
}
