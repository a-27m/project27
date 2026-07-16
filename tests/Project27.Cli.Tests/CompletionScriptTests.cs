using System.Diagnostics;
using Project27.Cli.Completion;
using Xunit;

namespace Project27.Cli.Tests;

/// <summary>
/// Drives the generated bash script in a real bash, the way the shell does: set
/// COMP_WORDS/COMP_CWORD, call the completion function, read back COMPREPLY.
///
/// The C# side is covered by <see cref="CompletionTests"/>; the bugs that actually
/// shipped were in the shell glue (a directive lost to a process-substitution subshell,
/// a triggerless fzf TAB coming back empty), and only running a shell finds those.
/// </summary>
public sealed class CompletionScriptTests : IDisposable
{
    private readonly TempDir _directory = new();

    /// <summary>Holds the `p27` shim. Kept out of <see cref="_directory"/>, which is the shell's cwd and is asserted on.</summary>
    private readonly TempDir _shimDirectory = new();

    private readonly string _script;

    public CompletionScriptTests()
    {
        _script = _directory.File("p27.bash");
        File.WriteAllText(_script, CompletionCommands.Script("bash"));
    }

    public void Dispose()
    {
        _directory.Dispose();
        _shimDirectory.Dispose();
    }

    private static bool BashAvailable => !OperatingSystem.IsWindows() && File.Exists("/bin/bash");

    /// <summary>
    /// Puts a `p27` on PATH, which is the only thing the script assumes. It shells out to
    /// `dotnet p27.dll` rather than relying on the apphost being copied next to the test
    /// binary — that is a ProjectReference detail, not something completion promises, and
    /// depending on it made these tests pass on macOS and fail on Linux.
    /// </summary>
    private string CreateShim()
    {
        var shim = Path.Combine(_shimDirectory.Path, "p27");
        var library = Path.Combine(AppContext.BaseDirectory, "p27.dll");
        File.WriteAllText(
            shim,
            $"""
             #!/bin/sh
             exec "{DotnetPath()}" "{library}" "$@"
             """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                shim,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return _shimDirectory.Path;
    }

    /// <summary>The muxer running this test, so the shim cannot pick a different .NET.</summary>
    private static string DotnetPath()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
            && File.Exists(Path.Combine(root, "dotnet")))
        {
            return Path.Combine(root, "dotnet");
        }

        return "dotnet";
    }

    /// <summary>Runs <paramref name="body"/> in a bash that has the script sourced, and returns stdout.</summary>
    private string Bash(string body)
    {
        // $$ so that the shell's own braces stay literal and only {{…}} interpolates.
        var driver = _directory.File("driver.sh");
        File.WriteAllText(
            driver,
            $$"""
              export PATH="{{CreateShim()}}:$PATH"
              cd "{{_directory.Path}}"

              # The script hides p27's stderr, so probe here: without this a broken p27 is
              # indistinguishable from "no candidates" and the failure says only "".
              command -v p27 >&2 || echo "DIAG: p27 is not on PATH" >&2
              p27 --version >/dev/null 2>>"{{_directory.File("diag.txt")}}" \
                || echo "DIAG: p27 exited $? (see diag.txt)" >&2

              source "{{_script}}"

              # Call the completion function exactly as bash does for `complete -F`.
              try() {
                COMP_WORDS=("$@")
                COMP_CWORD=$(( $# - 1 ))
                COMP_LINE="${*}"
                COMP_POINT=${#COMP_LINE}
                COMPREPLY=()
                _p27_completion
                printf '%s\n' "${COMPREPLY[*]}"
              }

              {{body}}
              """);

        using var process = Process.Start(new ProcessStartInfo("/bin/bash", [driver])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        var diagnostics = File.Exists(_directory.File("diag.txt"))
            ? File.ReadAllText(_directory.File("diag.txt"))
            : "";
        Assert.True(
            stdout.Trim().Length > 0 || stderr.Length == 0,
            $"the shell produced nothing.\nstderr:\n{stderr}\np27 stderr:\n{diagnostics}");
        return stdout.Trim();
    }

    [Fact]
    public void The_script_completes_subcommands_in_a_real_bash()
    {
        Assert.SkipUnless(BashAvailable, "needs a POSIX bash");

        Assert.Contains("add-recurring", Bash("""try p27 task ""  """), StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_completes_an_option_value_in_a_real_bash()
    {
        Assert.SkipUnless(BashAvailable, "needs a POSIX bash");

        Assert.Equal("fs ss ff sf", Bash("""try p27 link add 1 2 --type "" """));
    }

    /// <summary>
    /// The directive is the last line of `__complete`'s output. Reading it inside a helper
    /// run by process substitution loses it to the subshell, and path completion silently
    /// does nothing — which is exactly what shipped once.
    /// </summary>
    [Fact]
    public void A_file_directive_falls_back_to_path_completion_in_a_real_bash()
    {
        Assert.SkipUnless(BashAvailable, "needs a POSIX bash");

        Cli.Ok("init", "Demo", "--file", _directory.File("Demo.p27"));

        Assert.Equal("Demo.p27", Bash("""try p27 -f "" """));
    }

    [Fact]
    public void A_value_containing_spaces_stays_one_word_in_a_real_bash()
    {
        Assert.SkipUnless(BashAvailable, "needs a POSIX bash");

        var file = _directory.File("Demo.p27");
        Cli.Ok("init", "Demo", "--file", file);
        Cli.Ok("task", "add", "Design", "-d", "3d", "--file", file);
        Cli.Ok("resource", "add", "Alice Smith", "--rate", "50/h", "--file", file);

        // Escaped, not split into "Alice" and "Smith".
        Assert.Equal(@"Alice\ Smith", Bash("""try p27 assign add 1 "" """));
    }

    [Fact]
    public void The_dynamic_task_source_reaches_the_project_from_a_real_bash()
    {
        Assert.SkipUnless(BashAvailable, "needs a POSIX bash");

        var file = _directory.File("Demo.p27");
        Cli.Ok("init", "Demo", "--file", file);
        Cli.Ok("task", "add", "Design", "-d", "3d", "--file", file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", file);

        Assert.Equal("1 2", Bash("""try p27 task show "" """));
    }
}
