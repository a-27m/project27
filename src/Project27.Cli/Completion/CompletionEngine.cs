using System.CommandLine;

namespace Project27.Cli.Completion;

/// <summary>
/// Resolves shell completions by walking the real `p27` command tree.
///
/// Input is argv, never a command line: the shell has already dequoted the words, so a
/// value with spaces ("Alpha Project") arrives as one element and no tokenizer is
/// involved. <paramref name="words"/> is everything committed before the cursor
/// (excluding the program name); the partial word under the cursor is separate and may
/// be empty.
/// </summary>
internal static class CompletionEngine
{
    public static CompletionResult Resolve(Command root, IReadOnlyList<string> words, CompletionRequest request)
    {
        var command = root;
        var inScope = new List<Option>(root.Options);
        var positionals = 0;

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];

            if (FindOption(inScope, word) is { } option)
            {
                // Skip the option's value so it is not mistaken for a subcommand.
                if (TakesValue(option) && index + 1 < words.Count)
                {
                    index++;
                }

                continue;
            }

            if (command.Subcommands.FirstOrDefault(c => c.Name == word || c.Aliases.Contains(word)) is { } child)
            {
                command = child;
                // Recursive options stay in scope for descendants; the rest do not.
                inScope = [.. child.Options, .. inScope.Where(o => o.Recursive)];
                positionals = 0;
                continue;
            }

            positionals++;
        }

        // A trailing value-taking option means the cursor sits on its value.
        if (words.Count > 0 && FindOption(inScope, words[^1]) is { } pending && TakesValue(pending))
        {
            return For(pending, request);
        }

        if (request.WordToComplete.StartsWith('-'))
        {
            return new CompletionResult(Filter(OptionNames(command, inScope), request.WordToComplete));
        }

        var subcommands = Filter(Subcommands(command), request.WordToComplete);
        var positional = PositionalFor(command, positionals);
        if (positional is null)
        {
            return new CompletionResult(subcommands);
        }

        var values = For(positional, request);
        return values with { Candidates = [.. subcommands, .. values.Candidates] };
    }

    // ------------------------------------------------------------------ sources

    private static CompletionResult For(Symbol symbol, CompletionRequest request)
        => new(
            Filter(Invoke(CompletionRegistry.SourceFor(symbol), request), request.WordToComplete),
            CompletionRegistry.DirectiveFor(symbol));

    private static IEnumerable<Candidate> Subcommands(Command command)
        => command.Subcommands
            .Where(c => !c.Hidden)
            .Select(c => new Candidate(c.Name, c.Description));

    private static IEnumerable<Candidate> OptionNames(Command command, List<Option> inScope)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in inScope.Where(o => !o.Hidden))
        {
            foreach (var name in Names(option))
            {
                if (seen.Add(name))
                {
                    yield return new Candidate(name, option.Description);
                }
            }
        }

        if (seen.Add("--help"))
        {
            yield return new Candidate("--help", "Show help and usage information.");
        }

        static IEnumerable<string> Names(Option option)
            // `/h`-style aliases are a Windows convention System.CommandLine adds; they
            // are noise in a POSIX shell.
            => new[] { option.Name }.Concat(option.Aliases).Where(n => n.StartsWith('-'));
    }

    private static Argument? PositionalFor(Command command, int positionals)
    {
        var arguments = command.Arguments;
        if (arguments.Count == 0)
        {
            return null;
        }

        // Past the last argument only a variadic one keeps accepting values.
        return positionals < arguments.Count
            ? arguments[positionals]
            : arguments[^1].Arity.MaximumNumberOfValues > 1 ? arguments[^1] : null;
    }

    /// <summary>
    /// Runs a source defensively: a dynamic source reaches the network or disk, and a
    /// completion that throws would print a stack trace into the user's prompt.
    /// </summary>
    private static IEnumerable<Candidate> Invoke(CandidateSource? source, CompletionRequest request)
    {
        if (source is null)
        {
            return [];
        }

        try
        {
            return [.. source(request)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    // ----------------------------------------------------------------- plumbing

    private static Option? FindOption(List<Option> inScope, string word)
        => word.StartsWith('-')
            ? inScope.FirstOrDefault(o => o.Name == word || o.Aliases.Contains(word))
            : null;

    /// <summary>True when the option consumes the next word (`--project X`, not `--json`).</summary>
    private static bool TakesValue(Option option) => option.Arity.MinimumNumberOfValues > 0;

    /// <summary>
    /// Prefix match, case-insensitive: bash and zsh insert the candidate verbatim, so
    /// offering values that do not extend what was typed would corrupt the word. fzf
    /// narrows further within this set.
    /// </summary>
    private static IReadOnlyList<Candidate> Filter(IEnumerable<Candidate> candidates, string word)
        => [.. candidates.Where(c => c.Value.StartsWith(word, StringComparison.OrdinalIgnoreCase))];
}
