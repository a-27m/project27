# MCP server epic

A third host, `src/Project27.Mcp` (binary `p27-mcp`), exposing Core's operations
as [Model Context Protocol](https://modelcontextprotocol.io/) tools so AI
clients (Claude Desktop, Claude Code, and any other MCP client) can plan and
inspect Project27 projects directly. Built on the official `ModelContextProtocol`
C# SDK, stdio transport only (the standard for a locally-launched MCP server).

## Mode selection (D8 parity)

Same dual-mode shape as the CLI — the server operates on exactly one project
for the life of its stdio session, but unlike the CLI that project need not
be resolved at launch:

- **Local**: `--file <path>` or `P27_FILE`; if neither is given, the sole
  `.p27` file in the current directory (same rule as `CliContext.ResolveFile`).
  If none of those resolve a file, the session starts idle in that directory.
- **Remote**: `--server <url>` or `P27_SERVER` (plus auth: `--dev-user`/
  `P27_DEV_USER` or `--token`/`P27_TOKEN`) is required up front, but
  `--project <name|id>`/`P27_PROJECT` is optional — without it the session
  starts idle against that server/credentials.

An idle session has no bound project yet; every tool except `create_project`/
`open_project` fails with a clear "no project open" error until one of those
establishes it (see below). This is what lets a chat client create or attach
to a project mid-conversation instead of needing the file/project chosen
before the server ever launches.

## `create_project` / `open_project`: establishing the session lazily

`Session/ProjectSessionHost` is the `IProjectSession` the tool classes
actually see. It forwards every read/write to whichever session is
"current," and adds two operations that aren't part of `IProjectSession`
itself:

- **`create_project`** — local mode: bootstraps a new `.p27` file via
  `LocalProjectSession.Create` (`path` defaults to `"<name>.p27"` in the
  server's working directory). Remote mode: `RemoteProjectSession.CreateAsync`
  POSTs `/api/projects` then checks it out, mirroring the server's own
  `CreateProject` handler.
- **`open_project`** — attaches to something that already exists: a `.p27`
  path (local) or a project name/id (remote, via the existing `Resolve` +
  checkout flow already used by eager startup).

Both are one-shot: the host reserves its "project slot" *before* doing any
file/HTTP work and releases the reservation if that work throws, so a
rejected or failed create/open never leaves an orphan file or a half-open
remote checkout behind. Calling either a second time — including a startup
that already resolved a file/project — fails fast with "a project is already
open in this session; restart the MCP server to work with a different one."
This isn't full multi-project support (still one project per process,
matching D1's per-invocation-scope simplicity); it only moves *when* that one
project gets chosen from "always at launch" to "at launch, or on first use."

Known asymmetry, accepted rather than fixed: remote `create_project` is two
server calls (`POST /api/projects` then checkout); if the first succeeds and
the second throws, the new project exists on the server but this session
never adopts it — a real orphan, the server-side counterpart of the local
orphan-file bug the slot-reservation guard fixes. Left as-is because the
window is a single network call on a project this session just created and
owns (checkout essentially always succeeds); revisit with a `DELETE
/api/projects/{id}` in the catch if it's ever observed in practice.

## `Session/IProjectSession`: the mode-agnostic seam

Tool classes depend only on `IProjectSession`; they don't know which mode is
active.

- **`LocalProjectSession`** opens the file via `Storage.SqliteProjectStore`,
  loads and recalculates once, and applies every mutation through Core's
  existing `Commands.CommandExecutor` — the same command layer the web
  client's `POST /commands` rides on (E20). No new mutation path.
- **`RemoteProjectSession`** checks the project out on open (D6), applies
  mutations via `POST /projects/{id}/commands`, and reads via the existing
  `GET .../schedule|view|usage|drivers/{uid}|reports/{name}` endpoints. The
  lock releases on dispose only if this session's checkout call was the one
  that acquired it — `AcquiredAt == RefreshedAt` on the checkout response
  means it was free before this call (mirrors the CLI's inferred-keep rule,
  E19). A best-effort unlock swallows failures on shutdown; `p27 unlock`
  recovers a stuck lock same as it does for the CLI.

Both implementations recalculate once after load and once per applied command
batch, matching every other host (E2) — nothing auto-recalculates.

## Wire shapes: a third consumer-owned projection

`Session/ProjectionDtos.cs` defines Mcp's own read-side records (`TaskView`,
`UsageResult`, `ProjectSummary`, ...). This is not a new design — it's the
same "projections are consumer-owned" split as CLI `JsonShapes` and server
`ScheduleProjection` (E22), just a third consumer. `LocalProjectSession`
builds these directly from Core (`Views.TaskView`, `Reports.ReportBuilder`,
`EarnedValue`, `ProjectStats`, `Usage.Timephased`, `Scheduling.TaskDrivers`);
`RemoteProjectSession` deserializes the server's JSON straight into the same
record shapes (field names were chosen to match the server DTOs exactly, so
no translation layer is needed on that side).

Writes skip the parallel-model problem entirely: tool methods construct the
actual `Core.Commands.ProjectCommand` subtypes (`AddTaskCommand`,
`SetResourceCommand`, ...) from their flattened parameters and hand them to
`IProjectSession.ApplyAsync`, which either runs them through
`CommandExecutor` in-process (local) or serializes the same polymorphic
`ProjectCommand` list straight into the commands POST body (remote) — the
`[JsonPolymorphic]`/`[JsonDerivedType]` attributes on `ProjectCommand` (E20)
mean no bespoke serialization code is needed for either path.

## Tools: grouped by entity, not 1:1 with commands

`ProjectCommand` has ~35 variants; a 1:1 tool-per-command mapping would mean
~35 top-level tools with heavy schema overlap (many MCP clients pay a
context-token cost per tool definition on every turn, whether called or
not, and more same-shaped tools measurably hurts a model's tool-selection
accuracy). Instead, each tool groups one entity's operations behind an `op`
parameter, giving ~13 tools total:

- **Session**: `create_project`, `open_project`
- **Reads**: `get_project`, `list_tasks`, `get_task`, `list_resources`,
  `get_task_drivers`, `get_usage`, `get_report`
- **Writes**: `task_write`, `link_write`, `resource_write`,
  `assignment_write`, `calendar_write`, `field_write`, `schedule_write`
  (baselines/leveling/reschedule), `project_write`

Each write tool's parameters are a flattened union of the fields its grouped
commands need (e.g. `task_write` covers add/set/remove/move/indent/outdent/
split/unsplit/addRecurring) — still a direct field-for-field mapping onto the
command records, just chosen by an `op` string instead of one tool per
variant. `list_tasks`/`get_task` reuse the engine's existing filter/sort/
field-key syntax (`Views.FilterParser`, `Views.TaskView.Tables`) rather than
inventing a second query language.

## Implementation gotchas for future tools

- The MCP C# SDK's reflection-based JSON-schema builder treats a parameter as
  **required** unless it has an explicit default (`= null`, `= false`, ...) —
  a nullable *type* alone (`string?`) is not sufficient. A parameter declared
  `string? table` (no `= null`) surfaces at call time as "the arguments
  dictionary is missing a value for the required parameter", not a compile
  error. Every optional tool parameter in this codebase carries an explicit
  default; keep that pattern for any new tool.
- By default the SDK swallows every tool exception to a generic "An error
  occurred invoking '<tool>'" string — only `ModelContextProtocol.McpException`'s
  `Message` reaches the client (its doc comment says as much: other exception
  types are assumed to risk leaking internals). `Program.cs` registers a
  `WithRequestFilters(f => f.AddCallToolFilter(...))` that catches this
  codebase's user-facing exception types (`ProjectSessionException`,
  `ArgumentException`, `KeyNotFoundException`, `Commands.CommandException`)
  and rethrows them as `McpException` so the model actually sees "no task
  with uid 47" instead of a content-free failure. New tool code should throw
  one of those types (or extend the filter's exception list) for anything
  the model should be able to read and act on.

## Not in scope (v1)

Interop (MSPDI/CSV import/export) isn't exposed as a tool — `create_project`/
`open_project` cover starting or attaching to a `.p27`/server project, but
not importing one from another format. Use the CLI or web for that, then
`open_project` the result.

## Verification

Both modes, and both the eager and lazy startup paths, were exercised live
with a hand-rolled JSON-RPC/stdio client, not just unit tests:

- **Local, pre-existing file**: `initialize` → `tools/list` → `get_project` →
  `list_tasks` → `task_write` add against a real example `.p27` file, then the
  same file reopened with `p27 task list --json` — confirmed the CLI saw the
  task the MCP server added.
- **Remote, pre-existing project**: the same sequence plus
  `get_task_drivers`, `get_usage`, and `get_report`, against a throwaway
  project on a real `Project27.Server` (DevAuth, localhost) — every
  read/write round-tripped through checkout, `POST /commands`, and the
  view/schedule/usage/drivers/reports endpoints, and `p27 task list` against
  the same server confirmed the write persisted.
- **Local and remote, launched idle**: launched with no `--file`/`--project`,
  confirmed `get_project` fails with the actual "no project open" message
  (not the SDK's generic one), then `create_project` → `task_write` →
  a second `create_project` call (confirmed rejected, with no orphan file on
  disk and no orphan checkout) — for both local (`p27-mcp` with no args in an
  empty directory) and remote (`p27-mcp --server ... --dev-user ...` with no
  `--project`), each cross-checked via the CLI against the same file/server.

`Project27.Mcp.Tests` covers `LocalProjectSession`, `ProjectSessionHost`
(including the create/open guards and the no-orphan-file regression), and
the tool classes directly (18 tests). `RemoteProjectSession` has no
automated test yet — the live-server smoke tests above are its only
coverage today. Not done: an in-process integration test against a running
`Project27.Server` fixture; the CLI/Server test projects'
`WebApplicationFactory` harness is the natural base for a follow-up.
