using System.CommandLine;
using Project27.Core.Reports;

namespace Project27.Cli;

internal static class ReportCommands
{
    public static Command Command()
    {
        var command = new Command("report", "Generate self-contained HTML reports.");
        command.Add(List());
        foreach (var (name, title) in ReportBuilder.Available)
        {
            command.Add(One(name, title));
        }

        return command;
    }

    private static Command List()
    {
        var command = new Command("list", "List the available reports.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            if (context.Json)
            {
                context.WriteJson(ReportBuilder.Available.Select(r => new { name = r.Name, title = r.Title }).ToList());
                return 0;
            }

            Render.Table(
                context.Out,
                ["Name", "Title"],
                [.. ReportBuilder.Available.Select(IReadOnlyList<string> (r) => [r.Name, r.Title])]);
            return 0;
        }));
        return command;
    }

    private static Command One(string name, string title)
    {
        var outOpt = new Option<string?>("--out", "-o")
        {
            HelpName = "file.html",
            Description = "Output path; default <project>-<report>.html in the current directory.",
        };
        var command = new Command(name, title + ".") { outOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var html = ReportBuilder.Render(project, name);
            var path = parseResult.GetValue(outOpt)
                ?? SafeFileName(project.Name) + "-" + name + ".html";
            File.WriteAllText(path, html);
            context.Report(new { report = name, path }, $"wrote {path}");
            return 0;
        }));
        return command;
    }

    private static string SafeFileName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c)).ToLowerInvariant();
}
