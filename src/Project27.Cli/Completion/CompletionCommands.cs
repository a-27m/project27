using System.CommandLine;
using System.Reflection;

namespace Project27.Cli.Completion;

/// <summary>
/// The two commands behind shell completion: `p27 completion <shell>` prints the script,
/// and the hidden `p27 __complete` is what that script calls on every TAB.
/// </summary>
internal static class CompletionCommands
{
    /// <summary>Marks the end of the candidate list; see docs/spec/13-shell-completion.md.</summary>
    internal const string DirectivePrefix = ":";

    private static readonly string[] Shells = ["bash", "zsh"];

    public static Command Completion()
    {
        var shellArg = new Argument<string>("shell") { Description = "bash or zsh." }
            .Suggests(Shells);
        var command = new Command("completion", "Print the shell completion script for bash or zsh.")
        {
            shellArg,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var shell = parseResult.GetRequiredValue(shellArg).Trim().ToLowerInvariant();
            if (!Shells.Contains(shell))
            {
                throw new CliException($"unknown shell '{shell}'; use {string.Join(" or ", Shells)}");
            }

            context.Out.Write(Script(shell));
            return 0;
        }));
        return command;
    }

    internal static string Script(string shell)
    {
        var name = $"Project27.Cli.Completion.Scripts.{(shell == "zsh" ? "_p27" : "p27.bash")}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new CliException($"the {shell} completion script is missing from this build");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// `p27 __complete -- <argv...>`, where argv is the command line the user is typing:
    /// the program name, the committed words, and the (possibly empty) word under the
    /// cursor last. The shell has already dequoted them, so values containing spaces
    /// arrive intact as single arguments.
    ///
    /// Prints `value\tdescription` per line, then a directive line. It always exits 0 and
    /// never writes to stderr: a completion that fails should be silent, not noisy.
    /// </summary>
    public static Command Complete()
    {
        var wordsArg = new Argument<string[]>("words") { Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("__complete", "Internal: resolve shell completions.") { wordsArg };
        command.Hidden = true;
        command.SetAction(parseResult =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            try
            {
                var argv = parseResult.GetValue(wordsArg) ?? [];
                var result = Resolve(argv);
                foreach (var candidate in result.Candidates.Where(IsFramable))
                {
                    output.WriteLine(Line(candidate));
                }

                output.WriteLine(DirectivePrefix + result.Directive switch
                {
                    CompletionDirective.Files => "files",
                    CompletionDirective.ProjectFiles => "p27files",
                    _ => "none",
                });
            }
            catch (Exception)
            {
                // Never let a completion failure reach the prompt.
                output.WriteLine(DirectivePrefix + "none");
            }

            return 0;
        });
        return command;
    }

    internal static CompletionResult Resolve(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
        {
            return new CompletionResult([]);
        }

        // argv[0] is the program name; the last element is the word being completed.
        var words = argv.Skip(1).Take(Math.Max(0, argv.Count - 2)).ToArray();
        var wordToComplete = argv[^1];

        var root = CliRoot.Build();
        var request = new CompletionRequest(wordToComplete, root.Parse(words));
        return CompletionEngine.Resolve(root, words, request);
    }

    /// <summary>
    /// One candidate as `value\tdescription`. Tabs and newlines inside a value would
    /// break the framing and cannot be typed at a prompt anyway, so such candidates are
    /// dropped rather than mangled; descriptions are merely flattened.
    /// </summary>
    private static string Line(Candidate candidate)
    {
        var description = candidate.Description?.ReplaceLineEndings(" ").Replace('\t', ' ');
        return string.IsNullOrWhiteSpace(description)
            ? candidate.Value
            : candidate.Value + "\t" + description;
    }

    internal static bool IsFramable(Candidate candidate)
        => !candidate.Value.Contains('\t', StringComparison.Ordinal)
        && !candidate.Value.Contains('\n', StringComparison.Ordinal)
        && !candidate.Value.Contains('\r', StringComparison.Ordinal);
}
