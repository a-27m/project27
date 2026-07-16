using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Project27.Cli.Completion;

/// <summary>
/// One completion candidate: the literal the shell inserts, plus an optional
/// description shown beside it (zsh and fzf render it; plain bash drops it).
/// </summary>
internal readonly record struct Candidate(string Value, string? Description = null);

/// <summary>
/// What the shell should do besides inserting our candidates. Path completion stays in
/// the shell: it already knows how to walk directories, quote, and colour the result.
/// </summary>
internal enum CompletionDirective
{
    /// <summary>Our candidates are the whole answer.</summary>
    None,

    /// <summary>Complete paths.</summary>
    Files,

    /// <summary>Complete `.p27` files and directories to descend into.</summary>
    ProjectFiles,
}

/// <summary>Produces the candidates for one option value or positional argument.</summary>
internal delegate IEnumerable<Candidate> CandidateSource(CompletionRequest request);

internal sealed record CompletionResult(
    IReadOnlyList<Candidate> Candidates,
    CompletionDirective Directive = CompletionDirective.None);

/// <summary>
/// Attaches candidate sources to the symbols of the real command tree.
///
/// System.CommandLine 2.0.10 has its own <c>CompletionSources</c>, but its completion
/// path is unusable here: <c>Parse(string)</c> throws on quoted values and array parsing
/// never fires an option's sources at all. Project and task names contain spaces, so we
/// resolve completions ourselves (see <see cref="CompletionEngine"/>) and keep only the
/// tree itself — never a second copy of the command surface.
/// </summary>
internal static class CompletionRegistry
{
    private static readonly ConditionalWeakTable<Symbol, CandidateSource> Sources = new();
    private static readonly ConditionalWeakTable<Symbol, object> Directives = new();

    /// <summary>Registers <paramref name="source"/> as the completions for this symbol.</summary>
    internal static T Suggests<T>(this T symbol, CandidateSource source)
        where T : Symbol
    {
        Sources.AddOrUpdate(symbol, source);
        return symbol;
    }

    /// <summary>Registers a fixed value list, in the canonical spelling the docs use.</summary>
    internal static T Suggests<T>(this T symbol, params string[] values)
        where T : Symbol
        => symbol.Suggests(_ => values.Select(v => new Candidate(v)));

    /// <summary>Hands this symbol's completion to the shell's own path completion.</summary>
    internal static T SuggestsPaths<T>(this T symbol, CompletionDirective directive = CompletionDirective.Files)
        where T : Symbol
    {
        Directives.AddOrUpdate(symbol, directive);
        return symbol;
    }

    internal static CandidateSource? SourceFor(Symbol symbol)
        => Sources.TryGetValue(symbol, out var source) ? source : null;

    internal static CompletionDirective DirectiveFor(Symbol symbol)
        => Directives.TryGetValue(symbol, out var directive)
            ? (CompletionDirective)directive
            : CompletionDirective.None;
}
