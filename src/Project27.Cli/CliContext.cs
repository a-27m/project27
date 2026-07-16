using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Cli.Auth;
using Project27.Core;
using Project27.Core.Persistence;
using Project27.Storage;

namespace Project27.Cli;

/// <summary>Where a loaded project goes back to: a local `.p27` file or a server check-in.</summary>
internal interface IProjectSession
{
    public void Save(Project project);
}

/// <summary>Per-invocation state: resolved project source, output writers, output mode.</summary>
internal sealed class CliContext(ParseResult result)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public TextWriter Out => result.InvocationConfiguration.Output;

    public TextWriter Error => result.InvocationConfiguration.Error;

    public bool Json => result.GetValue(CliRoot.JsonOption);

    public string? ExplicitFile => result.GetValue(CliRoot.FileOption) ?? Environment.GetEnvironmentVariable("P27_FILE");

    public string ResolveFile() => ResolveFile(ExplicitFile, Environment.CurrentDirectory);

    /// <summary>Explicit path wins; otherwise exactly one `.p27` in <paramref name="directory"/>.</summary>
    internal static string ResolveFile(string? explicitPath, string directory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        // GetFiles' 3-character-extension pattern also matches longer extensions; filter exact.
        var candidates = Directory.GetFiles(directory, "*" + SqliteProjectStore.FileExtension)
            .Where(f => f.EndsWith(SqliteProjectStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new CliException($"no {SqliteProjectStore.FileExtension} file in the current directory; use --file"),
            _ => throw new CliException($"several {SqliteProjectStore.FileExtension} files in the current directory; use --file"),
        };
    }

    public string? ServerUrl
        => result.GetValue(CliRoot.ServerOption) ?? Environment.GetEnvironmentVariable("P27_SERVER");

    public bool IsRemote => !string.IsNullOrWhiteSpace(ServerUrl);

    public RemoteClient CreateRemoteClient()
    {
        if (!IsRemote)
        {
            throw new CliException("this command needs a server; pass --server <url> or set P27_SERVER");
        }

        var explicit_token = result.GetValue(CliRoot.TokenOption) ?? Environment.GetEnvironmentVariable("P27_TOKEN");
        var explicit_dev_user = result.GetValue(CliRoot.DevUserOption);

        if (!string.IsNullOrEmpty(explicit_token))
        {
            return new RemoteClient(ServerUrl!, explicit_token, null);
        }

        if (!string.IsNullOrEmpty(explicit_dev_user))
        {
            return new RemoteClient(ServerUrl!, null, explicit_dev_user);
        }

        var stored = CredentialStore.Get(ServerUrl!);
        if (stored != null)
        {
            var token = ResolveOrRefreshToken(stored);
            return new RemoteClient(ServerUrl!, token, null);
        }

        return new RemoteClient(ServerUrl!, null, null);
    }

    private static string ResolveOrRefreshToken(StoredCredential stored)
    {
        var now = DateTimeOffset.UtcNow;
        if (stored.ExpiresAt > now.AddSeconds(60))
        {
            return stored.AccessToken;
        }

        if (string.IsNullOrEmpty(stored.RefreshToken))
        {
            throw new CliException($"your login for {stored.ServerUrl} has expired; run: p27 login --server {stored.ServerUrl}");
        }

        try
        {
            var login = new OidcLogin(stored.Authority, stored.CliClientId, stored.Scopes, CliConfig.CliLoopbackPort);
            var result = login.Refresh(stored);
            var updated = stored with
            {
                AccessToken = result.AccessToken,
                IdToken = result.IdToken,
                RefreshToken = result.RefreshToken,
                ExpiresAt = result.ExpiresAt,
            };
            CredentialStore.Put(updated);
            return result.AccessToken;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CliException($"token refresh failed for {stored.ServerUrl}: {ex.Message}", ex);
        }
    }

    public string RequireProjectRef()
        => result.GetValue(CliRoot.ProjectOption) ?? Environment.GetEnvironmentVariable("P27_PROJECT")
            ?? throw new CliException("server mode needs --project <name|id>");

    public (IProjectSession Store, Project Project) OpenProject()
    {
        if (IsRemote)
        {
            var client = CreateRemoteClient();
            var info = client.Resolve(RequireProjectRef());
            var (json, version) = client.GetDocument(info.Id);
            var project = ProjectDocumentMapper.FromDocument(ProjectDocumentSerializer.Deserialize(json));
            project.Recalculate();
            return (new RemoteSession(client, info.Id, version), project);
        }

        var store = SqliteProjectStore.Open(ResolveFile());
        return (new LocalSession(store), store.Load());
    }

    private sealed class LocalSession(SqliteProjectStore store) : IProjectSession
    {
        public void Save(Project project) => store.Save(project);
    }

    /// <summary>
    /// Check-in on save: acquires (or refreshes) the lock, verifies the version read
    /// is still current, PUTs the document. A lock that existed before this
    /// invocation (explicit `p27 checkout`) is kept; a lock acquired here is released.
    /// </summary>
    private sealed class RemoteSession(RemoteClient client, Guid projectId, int loadedVersion) : IProjectSession
    {
        public void Save(Project project)
        {
            var checkout = client.Checkout(projectId);
            if (checkout.Version != loadedVersion)
            {
                client.Unlock(projectId);
                throw new CliException(
                    $"the project changed on the server (version {checkout.Version}, was {loadedVersion}); re-run the command");
            }

            var preExistingLock = checkout.Lock.AcquiredAt != checkout.Lock.RefreshedAt;
            var json = ProjectDocumentSerializer.Serialize(ProjectDocumentMapper.ToDocument(project));
            client.Checkin(projectId, loadedVersion, json, keepLock: preExistingLock);
        }
    }

    public void WriteJson(object value) => Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>Mutation outcome: the affected entity in JSON mode, a one-liner otherwise.</summary>
    public void Report(object jsonValue, string humanMessage)
    {
        if (Json)
        {
            WriteJson(jsonValue);
        }
        else
        {
            Out.WriteLine(humanMessage);
        }
    }
}
