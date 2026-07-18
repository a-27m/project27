using System.CommandLine;

namespace Project27.Cli.Completion;

/// <summary>
/// What a <see cref="CandidateSource"/> gets to work with: the partial word, and a
/// <see cref="CliContext"/> over the words typed so far.
///
/// The context is the real one, so `--server`/`P27_SERVER` resolution, credential lookup
/// and `.p27` discovery behave exactly as they will when the command actually runs —
/// completing on a server the command would not use is worse than not completing at all.
/// </summary>
internal sealed class CompletionRequest
{
    /// <summary>Dynamic sources reach the network or disk; a prompt must never hang on them.</summary>
    internal static readonly TimeSpan RemoteTimeout = TimeSpan.FromMilliseconds(1500);

    public CompletionRequest(string wordToComplete, ParseResult parseResult)
    {
        WordToComplete = wordToComplete;
        Cli = new CliContext(parseResult);
    }

    /// <summary>The word being completed; may be empty. Sources need not filter on it.</summary>
    public string WordToComplete { get; }

    public CliContext Cli { get; }

    public bool IsRemote => Cli.IsRemote;

    /// <summary>
    /// Loads the project the command would act on, or null if it cannot be reached.
    /// Completion is best-effort: an unreachable server or an absent file yields nothing.
    /// </summary>
    public Core.Project? TryOpenProject()
    {
        try
        {
            return Cli.OpenProjectForCompletion(RemoteTimeout);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
