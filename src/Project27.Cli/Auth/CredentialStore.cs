using System.Text.Json;
using System.Text.Json.Serialization;

namespace Project27.Cli.Auth;

internal sealed class CredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static string? RootDirectoryOverride { get; set; }

    private static string GetRootDirectory()
    {
        if (RootDirectoryOverride is { } overrideDir)
        {
            return overrideDir;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            return Path.Combine(appData, "p27");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            return Path.Combine(userProfile, ".p27");
        }

        throw new InvalidOperationException("Cannot determine credential store directory (neither APPDATA nor HOME is set).");
    }

    private static string GetStorePath() => Path.Combine(GetRootDirectory(), "credentials.json");

    internal static Dictionary<string, StoredCredential> Load()
    {
        var path = GetStorePath();
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        var creds = JsonSerializer.Deserialize<Dictionary<string, StoredCredential>>(json, JsonOptions) ?? [];
        return creds;
    }

    internal static void Save(IReadOnlyDictionary<string, StoredCredential> credentials)
    {
        var rootDir = GetRootDirectory();
        if (!Directory.Exists(rootDir))
        {
            var dirInfo = Directory.CreateDirectory(rootDir);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(rootDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // Ignore if chmod fails (e.g., filesystem doesn't support it)
                }
            }
        }

        var path = GetStorePath();
        var json = JsonSerializer.Serialize(credentials, JsonOptions);
        if (!OperatingSystem.IsWindows())
        {
            // Set the restrictive mode at creation time (not after) so the file is never
            // briefly readable under the process umask before permissions are tightened.
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };
            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.Write(json);
        }
        else
        {
            File.WriteAllText(path, json);
        }
    }

    internal static StoredCredential? Get(string serverUrl)
    {
        var normalized = NormalizeUrl(serverUrl);
        var creds = Load();
        return creds.TryGetValue(normalized, out var cred) ? cred : null;
    }

    internal static void Put(StoredCredential credential)
    {
        var normalized = NormalizeUrl(credential.ServerUrl);
        var creds = Load();
        creds[normalized] = credential with { ServerUrl = normalized };
        Save(creds);
    }

    internal static void Remove(string serverUrl)
    {
        var normalized = NormalizeUrl(serverUrl);
        var creds = Load();
        creds.Remove(normalized);
        Save(creds);
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return trimmed;
    }
}
