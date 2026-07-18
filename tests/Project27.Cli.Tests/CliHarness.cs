using Xunit;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;

namespace Project27.Cli.Tests;

internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public JsonElement Json() => JsonDocument.Parse(Stdout).RootElement.Clone();
}

/// <summary>Runs the command tree in-process with captured output.</summary>
internal static class Cli
{
    static Cli()
    {
        // Tests must be immune to the developer's ambient P27_* configuration —
        // P27_SERVER alone silently flips every file-mode test into server mode.
        foreach (var name in (string[])["P27_FILE", "P27_SERVER", "P27_PROJECT", "P27_TOKEN"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public static CliResult Run(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = CliRoot.Build().Parse(args).Invoke(new InvocationConfiguration
        {
            Output = output,
            Error = error,
        });
        return new(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Runs and asserts success.</summary>
    public static CliResult Ok(params string[] args)
    {
        var result = Run(args);
        Assert.Equal("", result.Stderr);
        Assert.Equal(0, result.ExitCode);
        return result;
    }

    /// <summary>Runs and asserts failure with an `error:` message.</summary>
    public static CliResult Fail(params string[] args)
    {
        var result = Run(args);
        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("error:", result.Stderr, StringComparison.Ordinal);
        return result;
    }
}

/// <summary>Per-test scratch directory; every test file passes explicit --file paths.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("p27-cli-tests").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS temp dir is purged eventually anyway.
        }
    }
}
