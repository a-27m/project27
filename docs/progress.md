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
| 5 | Interop (MSPDI/CSV) | done after 12, as planned (b2b7dd4) | — |
| 6 | Server | done | 2bfdc88 |
| 7 | Web foundation | done | dd99a46 + cd38b54 |
| 8 | Tracking & EVM | done | ed5944d |
| 9 | Views & fields | **done** (a92e84e, 75ae639, 9cabb8d, f3e1edf) | — |
| 10 | Advanced scheduling | done (8a35e81) — **subprojects = extension point only** (user decision 2026-07-11; revisit at the very end, after 12/5; seams in spec 10) | — |
| 11 | Reports | done (3b38136) | — |
| 12 | Polish & web parity | **done** (02ff3c5, fd5a123, 50ea3b5, aed9069, 3e5ba5d, 4e4ab3a) | — |

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

## Reports (phase 11) essentials

- `Core.Reports.ReportBuilder.Render(project, name)` → self-contained HTML;
  `Available` lists the six reports. Reports re-present existing projections
  only. CLI `p27 report <name> [--out]`; server
  `GET /{id}/reports/{name}` (text/html); web Reports menu (blob URL tab).
- Counts at 11 close: Core 209, Storage 3, Cli 83, Server 19, web 28.

## Phase 12 state (web parity per spec 12 matrix)

- **User decision 2026-07-11: full web/CLI feature parity** — plan + matrix in
  docs/spec/12-polish.md. 12a (packaging/compose/guide), 12p-1 (21 command
  ops + `GET /view` + `GET /drivers/{uid}` + projection carries
  calendars/resources/custom-field defs/assignments/all task fields),
  12p-2 (TaskInspector: 6 tabs, everything editable), 12p-3
  (ResourcesView + ProjectSettings + Plan menu) are done.
- **12p-4 done**: TableView (server view engine), Manage menu with
  CustomFieldsManager + CalendarManager + RecurringTaskDialog
  (components/Managers.tsx), inspector Drivers tab. Parity matrix complete
  except usage editing (deviation #20) and file-local verbs.
- **12b done**: `CommandInverter.ApplyWithInverse` (Core.Commands) — inverse
  per op from pre-state; destructive ops (removeTask/removeResource/level/
  baseline/reschedule/calendar ops) return null = undo barrier. Commands
  endpoint returns `inverse` (reversed batch); web keeps undo/redo stacks
  (Ctrl+Z / Ctrl+Shift+Z, cleared on lock transitions). Modals focus + Escape;
  errors role=alert. Full keyboard grid-nav + axe run remain as noted gaps.
- Counts at 12 close: Core 217, Storage 3, Cli 83, Server 21, web 28.
- **Phase 5 done**: `Project27.Interop` — `CsvExporter` (view engine →
  RFC-4180), `MspdiWriter`/`MspdiReader` (1-based outline, PT-durations,
  link lag in tenths of minutes, hourly rates, baseline 0; lossy edges in
  deviation #32). CLI `export csv|mspdi`, `import mspdi`.
- **All roadmap phases (0–12 + 5) are complete.** Counts at close:
  Core 217, Storage 3, Interop 9, Cli 86, Server 21 (.NET 336) + web 28.
- **Open product decision — the subprojects revisit** (user deferred to the
  very end): the extension-point seams are in spec 10 (JSON tolerates new
  members; external tasks ≈ manual-task islands; server snapshots per
  project id; live pools = same cross-file concern). Options when picked up:
  (a) server-side cross-project links (external predecessor = read-only
  mirror task synced from another server project), (b) master projects as a
  web-side composition view, (c) leave as extension point. Needs a product
  call before implementation.

## Kubernetes deployment (epic, 2026-07-12) essentials

- `charts/project27/` — one flat Helm chart (no subcharts) covering both
  images CI already builds (`ghcr.io/<owner>/project27-server`,
  `-web`). Storage is **SQLite only** (Postgres from D1 explicitly deferred);
  server `replicas` is hardcoded at 1 in `server-deployment.yaml` (not a
  values knob) — per-project checkout/edit locks + SSE fan-out are in-process
  (D6), and SQLite has no concurrent-writer story, so replicas > 1 would
  silently corrupt state rather than degrade. Deployment `strategy.type:
  Recreate` (RollingUpdate would deadlock two pods on one RWO PVC).
- Routing is pluggable via `values.routing.provider`: `istio` (Gateway +
  VirtualService, default — `/api` routed before `/`, `timeout: 0s` +
  `retries.attempts: 0` on the whole `/api` route since Istio-level retry/
  timeout on the SSE stream (`GET /projects/{id}/events`) would corrupt or
  kill it), `ingress` (plain `networking.k8s.io/Ingress`, ingress-nginx
  annotation defaults, fully overridable for other controllers), or `none`
  (Services only). Both Service ports are named `http` — Istio silently
  falls back to opaque TCP (no path routing, no timeout/retry) on unnamed
  ports, easy to miss.
- `web-configmap-nginx.yaml` templates the web image's baked-in
  `nginx.conf` (which hardcodes `proxy_pass http://server:8080`) to the
  real Helm-computed backend Service name — keeps the web pod's `/api/`
  proxy working standalone (port-forward, `routing.provider: none`) even
  though Istio mode normally routes `/api` straight to the backend first.
- `auth.authority` uses a Helm `required` guard — fails `helm install`
  immediately instead of a crash-looping pod (D5's startup
  `InvalidOperationException` otherwise only surfaces via `kubectl logs`).
- Two small server-side prerequisites this chart depends on: unauthenticated
  `GET /healthz` (`Program.cs`, `AllowAnonymous()`, outside the
  `/api`-`RequireAuthorization()` group) for probes, and `USER $APP_UID` in
  `src/Project27.Server/Dockerfile`'s runtime stage (image ran as root
  before this) — paired with an explicit `chown` of `/data` in the same
  Dockerfile stage so the existing docker-compose eval flow keeps working
  under the new non-root user.
- Verified: `helm lint`, `helm template` for all three routing modes +
  `kubectl apply --dry-run=client` (istio CRDs excepted — no CRDs in a
  plain client-side dry-run without Istio installed), `dotnet build` (0
  warnings), full `dotnet test` (339 passing, incl. new `HealthzTests`).
  Not done: a live cluster smoke test (kind/minikube) — no cluster
  available in this environment; recommended manual follow-up.
