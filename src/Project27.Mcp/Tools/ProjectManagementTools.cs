using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class ProjectManagementTools(ProjectSessionHost host)
{
    [McpServerTool(Name = "create_project"), Description(
        "Creates a brand-new project and binds this session to it. Only usable when no project is open yet — this " +
        "server operates on one project per session; restart p27-mcp to work with a different one afterward. In " +
        "local mode, `path` defaults to \"<name>.p27\" in the server's working directory; in remote mode `path` is " +
        "ignored.")]
    public async Task<string> CreateProject(
        string name,
        [Description("Project start date/time; defaults to today at 08:00.")] DateTime? start = null,
        [Description("Local mode only: the .p27 file path to create (relative paths resolve against the server's working directory).")]
        string? path = null,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await host.CreateProjectAsync(name, start, path, cancellationToken), ReadTools.JsonOptions);

    [McpServerTool(Name = "open_project"), Description(
        "Attaches this session to an existing project. Only usable when no project is open yet. `reference` is a " +
        ".p27 file path in local mode (relative paths resolve against the server's working directory), or a " +
        "project name/id in remote mode.")]
    public async Task<string> OpenProject(string reference, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await host.OpenProjectAsync(reference, cancellationToken), ReadTools.JsonOptions);
}
