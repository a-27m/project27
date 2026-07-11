using System.CommandLine;
using Project27.Core.Views;
using Project27.Interop;
using Project27.Storage;

namespace Project27.Cli;

internal static class InteropCommands
{
    public static Command Export()
    {
        var command = new Command("export", "Export the project to interchange formats.");
        command.Add(Csv());
        command.Add(MspdiOut());
        return command;
    }

    public static Command Import()
    {
        var command = new Command("import", "Import from interchange formats.");
        command.Add(MspdiIn());
        return command;
    }

    private static Command Csv()
    {
        var outOpt = new Option<string?>("--out", "-o") { HelpName = "file.csv", Description = "Output path; default <project>.csv." };
        var tableOpt = new Option<string?>("--table") { HelpName = "entry|…" };
        var fieldsOpt = new Option<string?>("--fields") { HelpName = "keys" };
        var filterOpt = new Option<string?>("--filter") { HelpName = "expr" };
        var sortOpt = new Option<string?>("--sort") { HelpName = "keys" };
        var groupByOpt = new Option<string?>("--group-by") { HelpName = "field" };
        var command = new Command("csv", "Any task view as RFC-4180 CSV.") { outOpt, tableOpt, fieldsOpt, filterOpt, sortOpt, groupByOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            try
            {
                IReadOnlyList<string> fields = parseResult.GetValue(fieldsOpt) is { } fieldList
                    ? [.. fieldList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                    : CsvExporter.FieldsOf(parseResult.GetValue(tableOpt));
                var definition = new ViewDefinition(
                    fields,
                    parseResult.GetValue(filterOpt) is { } filter ? FilterParser.Parse(project, filter) : null,
                    parseResult.GetValue(sortOpt) is { } sort ? TaskView.ParseSorts(sort) : null,
                    parseResult.GetValue(groupByOpt));
                var csv = CsvExporter.Write(project, definition);
                var path = parseResult.GetValue(outOpt) ?? SafeName(project.Name) + ".csv";
                File.WriteAllText(path, csv);
                context.Report(new { format = "csv", path }, $"wrote {path}");
                return 0;
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException)
            {
                throw new CliException(exception.Message, exception);
            }
        }));
        return command;
    }

    private static Command MspdiOut()
    {
        var outOpt = new Option<string?>("--out", "-o") { HelpName = "file.xml", Description = "Output path; default <project>.xml." };
        var command = new Command("mspdi", "Microsoft Project Data Interchange XML.") { outOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var path = parseResult.GetValue(outOpt) ?? SafeName(project.Name) + ".xml";
            File.WriteAllText(path, MspdiWriter.Write(project));
            context.Report(new { format = "mspdi", path }, $"wrote {path}");
            return 0;
        }));
        return command;
    }

    private static Command MspdiIn()
    {
        var fileArg = new Argument<string>("xml") { Description = "MSPDI XML file to import." };
        var outOpt = new Option<string?>("--file") { HelpName = "new.p27", Description = "Target project file; default <name>.p27." };
        var command = new Command("mspdi", "Create a new .p27 from Microsoft Project XML.") { fileArg, outOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            if (context.IsRemote)
            {
                throw new CliException("import creates local files; run it without --server");
            }

            var source = parseResult.GetRequiredValue(fileArg);
            if (!File.Exists(source))
            {
                throw new CliException($"'{source}' not found");
            }

            var project = MspdiReader.Read(File.ReadAllText(source));
            var path = parseResult.GetValue(outOpt) ?? SafeName(project.Name) + SqliteProjectStore.FileExtension;
            SqliteProjectStore.Create(path, project);
            context.Report(
                new { imported = project.Tasks.Count, resources = project.Resources.Count, path },
                $"imported {Render.Num(project.Tasks.Count)} task(s), {Render.Num(project.Resources.Count)} resource(s) into {path}");
            return 0;
        }));
        return command;
    }

    private static string SafeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c)).ToLowerInvariant();
}
