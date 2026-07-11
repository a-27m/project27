# Progress & session notes

Working state for whoever (or whatever session) picks the project up next.
Update this file at the end of every phase. Facts here are the ones that are
expensive to re-derive; conventions live in `decisions.md` (D1–D9 + D6a).

## Phase status

| # | Phase | Status | Commit |
|---|-------|--------|--------|
| 0 | Scaffold & docs | done | 9e15c50 |
| 1 | Domain + calendar engine | done | 20011aa |
| 2 | CPM scheduler | done | b98560b |
| 3 | Persistence + CLI | done | 695563e + d31b7d0 |
| 4 | Resources & costs | done | f269ad9 |
| 5 | Interop (MSPDI/CSV) | **postponed → after 12** | — |
| 6 | Server | done | 2bfdc88 |
| 7 | Web foundation | done | dd99a46 + cd38b54 |
| 8 | Tracking & EVM | done | ed5944d |
| 9 | Views & fields | **done** (a92e84e, 75ae639, 9cabb8d, f3e1edf) | — |
| 10 | Advanced scheduling | done (8a35e81) — **subprojects = extension point only** (user decision 2026-07-11; revisit at the very end, after 12/5; seams in spec 10) | — |
| 11 | Reports | next | — |
| 12 | Polish | pending | — |

Specs: `docs/spec/01…04, 06, 07, 08, 09`. Deviations from MS Project: `docs/spec/deviations.md` (#1–#25).

## Build & test

- `dotnet build Project27.slnx` — must stay at **0 warnings** (TreatWarningsAsErrors).
- `dotnet test` — all suites; counts at phase 8 close: Core 165, Storage 3,
  Server 17, Cli 63 (248 total) + web 21 (Vitest), all green.
- net10.0, central package management. Pinned overrides for vulnerable
  transitives: SQLitePCLRaw.bundle_e_sqlite3 3.0.3, Microsoft.OpenApi 2.10.0.

## Engine facts that bite (verified, easy to forget)

- Scheduling is **explicit**: mutate → `Project.Recalculate()`. Stores/CLI do it on load/save.
- `OutlineLevel` is **0-based**; hidden root = −1 (deviation #10). Top-level count = `OutlineLevel == 0`.
- Summary `Duration` getter is stale; use `DurationMinutes` (rolled up). Setting a summary duration throws.
- `WorkCalendar(name)` with no base/week = dead calendar; presets via `CreateStandard/Create24Hours/CreateNightShift`.
- Working-time arithmetic = extension methods on `IWorkSchedule` (`WorkScheduleArithmetic`); `ScheduleIntersection` combines task × resource calendars.
- Triangle (`EffortTriangle`): edits via `Assignment.SetWork/SetUnits/SetContour` and the `ProjectTask.Duration` setter; **restore paths bypass it** (mapper restores tasks before assignments exist).
- Costs/assignment dates are outputs of `Recalculate`; `Task.Cost`/`WorkMinutes` roll up excluding inactive children.
- xUnit v3: explicit `using Xunit;` (no global usings); `Assert.Throws<T>` is exact-type; fixtures need parameterless ctors; xUnit1051 wants `TestContext.Current.CancellationToken`.

## Persistence

- `ProjectDocument` SchemaVersion **2** (v1 loads; mapper gate `is not (1 or 2)`).
- One wire format: `Project27.Storage.ProjectDocumentSerializer` (string enums, ignore-null). `.p27` file: `meta` + single-row `snapshot`; server store: `projects/snapshots(history)/members/locks`, all TEXT/INTEGER, dialect-neutral SQL shared by `SqliteServerStore`/`PostgresServerStore`.

## Server (phase 6) essentials

- Snapshot API v1 (D6a): `POST checkout` → `GET document` (ETag + `X-Project-Version`) → `PUT document` with `If-Match` (releases lock unless `?keep=true`). 409 = lock/version conflict, 422 = document invalid, 404 = absent **or invisible**.
- Auth: `Auth:Authority` (JWT bearer) and/or `Auth:DevAuth` (Development only; `X-Dev-User` header; startup guard). Roles: reader/editor/owner; last-owner guard on members.
- Stale lock = idle > `Locking:StaleAfterMinutes` (default 30): editors steal stale, owners any (via `DELETE lock`).
- SSE `GET /{id}/events`: `checkout | checkin | lock-released` (in-memory broker, single-node).

## CLI

- Local: `--file`/single `.p27` in cwd. Remote: `--server` (P27_SERVER), `--project <name|id>`, `--token` (P27_TOKEN), `--dev-user`.
- Remote mutating verbs: fetch → mutate in-process → checkout+PUT; a lock whose `AcquiredAt != RefreshedAt` pre-existed (explicit `p27 checkout`) and is kept.
- Test seams: in-process invoke via `InvocationConfiguration{Output,Error}` (`CliHarness`); `RemoteClient.HandlerFactory` routes to a `WebApplicationFactory` TestServer (extern alias `server` — both hosts define `Program`).

## Web / command layer (phase 7) essentials

- `Project27.Core.Commands`: op-discriminated command records +
  `CommandExecutor` (throws `CommandException`; **no** implicit Recalculate).
  Undo/redo inverses deferred to phase 12.
- Server: `GET /{id}/schedule` (computed projection, server-owned DTOs — Core
  field catalog unifies projections in phase 9) and `POST /{id}/commands`
  (lock-held; returns `{version, createdUids, schedule}`, keeps the lock).
- `web/`: Vite + React 19 + TS 6, no UI deps. `npm run build` / `npm test`
  (Vitest, 21) / `npm run lint` (oxlint) — all part of "done". Dev proxy `/api`
  → `P27_SERVER` or `http://localhost:5240`.
- Pure logic in `web/src/lib/` (timescale, virtualize, drag, format) is
  unit-tested; components are thin over it. SSE via fetch-stream (EventSource
  can't carry auth headers). Both split panes scroll-synced; sticky headers.
- Playwright browser smoke deferred (no browsers in this environment) —
  tracked for phase 12.

## Tracking/EVM (phase 8) essentials

- Persistence is now **schema v3** (v1/v2 load). Baselines live per task
  (`TaskBaseline`) and assignment; `EarnedValue.ForTask/ForProject` computes
  on demand after `Recalculate()` (status date fallback: project finish).
- Actuals **pin** the scheduler in `ComputeEarly` before manual/constraint
  logic; `FinalizeDates` backfills implied actuals (%>0 → actual start,
  %=100 → actual finish) and actual spans rewrite `DurationMinutes`.
- `RescheduleUncompletedWork` reuses the split machinery; deviations #19–#23
  cover BCWS proration, derived actuals, interim plans, summary %, splits.

## Views & fields (phase 9, in progress) essentials

- **9a done**: `FieldCatalog` (Core.Fields) — key → `FieldDefinition(Key,
  Caption, Kind, Accessor)`; raw duration/work values are decimal **minutes**;
  `Format/Compare/ParseLiteral` are kind-driven. `Core.Views`: `FilterParser`
  (recursive descent; `~` = contains), `TaskView.Evaluate` + `Tables` (8
  built-ins) + `ParseSorts`. CLI: `p27 view`, `p27 field list`.
  `FieldCatalog.Resolve(project, key)` has the project parameter *specifically*
  so 9b custom fields/aliases can resolve there.
- **9b done**: custom field slots + aliases resolve through
  `FieldCatalog.Resolve` (also virtual `<field>.icon`); `FormulaEvaluator`
  (Core.Fields) has **duration literals** (`2d` → minutes) and a
  thread-static cycle guard (formulas re-enter via accessors). Values typed
  per kind; persistence **schema v4**; CLI `customfield define/list/remove`,
  `task set --field Name=value` ('none' clears). Counts at 9b close:
  Core 185, Storage 3, Cli 73, Server 17 (+ web 21).
- **9c done**: `Core.Usage.Timephased` — `ForAssignment/ForTask/Merge/ByWeek`;
  contour decile tables (same averages as `AverageUtilization`); exactly
  conserving via telescoped rounded cumulatives; lump costs by resource
  accrual. CLI `p27 usage [-g day|week] [--assignments] [--cost] [--filter]`.
  Note: BCWS's linear proration IS flat time-phasing of the baseline (no
  contour stored), so EVM was left as-is; deviation #19 now concerns accrual
  only. Usage *editing* deferred (needs actual-work model, phase 8's #20).
- **9d done**: server `GET /{id}/usage?granularity=day|week`; web view
  switcher (Gantt | Network | Timeline | Usage) in `ProjectView`; pure layout
  in `web/src/lib/network.ts` (rank = longest predecessor chain, summaries
  excluded) and `lanes.ts` (greedy interval packing). Projection unification
  deferred to phase 12 (documented in spec 09 §9d). Note: assignments are
  **not editable via commands yet** — server tests seed them via document
  check-in; add resource/assignment command ops when the web needs them.
- Counts at 9d close: Core 193, Storage 3, Cli 77, Server 18, web 28.

## Advanced scheduling (phase 10) essentials

- `ResourceLeveler` (Core.Scheduling): victim order **must** prefer
  already-delayed tasks or peers ping-pong on "most slack" forever.
  `Project.Level()/ClearLeveling()/FindOverallocations()`;
  `Task.LevelingDelayMinutes` (internal set) applied in ComputeEarly after
  bounds, skipped for forced-date paths. Schema **v5**.
- `TaskDrivers.Explain` (inspector) mirrors ComputeEarly read-only; binding
  detection compares `NextWorkingTime(imposed) == Start`. CLI `task drivers`.
- `Project.ImportResources(source)` = pool simplification (deviation #30);
  CLI `resource import --from file.p27`. Live pools = subprojects extension
  point (spec 10 documents the seams: JSON tolerates unknown members;
  external tasks ≈ manual-task islands; server snapshots per project id).
- Counts at 10 close: Core 203, Storage 3, Cli 81, Server 18, web 28.

## Phase 11 pointers (next)

- Scope (roadmap): dashboard/report set, PDF/PNG export, CLI report
  generation, print layouts.
- Realistic shape: Core/CLI report definitions producing **self-contained
  HTML** (tables + inline SVG charts reusing view/EVM/usage projections);
  `p27 report <name> [--out file.html]`; server `GET /{id}/reports/{name}`;
  web report page. PDF/PNG export needs a headless browser — document as
  print-to-PDF via the browser (no Chromium dependency in this environment);
  revisit bundling in phase 12.
- Report set: project overview (health, milestones, EVM summary), critical
  tasks, late tasks (vs baseline), resource overview (work/cost/overalloc),
  cost overview, upcoming tasks.
