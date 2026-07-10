using System.CommandLine;

namespace Project27.Cli;

/// <summary>Assembles the `p27` command tree (see docs/spec/03-persistence-cli.md §2.3).</summary>
internal static class CliRoot
{
    internal static readonly Option<string?> FileOption = new("--file", "-f")
    {
        Description = "Project file (default: the single *.p27 in the current directory).",
        HelpName = "path",
        Recursive = true,
    };

    internal static readonly Option<bool> JsonOption = new("--json")
    {
        Description = "Machine-readable JSON output.",
        Recursive = true,
    };

    internal static readonly Option<string?> ServerOption = new("--server")
    {
        Description = "Server base URL (or P27_SERVER); switches from local files to the REST API.",
        HelpName = "url",
        Recursive = true,
    };

    internal static readonly Option<string?> ProjectOption = new("--project", "-p")
    {
        Description = "Server project by name or id (server mode).",
        HelpName = "name|id",
        Recursive = true,
    };

    internal static readonly Option<string?> TokenOption = new("--token")
    {
        Description = "Bearer token for the server (or P27_TOKEN).",
        HelpName = "jwt",
        Recursive = true,
    };

    internal static readonly Option<string?> DevUserOption = new("--dev-user")
    {
        Description = "DevAuth user for a Development server.",
        HelpName = "id",
        Recursive = true,
    };

    public static RootCommand Build()
    {
        var root = new RootCommand("Project27 command-line client: plan, schedule, and inspect .p27 project files.");
        root.Add(FileOption);
        root.Add(JsonOption);
        root.Add(ServerOption);
        root.Add(ProjectOption);
        root.Add(TokenOption);
        root.Add(DevUserOption);
        root.Add(ProjectCommands.Init());
        root.Add(ProjectCommands.Project());
        root.Add(ProjectCommands.Schedule());
        root.Add(TaskCommands.Command());
        root.Add(LinkCommands.Command());
        root.Add(CalendarCommands.Command());
        root.Add(ResourceCommands.Command());
        root.Add(AssignCommands.Command());
        root.Add(ServerCommands.Checkout());
        root.Add(ServerCommands.Checkin());
        root.Add(ServerCommands.Unlock());
        return root;
    }

    /// <summary>
    /// Runs a verb action with uniform error reporting: engine and input violations become
    /// `error: …` on stderr with exit code 1; anything else is a bug and propagates.
    /// </summary>
    public static int Run(ParseResult result, Func<CliContext, int> action)
    {
        var context = new CliContext(result);
        try
        {
            return action(context);
        }
        catch (Exception exception) when (exception
            is CliException
            or FormatException
            or ArgumentException
            or InvalidOperationException
            or IOException
            or InvalidDataException
            or NotSupportedException)
        {
            context.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }
}
