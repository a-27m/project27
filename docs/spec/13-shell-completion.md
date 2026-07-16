# Phase 13 — Shell completion (bash, zsh, fzf)

Goal: `p27` completes like `kubectl` or `az` — subcommands, options, and the
project's own nouns (task rows, resource names, calendars, server projects) —
with fzf as a first-class renderer where it is installed.

Status: **done**.

## 1. Surface

| Command | Purpose |
|---------|---------|
| `p27 completion bash\|zsh` | Prints the completion script for that shell (embedded resource). |
| `p27 __complete -- <argv…>` | Hidden. Resolves candidates for the argv the shell is holding. |
| `scripts/install-completion.sh [bash\|zsh\|all]` | Writes the script into the directory the shell already searches. |

The install script **never edits `~/.zshrc`, `~/.bashrc` or any other startup
file** (D4 spirit: the tool does not mutate what it does not own). Where a
directory has to join `$fpath` for zsh to find the script, the line is printed
for the user to add.

- **bash** → `${XDG_DATA_HOME:-$HOME/.local/share}/bash-completion/completions/p27`.
  bash-completion loads this on demand, so no rc entry is needed.
- **zsh** → `_p27` in the first writable `*/site-functions` directory already on
  `$fpath`; otherwise `~/.zfunc`, and *then* the `fpath=(…)` line is printed.

## 2. Why completion is resolved in-process, not by System.CommandLine

System.CommandLine 2.0.10 ships `CompletionSources` and
`ParseResult.GetCompletions()`. Both were measured and rejected (see E36 in `docs/engineering-decisions.md`):

| Path | Result |
|------|--------|
| `Parse(string).GetCompletions()` | Throws `InvalidOperationException` on any quoted value (`--project "Alpha"`). |
| `Parse(string[]).GetCompletions()` | Never fires an option's `CompletionSources`; returns sibling commands instead. |

Project and resource names contain spaces (`"Alpha Project"`, `"Alice Smith"`),
so a path that throws on quoting is disqualified rather than degraded.

`Completion/CompletionEngine.cs` therefore walks the tree that `CliRoot.Build()`
returns. It **introspects** the real `Command`/`Option`/`Argument` objects — it
never restates the command surface, so completion cannot drift from the CLI (the
same reason Core is the single source of behaviour for both hosts).

## 3. The argv protocol

The shell passes argv, never a command line:

```
p27 __complete -- <program> <committed words…> <word under cursor>
```

The shell has already dequoted the words, so `"Alpha Project"` arrives as one
element and no tokenizer is involved on our side — the class of bug that
disqualified the built-in path cannot occur.

Output is one candidate per line, then a directive line:

```
Alpha Project\towner
Beta\towner
:none
```

| Directive | The shell should |
|-----------|------------------|
| `:none` | Use our candidates only. |
| `:files` | Fall back to its own path completion. |
| `:p27files` | Fall back to path completion restricted to `*.p27`. |

Path completion stays in the shell: it already knows how to walk directories,
quote, and colour the result.

`__complete` **always exits 0 and writes nothing to stderr.** A completion that
fails must be silent — a stack trace in the prompt is worse than no completion.
Every dynamic source is wrapped, and the completion-path `RemoteClient` gets a
1.5 s timeout so a slow server cannot stall a prompt.

## 4. Server mode

`P27_SERVER` (and `P27_PROJECT`) put the CLI in server mode with **nothing on the
command line to indicate it**. Completion must therefore resolve its source
exactly the way the verbs do, so `__complete` builds a real `CliContext` from the
committed words and uses its `IsRemote`/`ServerUrl`/`ProjectRef`/`ResolveFile`.
Anything else would complete against a source the command would not use.

`CliContext.OpenProjectForCompletion` is the read-only, timeboxed load: it never
takes an editing lock (D6), because pressing TAB must not check a project out.

## 5. fzf

| Shell | Mechanism |
|-------|-----------|
| zsh | `_describe` supplies value + description; **fzf-tab** renders them (and its preview) with no extra setup. |
| zsh | fzf's own `**` trigger finds `_fzf_complete_p27` by name — no registration needed. |
| bash | `__fzf_defc p27 _fzf_complete_p27` wraps our `complete -F`, and fzf delegates back to it when `**` is absent, so plain TAB is unchanged. |

`_fzf_complete_p27_post` strips the description column so only the value is
inserted. Plain bash cannot show descriptions and drops them.

## 6. Coverage

Dynamic: server project names, task rows (id inserted, name as description),
resource names, calendar names, custom fields defined in the project, logged-in
servers. Static: every keyword list in `Parsers` (dependency type, constraint,
task/resource type, contour, accrual, rate table, day, week ordinal, boolean),
field keys and view tables from the Core catalogs, custom-field slots derived
from `CustomFieldDefinition.Slots`.

`--fields id,name` completes the last element of a comma-separated list by
re-emitting the committed prefix with each candidate, so the shell still inserts
one word.

The keyword lists in `CompletionValues` are hand-written (the parsers accept
synonyms and any casing; completion offers only the canonical spelling). A test
feeds every candidate back through its parser, so a list cannot drift into
suggesting values the CLI rejects.
