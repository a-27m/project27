using System.CommandLine;
using Project27.Cli.Completion;
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
        command.Add(P27In());
        return command;
    }

    private static Command Csv()
    {
        var outOpt = new Option<string?>("--out", "-o") { HelpName = "file.csv", Description = "Output path; default <project>.csv." }
            .SuggestsPaths();
        var tableOpt = new Option<string?>("--table") { HelpName = "entry|…" }.Suggests(CompletionValues.Tables);
        var fieldsOpt = new Option<string?>("--fields") { HelpName = "keys" }
            .Suggests(CompletionValues.CommaList(CompletionValues.Fields));
        var filterOpt = new Option<string?>("--filter") { HelpName = "expr" };
        var sortOpt = new Option<string?>("--sort") { HelpName = "keys" };
        var groupByOpt = new Option<string?>("--group-by") { HelpName = "field" }.Suggests(CompletionValues.Fields);
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
        var outOpt = new Option<string?>("--out", "-o") { HelpName = "file.xml", Description = "Output path; default <project>.xml." }
            .SuggestsPaths();
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
        var fileArg = new Argument<string>("xml") { Description = "MSPDI XML file to import." }.SuggestsPaths();
        var outOpt = new Option<string?>("--file") { HelpName = "new.p27", Description = "Target project file; default <name>.p27. Local mode only — errors with --server." }
            .SuggestsPaths(CompletionDirective.ProjectFiles);
        var command = new Command("mspdi", "Create a new .p27 from Microsoft Project XML, or import into the server.") { fileArg, outOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var source = parseResult.GetRequiredValue(fileArg);
            if (!File.Exists(source))
            {
                throw new CliException($"'{source}' not found");
            }

            var xml = File.ReadAllText(source);

            if (context.IsRemote)
            {
                if (parseResult.GetValue(outOpt) is not null)
                {
                    throw new CliException("--file only applies in local mode; a remote import always creates a new server project");
                }

                using var client = context.CreateRemoteClient();
                var imported = client.ImportMspdi(xml);
                context.Report(imported, $"imported into project '{imported.Name}' ({imported.Id:D})");
                return 0;
            }

            var project = MspdiReader.Read(xml);
            var path = parseResult.GetValue(outOpt) ?? SafeName(project.Name) + SqliteProjectStore.FileExtension;
            SqliteProjectStore.Create(path, project);
            context.Report(
                new { imported = project.Tasks.Count, resources = project.Resources.Count, path },
                $"imported {Render.Num(project.Tasks.Count)} task(s), {Render.Num(project.Resources.Count)} resource(s) into {path}");
            return 0;
        }));
        return command;
    }

    private static Command P27In()
    {
        var fileArg = new Argument<string>("p27") { Description = ".p27 file to import." };
        var command = new Command("p27", "Import a .p27 project file into the server (server mode only).") { fileArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            if (!context.IsRemote)
            {
                throw new CliException("p27 import only works in --server mode");
            }

            var source = parseResult.GetRequiredValue(fileArg);
            if (!File.Exists(source))
            {
                throw new CliException($"'{source}' not found");
            }

            using var fileStream = File.OpenRead(source);
            using var client = context.CreateRemoteClient();
            var imported = client.ImportP27(fileStream);
            context.Report(imported, $"imported into project '{imported.Name}' ({imported.Id:D})");
            return 0;
        }));
        return command;
    }

    private static string SafeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c)).ToLowerInvariant();
}
