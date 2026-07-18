using System.CommandLine;
using Project27.Cli.Completion;
using Project27.Cli;

namespace Project27.Cli;

/// <summary>Server-only verbs: project lifecycle on a server, and explicit lock control.</summary>
internal static class ServerCommands
{
    public static Command ProjectList()
    {
        var command = new Command("list", "List projects on the server (server mode).");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var projects = client.ListProjects();
            if (context.Json)
            {
                context.WriteJson(projects);
                return 0;
            }

            if (projects.Count == 0)
            {
                context.Out.WriteLine("no projects");
                return 0;
            }

            Render.Table(
                context.Out,
                ["Name", "Id", "Version", "Role", "Lock"],
                [
                    .. projects.Select(IReadOnlyList<string> (p) =>
                    [
                        p.Name,
                        p.Id.ToString("D"),
                        Render.Num(p.Version),
                        p.Role,
                        p.Lock is { } held ? held.UserId + (held.Stale ? " (stale)" : "") : "",
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    public static Command ProjectCreate()
    {
        var nameArg = new Argument<string>("name");
        var startOpt = new Option<string?>("--start") { HelpName = "date" };
        var command = new Command("create", "Create a project on the server (server mode).") { nameArg, startOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var start = parseResult.GetValue(startOpt) is { } text
                ? Parsers.DateInput(text, new Core.Time.TimeSettings(), finishLike: false)
                : (DateTime?)null;
            var created = client.CreateProject(parseResult.GetRequiredValue(nameArg), start);
            context.Report(created, $"created project '{created.Name}' ({created.Id:D}) at version {created.Version}");
            return 0;
        }));
        return command;
    }

    public static Command ProjectDelete()
    {
        var refArg = new Argument<string>("project") { Description = "Project name or id." }.Suggests(CompletionValues.Projects);
        var command = new Command("delete", "Delete a project on the server (owner only).") { refArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var info = client.Resolve(parseResult.GetRequiredValue(refArg));
            client.DeleteProject(info.Id);
            context.Report(new RemovedJson("project", info.Name), $"deleted project '{info.Name}'");
            return 0;
        }));
        return command;
    }

    public static Command Checkout()
    {
        var command = new Command("checkout", "Take the project's edit lock and hold it across commands (server mode).");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var info = client.Resolve(context.RequireProjectRef());
            var checkout = client.Checkout(info.Id);
            context.Report(checkout, $"checked out '{info.Name}' at version {checkout.Version}");
            return 0;
        }));
        return command;
    }

    public static Command Checkin()
    {
        var command = new Command("checkin", "Release the project's edit lock (server mode).");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var info = client.Resolve(context.RequireProjectRef());
            client.Unlock(info.Id);
            context.Report(new { released = true }, $"released the lock on '{info.Name}'");
            return 0;
        }));
        return command;
    }

    public static Command Unlock()
    {
        var command = new Command("unlock", "Release the project's lock — own, stale, or (as owner) anyone's (server mode).");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            using var client = context.CreateRemoteClient();
            var info = client.Resolve(context.RequireProjectRef());
            client.Unlock(info.Id);
            context.Report(new { released = true }, $"unlocked '{info.Name}'");
            return 0;
        }));
        return command;
    }
}
