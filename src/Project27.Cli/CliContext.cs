using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Core;
using Project27.Storage;

namespace Project27.Cli;

/// <summary>Per-invocation state: resolved project file, output writers, output mode.</summary>
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

    public string? ExplicitFile => result.GetValue(CliRoot.FileOption);

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

    public (SqliteProjectStore Store, Project Project) OpenProject()
    {
        var store = SqliteProjectStore.Open(ResolveFile());
        return (store, store.Load());
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
