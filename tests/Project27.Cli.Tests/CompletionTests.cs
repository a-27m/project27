using Project27.Cli.Completion;
using Xunit;

namespace Project27.Cli.Tests;

/// <summary>
/// `__complete` takes the argv the shell is holding: the program name, the committed
/// words, then the (possibly empty) word under the cursor.
/// </summary>
public sealed class CompletionTests
{
    private static CompletionResult Complete(params string[] argv)
        => CompletionCommands.Resolve(argv);

    private static string[] Values(params string[] argv)
        => [.. Complete(argv).Candidates.Select(c => c.Value)];

    // ------------------------------------------------------------ command tree

    [Fact]
    public void Completes_subcommands_with_their_descriptions()
    {
        var candidates = Complete("p27", "").Candidates;

        Assert.Contains(candidates, c => c.Value == "task" && c.Description == "Manage tasks and the outline.");
        Assert.Contains(candidates, c => c.Value == "resource");
    }

    [Fact]
    public void Completes_nested_subcommands()
    {
        Assert.Contains("add-recurring", Values("p27", "task", ""));
        Assert.Contains("set-day", Values("p27", "calendar", ""));
    }

    [Fact]
    public void Filters_by_the_partial_word()
    {
        var values = Values("p27", "task", "ad");

        Assert.Equal(["add", "add-recurring"], values);
    }

    [Fact]
    public void Hides_the_internal_complete_command()
    {
        Assert.DoesNotContain("__complete", Values("p27", ""));
        Assert.Contains("completion", Values("p27", ""));
    }

    [Fact]
    public void Offers_option_names_only_once_a_dash_is_typed()
    {
        Assert.DoesNotContain("--json", Values("p27", "task", ""));
        Assert.Contains("--json", Values("p27", "task", "-"));
    }

    [Fact]
    public void Recursive_options_stay_in_scope_for_nested_commands()
    {
        var values = Values("p27", "task", "add", "-");

        // Declared on the root with Recursive = true.
        Assert.Contains("--server", values);
        Assert.Contains("--file", values);
        // Declared on `task add` itself.
        Assert.Contains("--milestone", values);
    }

    [Fact]
    public void Does_not_offer_windows_style_slash_aliases()
    {
        var values = Values("p27", "-");

        Assert.DoesNotContain(values, v => v.StartsWith('/'));
        Assert.Contains("--help", values);
    }

    [Fact]
    public void A_flag_does_not_swallow_the_next_word()
    {
        // --json takes no value, so the cursor is on a subcommand, not on --json's value.
        Assert.Contains("task", Values("p27", "--json", ""));
    }

    [Fact]
    public void An_option_value_is_not_mistaken_for_a_subcommand()
    {
        // "list" here is --project's value, so `task` is still the command to complete.
        Assert.Contains("add", Values("p27", "--project", "list", "task", ""));
    }

    // ----------------------------------------------------------- static values

    [Fact]
    public void Completes_enum_option_values()
    {
        Assert.Equal(["fs", "ss", "ff", "sf"], Values("p27", "link", "add", "1", "2", "--type", ""));
        Assert.Equal(["snet", "snlt"], Values("p27", "task", "set", "1", "--constraint", "sn"));
    }

    [Fact]
    public void Completes_short_option_values()
    {
        Assert.Equal(["day", "week"], Values("p27", "usage", "-g", ""));
    }

    [Fact]
    public void Completes_custom_field_slots_from_the_core_catalog()
    {
        var values = Values("p27", "customfield", "define", "text1");

        Assert.Contains("text1", values);
        Assert.Contains("text19", values);
    }

    /// <summary>
    /// The keyword lists in <see cref="CompletionValues"/> are written by hand; if one
    /// drifts from its parser we would be completing values the CLI then rejects.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeywordLists))]
    public void Every_suggested_keyword_is_accepted_by_its_parser(string label, string[] values, Action<string> parse)
    {
        Assert.NotEmpty(values);
        foreach (var value in values)
        {
            var exception = Record.Exception(() => parse(value));
            Assert.True(exception is null, $"{label}: the CLI rejects the suggested value '{value}': {exception?.Message}");
        }
    }

    public static TheoryData<string, string[], Action<string>> KeywordLists()
        => new()
        {
            { "dependency type", CompletionValues.DependencyTypes, v => Parsers.DependencyTypeInput(v) },
            { "constraint", CompletionValues.Constraints, v => Parsers.ConstraintInput(v) },
            { "resource type", CompletionValues.ResourceTypes, v => Parsers.ResourceTypeInput(v) },
            { "task type", CompletionValues.TaskTypes, v => Parsers.TaskTypeInput(v) },
            { "contour", CompletionValues.Contours, v => Parsers.ContourInput(v) },
            { "accrual", CompletionValues.Accruals, v => Parsers.AccrualInput(v) },
            { "rate table", CompletionValues.RateTables, v => Parsers.RateTableInput(v) },
            { "day of week", CompletionValues.DaysOfWeek, v => Parsers.DayOfWeekInput(v) },
            { "week ordinal", CompletionValues.WeekOrdinals, v => Parsers.WeekOrdinalInput(v) },
            { "boolean", CompletionValues.Booleans, v => Parsers.BoolInput(v) },
        };

    // ---------------------------------------------------------- dynamic values

    [Fact]
    public void Completes_task_rows_from_the_project_with_names_as_descriptions()
    {
        using var directory = new TempDir();
        var file = directory.File("plan.p27");
        Cli.Ok("init", "Demo", "--file", file);
        Cli.Ok("task", "add", "Design phase", "-d", "3d", "--file", file);
        Cli.Ok("task", "add", "Build it", "-d", "5d", "--file", file);

        var candidates = Complete("p27", "--file", file, "task", "show", "").Candidates;

        Assert.Equal(["1", "2"], candidates.Select(c => c.Value));
        Assert.Equal(["Design phase", "Build it"], candidates.Select(c => c.Description));
    }

    [Fact]
    public void Completes_resource_names_containing_spaces_as_single_values()
    {
        using var directory = new TempDir();
        var file = directory.File("plan.p27");
        Cli.Ok("init", "Demo", "--file", file);
        Cli.Ok("task", "add", "Design", "-d", "3d", "--file", file);
        Cli.Ok("resource", "add", "Alice Smith", "--rate", "50/h", "--file", file);

        var values = Values("p27", "--file", file, "assign", "add", "1", "");

        Assert.Equal(["Alice Smith"], values);
    }

    [Fact]
    public void Completes_the_last_element_of_a_comma_separated_list()
    {
        using var directory = new TempDir();
        var file = directory.File("plan.p27");
        Cli.Ok("init", "Demo", "--file", file);

        var values = Values("p27", "--file", file, "view", "--fields", "id,na");

        // The committed prefix comes back with the candidate so the shell inserts one word.
        Assert.Equal(["id,name"], values);
    }

    [Fact]
    public void A_variadic_argument_keeps_completing_after_the_first_value()
    {
        using var directory = new TempDir();
        var file = directory.File("plan.p27");
        Cli.Ok("init", "Demo", "--file", file);
        Cli.Ok("task", "add", "Design", "-d", "3d", "--file", file);
        Cli.Ok("task", "add", "Build", "-d", "3d", "--file", file);

        // `task indent <tasks…>` takes one-or-more refs; the second still completes.
        Assert.Equal(["1", "2"], Values("p27", "--file", file, "task", "indent", "1", ""));

        // `task remove <task>` takes exactly one, so there is nothing to offer after it.
        Assert.Empty(Values("p27", "--file", file, "task", "remove", "1", ""));
    }

    [Fact]
    public void Local_mode_offers_no_server_projects()
    {
        Assert.Empty(Values("p27", "--project", ""));
    }

    // -------------------------------------------------------------- directives

    [Fact]
    public void Project_file_options_defer_to_the_shells_path_completion()
    {
        var result = Complete("p27", "--file", "");

        Assert.Equal(CompletionDirective.ProjectFiles, result.Directive);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Output_paths_defer_to_the_shells_path_completion()
    {
        Assert.Equal(CompletionDirective.Files, Complete("p27", "export", "csv", "--out", "").Directive);
    }

    [Fact]
    public void Candidate_lists_carry_no_directive()
    {
        Assert.Equal(CompletionDirective.None, Complete("p27", "task", "").Directive);
    }

    // ------------------------------------------------------------- robustness

    [Fact]
    public void Nonsense_input_completes_to_nothing_rather_than_failing()
    {
        Assert.Empty(Complete().Candidates);
        Assert.Empty(Complete("p27").Candidates);
        Assert.Empty(Complete("p27", "task", "show", "1", "2", "3", "4", "").Candidates);
    }

    [Fact]
    public void An_unknown_word_falls_back_to_the_commands_at_that_level()
    {
        // Most likely a typo: keep offering what could legally go there.
        Assert.Contains("task", Values("p27", "no-such-command", ""));
    }

    [Fact]
    public void An_unreachable_server_completes_to_nothing_and_still_exits_zero()
    {
        var result = Cli.Run(
            "__complete", "--", "p27", "--server", "http://127.0.0.1:1", "--project", "");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Stderr);
        Assert.Equal(":none", result.Stdout.Trim());
    }

    [Fact]
    public void A_missing_project_file_completes_to_nothing_and_still_exits_zero()
    {
        var result = Cli.Run("__complete", "--", "p27", "--file", "/nope/missing.p27", "task", "show", "");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Stderr);
        Assert.Equal(":none", result.Stdout.Trim());
    }

    // ------------------------------------------------------------ wire framing

    [Fact]
    public void Candidates_are_printed_as_value_tab_description_then_a_directive()
    {
        var lines = Cli.Ok("__complete", "--", "p27", "link", "add", "1", "2", "--type", "f")
            .Stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["fs", "ff", ":none"], lines);
    }

    [Fact]
    public void A_description_is_separated_from_its_value_by_a_single_tab()
    {
        var lines = Cli.Ok("__complete", "--", "p27", "ta")
            .Stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("task\tManage tasks and the outline.", lines[0]);
    }

    // ------------------------------------------------------------- the scripts

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    public void The_completion_script_is_embedded_and_calls_back_into_complete(string shell)
    {
        var script = Cli.Ok("completion", shell).Stdout;

        Assert.Contains("p27 __complete --", script, StringComparison.Ordinal);
        // fzf is a first-class target for both shells.
        Assert.Contains("_fzf_complete_p27", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zsh_script_is_an_autoloadable_compdef_function()
    {
        var script = Cli.Ok("completion", "zsh").Stdout;

        Assert.StartsWith("#compdef p27", script, StringComparison.Ordinal);
        // _describe is what feeds descriptions to zsh and to fzf-tab.
        Assert.Contains("_describe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bash_script_registers_itself_and_survives_comp_wordbreaks()
    {
        var script = Cli.Ok("completion", "bash").Stdout;

        Assert.Contains("complete -F _p27_completion p27", script, StringComparison.Ordinal);
        // ':' and '=' would otherwise shred `uid:5` refs.
        Assert.Contains("_init_completion -n \":=\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_shell_is_an_error_not_an_empty_script()
    {
        Assert.Contains("unknown shell", Cli.Fail("completion", "fish").Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_argument_completes_to_the_supported_shells()
    {
        Assert.Equal(["bash", "zsh"], Values("p27", "completion", ""));
    }
}
