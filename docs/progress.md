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
| 7 | Web foundation | next | — |
| 8–12 | Tracking/EVM · Views · Advanced scheduling · Reports · Polish | pending | — |

Specs: `docs/spec/01…04, 06`. Deviations from MS Project: `docs/spec/deviations.md` (#1–#18).

## Build & test

- `dotnet build Project27.slnx` — must stay at **0 warnings** (TreatWarningsAsErrors).
- `dotnet test` — all suites; counts at phase 6 close: Core 145, Storage 3,
  Server 13, Cli 57 (218 total, all green).
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

## Phase 7 pointers (next)

- Scope: React+TS (Vite) app shell, auth flow, virtualized task sheet, custom
  SVG Gantt with drag/link, split views (`web/`, decision D2).
- Brings the **Core command layer** + `POST /projects/{id}/commands` (D6a) for
  fine-grained edits; web state = snapshot + command queue while holding the lock.
- Server already publishes SSE for live refresh; OpenAPI at `/openapi/v1.json` (Development).
- Web tests: component tests for grid/Gantt logic, Playwright smoke (architecture.md).
