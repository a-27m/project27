using System.Globalization;
using Project27.Cli.Auth;
using Project27.Core;
using Project27.Core.Fields;
using Project27.Core.Views;

namespace Project27.Cli.Completion;

/// <summary>
/// The candidate sources wired onto the command tree.
///
/// The keyword lists mirror the `switch` statements in <see cref="Parsers"/>. They are
/// spelled out rather than derived because the parsers accept synonyms and any casing,
/// while completion should only ever offer the one canonical spelling. A test feeds every
/// candidate here back through its parser, so the two cannot drift apart unnoticed.
/// </summary>
internal static class CompletionValues
{
    public static readonly string[] DependencyTypes = ["fs", "ss", "ff", "sf"];

    public static readonly string[] Constraints =
        ["asap", "alap", "snet", "snlt", "fnet", "fnlt", "mso", "mfo"];

    public static readonly string[] ResourceTypes = ["work", "material", "cost"];

    public static readonly string[] TaskTypes = ["fixed-units", "fixed-duration", "fixed-work"];

    public static readonly string[] Contours =
        ["flat", "back-loaded", "front-loaded", "double-peak", "early-peak", "late-peak", "bell", "turtle"];

    public static readonly string[] Accruals = ["start", "prorated", "end"];

    public static readonly string[] RateTables = ["A", "B", "C", "D", "E"];

    public static readonly string[] DaysOfWeek = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    public static readonly string[] WeekOrdinals = ["first", "second", "third", "fourth", "last"];

    public static readonly string[] Booleans = ["true", "false"];

    public static readonly string[] ScheduleFrom = ["start", "finish"];

    /// <summary>`calendar add --preset`; see CalendarCommands.Add.</summary>
    public static readonly string[] CalendarPresets = ["standard", "24h", "night-shift"];

    /// <summary>Server projects, by name. Silent in local mode — there is nothing to list.</summary>
    public static IEnumerable<Candidate> Projects(CompletionRequest request)
    {
        if (!request.IsRemote)
        {
            return [];
        }

        using var client = request.Cli.CreateRemoteClient(CompletionRequest.RemoteTimeout);
        return [.. client.ListProjects().Select(p => new Candidate(p.Name, Describe(p)))];

        static string Describe(RemoteProjectInfo project)
            => project.Lock is { Stale: false } held
                ? $"{project.Role}, checked out by {held.UserId}"
                : project.Role;
    }

    /// <summary>Servers the user has logged into, so `--server <TAB>` is not a blank.</summary>
    public static IEnumerable<Candidate> Servers(CompletionRequest request)
        => CredentialStore.Load().Values.Select(c => new Candidate(c.ServerUrl, "logged in"));

    /// <summary>Task rows: the id is inserted, the name is what the user actually recognises.</summary>
    public static IEnumerable<Candidate> Tasks(CompletionRequest request)
    {
        if (request.TryOpenProject() is not { } project)
        {
            return [];
        }

        return
        [
            .. project.Tasks.Select(t => new Candidate(
                t.RowNumber.ToString(CultureInfo.InvariantCulture),
                t.Name)),
        ];
    }

    public static IEnumerable<Candidate> Resources(CompletionRequest request)
    {
        if (request.TryOpenProject() is not { } project)
        {
            return [];
        }

        return [.. project.Resources.Select(r => new Candidate(r.Name, r.Type.ToString()))];
    }

    public static IEnumerable<Candidate> Calendars(CompletionRequest request)
    {
        if (request.TryOpenProject() is not { } project)
        {
            return [];
        }

        return [.. project.Calendars.Select(c => new Candidate(c.Name, c.BaseCalendar is null ? "base" : "derived"))];
    }

    public static IEnumerable<Candidate> Fields(CompletionRequest request)
        => FieldCatalog.All
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new Candidate(f.Key, f.Caption));

    public static IEnumerable<Candidate> Tables(CompletionRequest request)
        => TaskView.Tables.Keys.Select(k => new Candidate(k, "field selection"));

    /// <summary>Every custom-field slot id, derived from the Core catalog.</summary>
    public static IEnumerable<Candidate> CustomFieldSlots(CompletionRequest request)
        => CustomFieldDefinition.Slots.SelectMany(slot => Enumerable
            .Range(1, slot.Value.Count)
            .Select(n => new Candidate(
                slot.Key + n.ToString(CultureInfo.InvariantCulture),
                slot.Value.Kind.ToString())));

    /// <summary>Custom fields this project actually defines, by slot id and by alias.</summary>
    public static IEnumerable<Candidate> DefinedCustomFields(CompletionRequest request)
    {
        if (request.TryOpenProject() is not { } project)
        {
            return [];
        }

        return
        [
            .. project.CustomFields.SelectMany(f => f.Alias is { } alias
                ? new Candidate[] { new(f.Id, alias), new(alias, f.Id) }
                : [new Candidate(f.Id, f.Kind.ToString())]),
        ];
    }

    /// <summary>
    /// Completes the last element of a comma-separated value (`--fields id,name,finish`).
    /// The committed prefix is re-emitted with each candidate so that the engine's
    /// whole-word prefix filter still narrows correctly and the shell inserts one word.
    /// </summary>
    public static CandidateSource CommaList(CandidateSource inner) => request =>
    {
        var word = request.WordToComplete;
        var prefix = word[..(word.LastIndexOf(',') + 1)];
        return inner(request).Select(c => c with { Value = prefix + c.Value });
    };
}
