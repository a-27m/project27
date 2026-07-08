# Phase 3 — Persistence & CLI

Covers the `.p27` local project file, the `IProjectStore` abstraction, and the
`p27` command-line host over the engine surface delivered in phases 1–2
(calendars, outline, CPM scheduling). Server mode (`--server`) is phase 6; this
phase is local-file only.

## 1. `.p27` file format

A `.p27` file is a SQLite database with two tables:

| Table | Shape | Purpose |
|-------|-------|---------|
| `meta` | `key TEXT PK, value TEXT` | `format_version` (currently `1`), `project_name` (informational, for shell tooling) |
| `snapshot` | `id INTEGER PK CHECK(id=1), json TEXT, saved_at TEXT` | Exactly one row: the serialized `ProjectDocument`, plus a UTC ISO-8601 save stamp |

Design properties:

- **Snapshot, not event log.** Each save overwrites the single snapshot row in a
  transaction. Migration path is `format_version` (file envelope) plus
  `SchemaVersion` (JSON document, currently `1`) — either can evolve
  independently.
- **Inputs only.** The document stores scheduling *inputs* (durations,
  constraints, dependencies, calendars, manual dates, split shapes). Computed
  outputs (start/finish, slack, criticality) are recomputed by
  `Project.Recalculate()` on load; a `.p27` file can never contain a schedule
  that disagrees with its inputs.
- **Portability over throughput.** WAL off, connection pooling off — the file is
  fully closed between operations so it can be copied, synced, or mailed at any
  time the CLI is not mid-command.
- Stable identity survives round-trips: project/task/calendar GUIDs and task
  `UniqueId`s are restored verbatim.

`IProjectStore` is intentionally minimal (`Load`/`Save`); creation and opening
are concerns of the concrete store (`SqliteProjectStore.Create/Open`). The
PostgreSQL implementation arrives with the server (phase 6).

## 2. `p27` CLI

Git-style verbs (D8), local in-process engine. Human-readable tables by
default, `--json` for scripting. Every mutating verb is: load file → mutate
aggregate → `Recalculate()` → save → report. There is no daemon and no state
between invocations; the file is the state.

> The Core command layer (undo/redo) is a later phase; until it lands the CLI
> mutates the aggregate directly. Verb semantics are designed so the migration
> is invisible to users.

### 2.1 Global options

| Option | Meaning |
|--------|---------|
| `-f, --file <path>` | Project file. Default: exactly one `*.p27` in the current directory; zero or several is an error. |
| `--json` | Machine output on stdout: camelCase, ISO-8601 timestamps, enums as strings. |

Errors always go to stderr as `error: <message>`; exit code is `0` on success,
`1` on any failure (usage or runtime). Stdout carries only data.

### 2.2 Value syntaxes

- **Duration** — engine syntax: `3d`, `2.5w`, `30m`, `4eh` (elapsed), `1d?`
  (estimated). Case-insensitive unit aliases per phase-1 spec.
- **Date/time** — `yyyy-MM-dd` or `yyyy-MM-dd HH:mm` (invariant culture).
  Date-only values get the project's default start time for start-like fields
  (start, `snet`/`mso` constraints, manual start) and the default end time for
  finish-like fields (finish, deadline, `fnlt`/`mfo` constraints, manual
  finish).
- **Task reference** — bare integer = row number (the ID column of `task
  list`, MS Project's "ID"); `uid:<n>` = stable `UniqueId`. Row numbers shift
  on outline edits; UIDs never do.
- **Lag** — a duration (`2d`, `4eh`), a percentage (`50%`), or either with a
  leading `-` for lead time.
- **Dependency type** — `fs`, `ss`, `ff`, `sf` (case-insensitive).
- **Day schedule** — `off`, `inherit` (only where inheritance exists:
  `calendar set-day` on a derived calendar, work-week days), or intervals
  `08:00-12:00,13:00-17:00` (end `00:00` = midnight).
- **Recurrence** — colon-separated spec, shared by recurring tasks and
  calendar exceptions:
  `daily[:N]` · `weekly[:N]:mon,wed,fri` · `monthly-day:<day>[:N]` ·
  `monthly-weekday:first|second|third|fourth|last:<dow>[:N]` ·
  `yearly-date:<month>-<day>` · `yearly-weekday:<ordinal>:<dow>:<month>`
  (`N` = every N days/weeks/months, default 1).

### 2.3 Verbs

```
p27 init <name> [--start <date>] [--file <path>]

p27 project show
p27 project set [--name S] [--start DATE] [--finish DATE]
                [--schedule-from start|finish] [--calendar NAME]
                [--minutes-per-day N] [--minutes-per-week N]
                [--days-per-month N] [--week-starts-on DOW]
                [--day-start HH:mm] [--day-end HH:mm]
                [--critical-slack DURATION]

p27 schedule recalc

p27 task add <name> [--duration D] [--parent REF] [--at N] [--milestone]
p27 task add-recurring <name> --duration D --recur SPEC --from DATE
                       (--until DATE | --times N) [--parent REF]
p27 task list [--critical]
p27 task show REF
p27 task set REF [--name S] [--duration D] [--mode auto|manual]
                 [--active BOOL] [--milestone BOOL] [--priority N]
                 [--deadline DATE|none] [--constraint TYPE]
                 [--constraint-date DATE] [--calendar NAME|default]
                 [--wbs CODE|auto] [--manual-start DATE|none]
                 [--manual-finish DATE|none]
p27 task remove REF
p27 task move REF --parent REF|top [--at N]
p27 task indent REF...
p27 task outdent REF...
p27 task split REF --at D --gap D
p27 task unsplit REF

p27 link add <pred> <succ> [--type fs|ss|ff|sf] [--lag L]
p27 link set <pred> <succ> [--type T] [--lag L]
p27 link remove <pred> <succ>
p27 link list

p27 calendar list
p27 calendar show NAME
p27 calendar add NAME [--base NAME | --preset standard|24h|night-shift]
p27 calendar remove NAME
p27 calendar set-day NAME DOW SPEC
p27 calendar set-base NAME BASE|none
p27 calendar add-exception NAME EXNAME --from DATE [--to DATE]
                           [--hours SPEC] [--recur SPEC] [--times N]
p27 calendar remove-exception NAME EXNAME
p27 calendar add-workweek NAME WWNAME --from DATE --to DATE
                          [--mon SPEC] ... [--sun SPEC]
p27 calendar remove-workweek NAME WWNAME
```

Notes:

- `init` writes `<name>.p27` in the current directory unless `--file` says
  otherwise; refuses to overwrite. `--start` defaults to today at the default
  start time.
- Constraint types: `asap`, `alap`, `snet`, `snlt`, `fnet`, `fnlt`, `mso`,
  `mfo`. Setting `asap`/`alap` clears the constraint date; the other six
  require `--constraint-date` (or an already-set date).
- `task list` shows row number, indented name, duration, start, finish,
  predecessors in MSP notation (`3FS+2d`, bare row number for FS+0), and a
  `*` critical marker. Summary rows show rolled-up values.
- Calendars are addressed by name (case-insensitive, must be unambiguous);
  exceptions and work weeks by their name within the calendar.
- Mutations print a one-line confirmation (human) or the affected entity
  (`--json`).

### 2.4 JSON output

Shapes mirror the field catalog progressively; phase-3 task shape:

```json
{ "uid": 3, "id": 2, "name": "Design", "outlineLevel": 0, "wbs": "1.2",
  "summary": false, "milestone": false, "recurring": false, "mode": "auto",
  "active": true, "critical": true, "duration": "3d", "estimated": false,
  "start": "2026-07-08T08:00:00", "finish": "2026-07-10T17:00:00",
  "earlyStart": "…", "earlyFinish": "…", "lateStart": "…", "lateFinish": "…",
  "totalSlack": "0m", "freeSlack": "0m", "constraint": "asSoonAsPossible",
  "constraintDate": null, "deadline": null, "priority": 500,
  "calendar": null, "segments": [{ "start": "…", "finish": "…" }],
  "predecessors": [{ "uid": 1, "id": 1, "type": "finishToStart", "lag": "2d" }] }
```

`outlineLevel` is 0-based (top level = 0), the engine convention; MSPDI's
1-based `OutlineLevel` is an interop concern. Slack and lag render as compact
durations in days (`0.5d`, `-1ed`, `50%` for percent lag); consumers needing
raw numbers get minute fields with the full field catalog in phase 9.

## 3. Testing

- Storage: round-trip fixture (rich calendar/task/link population) asserting
  identical recomputed schedules — already in place.
- CLI: in-process invocation of the command tree against temp-dir `.p27`
  files, asserting exit codes, stderr, table output shapes, and `--json`
  payloads; golden scenario building a small plan end-to-end via verbs and
  checking the scheduled dates match the engine's.
