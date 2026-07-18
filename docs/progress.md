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
| — | MCP server (epic) | **done** — see below | — |

Specs: `docs/spec/01…04, 06, 07, 08, 09, 14`. Deviations from MS Project: `docs/spec/deviations.md` (#1–#25).

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
- `Project.SetBaseline` reads the *current* `Start`/`Finish`/`Cost`, so it must run on an already-recalculated aggregate — every endpoint that loads a document must call `Recalculate()` right after `FromDocument` (not just at the end of a command batch), or a lone `setBaseline` command captures nulls.

## Persistence

- `ProjectDocument` SchemaVersion **2** (v1 loads; mapper gate `is not (1 or 2)`).
- One wire format: `Project27.Storage.ProjectDocumentSerializer` (string enums, ignore-null). `.p27` file: `meta` + single-row `snapshot`; server store: `projects/snapshots(history)/members/locks`, all TEXT/INTEGER, dialect-neutral SQL shared by `SqliteServerStore`/`PostgresServerStore`.

## Server (phase 6) essentials

- Snapshot API v1 (D6a): `POST checkout` → `GET document` (ETag + `X-Project-Version`) → `PUT document` with `If-Match` (releases lock unless `?keep=true`). 409 = lock/version conflict, 422 = document invalid, 404 = absent **or invisible**.
- Auth: `Auth:Authority` (JWT bearer) and/or `Auth:DevAuth` (Development only; `X-Dev-User` header; startup guard). Roles: reader/editor/owner; last-owner guard on members.
- Stale lock = idle > `Locking:StaleAfterMinutes` (default 30): editors steal stale, owners any (via `DELETE lock`).
- SSE `GET /{id}/events`: `checkout | checkin | lock-released` (in-memory broker, single-node).

## CLI

- Local: `--file` (`P27_FILE`)/single `.p27` in cwd. Remote: `--server` (`P27_SERVER`), `--project <name|id>` (`P27_PROJECT`), `--token` (`P27_TOKEN`), `--dev-user`.
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

## OIDC in the web SPA (2026-07-13, decision D5a)

`web/` now performs its own Authorization Code + PKCE login instead of only
accepting a manually pasted bearer token — production is not expected to sit
behind an auth proxy. `oidc-client-ts` is an approved exception to E26 (see
engineering-decisions.md); everything else stays dependency-free.

- `GET /api/auth/config` (unauthenticated, `Program.cs`) serves
  `Auth:Authority`/`Auth:ClientId`/`Auth:Scopes`/`Auth:DevAuth` so the SPA
  never bakes provider config into the build — swapping providers is a server
  config change only. New config keys default to
  `Auth:Scopes = "openid profile offline_access"` (`offline_access` for
  refresh-token rotation; must also carry whatever scope the provider maps to
  `Auth:Audience`, or issued tokens 401 against the API — see the doc comment
  on `AuthSetup.AddProject27Auth`).
- `web/src/lib/oidc.ts`: PKCE flow via `UserManager` — `beginSignIn`,
  `completeSignIn` (redirect target is `/callback`, works because both
  nginx's and Vite's dev fallback serve `index.html` for any path),
  `restoreSession` on reload, `watchForExpiry` (refresh-token grant when the
  provider issues one; falls back to a full `signinRedirect` on renewal
  failure — that redirect does interrupt an in-progress edit, a known
  tradeoff of not running a silent iframe renew, which needs 3rd-party
  cookies).
- `state/auth.ts` now persists a `Session` discriminated union
  (`dev`/`token`/`oidc`) instead of a single `Credentials` shape; OIDC tokens
  themselves live in `oidc-client-ts`'s own `localStorage`-backed store, not
  in ours.
- `api/client.ts`'s `Credentials.getToken` is re-read on every request so
  rotation is picked up without re-plumbing callers.
- `charts/project27`: added `auth.clientId`/`auth.scopes` values, wired to
  `Auth__ClientId`/`Auth__Scopes` env vars, so a real deployment can actually
  use this.
- Verified: `dotnet build` (0 warnings), `npm run build` / `lint` / `test`
  (32 passing) in `web/`, `helm lint`, and a live smoke check — started the
  dev server and `curl`'d `/api/auth/config`, started Vite and confirmed
  `/callback` resolves (200, SPA fallback). **Not done:** an actual IdP
  round-trip (needs a real OIDC provider registration) — no browser or
  external IdP available in this environment; recommended manual follow-up
  before relying on this in production.
- **Azure AD gotcha found via manual round-trip (2026-07-13): `Auth:Authority`
  must include `/v2.0`.** `oidc-client-ts` fetches the discovery document from
  exactly `{authority}/.well-known/openid-configuration` — it does not append
  `/v2.0`. `https://login.microsoftonline.com/<tenant-or-common>` (no
  suffix) resolves Azure's legacy v1/ADAL metadata (`issuer:
  https://sts.windows.net/{tenantid}/`, endpoints under `/oauth2/...` not
  `/oauth2/v2.0/...`), which for custom (non-Microsoft) API resources issues
  **opaque access tokens** regardless of the API's `accessTokenAcceptedVersion:
  2` setting — that manifest flag only takes effect when the v2 endpoint is
  actually used. Symptom fingerprint, in case it recurs: `id_token` decodes
  fine (JWT), `access_token` doesn't (`IDX14100: no dots` server-side), the
  `/token` response body has no `scope` field (v1 uses different fields), and
  the requested scope/consent/tenant all check out correctly — because none of
  those are actually the problem. Fix: use
  `https://login.microsoftonline.com/<tenant-id-or-common-or-organizations>/v2.0`.
  Cheapest way to confirm which metadata a given `Authority` value resolves
  to, before chasing consent/audience/account-type theories: `curl
  {authority}/.well-known/openid-configuration` and check `issuer` — a v2
  issuer is `https://login.microsoftonline.com/{tenantid}/v2.0`; a v1 issuer
  is `https://sts.windows.net/{tenantid}/`. This is a five-second, fully
  local, reversible check — run it before any live Azure-side change
  (`signInAudience`, admin consent) when access tokens come back opaque.

## MCP server epic (2026-07-18)

Spec: `docs/spec/14-mcp-server.md`. New host, `src/Project27.Mcp` (`p27-mcp`),
exposing Core's task/schedule/resource/calendar operations as MCP tools for
AI clients (Claude Desktop/Code, etc.), on top of the official
`ModelContextProtocol` SDK (stdio transport).

- Dual-mode like the CLI (D8): a local `.p27` file (`--file`/`P27_FILE`, else
  the sole `.p27` in the cwd) or a checked-out server project
  (`--server`/`P27_SERVER` + `--project`/`P27_PROJECT`, `--dev-user`/
  `P27_DEV_USER` or `--token`/`P27_TOKEN`). Mode is resolved once at process
  start; the server then serves one project for its whole stdio session —
  but unlike the CLI, the *project* doesn't have to be: `--project`/
  `P27_PROJECT` (remote) or a resolvable `--file`/`P27_FILE`/cwd (local) are
  now optional. Added after initial ship, prompted by the realization that a
  chat client asked to "create a project for what we just discussed" has no
  file/project to point the server at when it launches — restarting an
  MCP server mid-conversation isn't something the model can do. Without one,
  the session starts idle; `Session/ProjectSessionHost` forwards
  `IProjectSession` to whichever session is current and adds `create_project`
  (bootstraps a new local file or `POST /api/projects`) / `open_project`
  (attaches to an existing one) to establish it. Both reserve the session's
  "project slot" before doing any file/HTTP work and release it on failure,
  so a rejected create/open never leaves an orphan `.p27` file or checkout
  behind — the first version of this had exactly that bug (the guard ran
  *after* the file was already created) until a live smoke test caught it.
  Deliberately still one project per process (D1-scale simplicity, not full
  multi-project support): a second create/open call, including one that
  would follow an eager startup open, fails fast with a "restart the server"
  message.
- `Session/IProjectSession` is the mode-agnostic seam: `LocalProjectSession`
  opens the file directly via `Storage.SqliteProjectStore` and applies
  mutations through Core's existing `Commands.CommandExecutor` (no new
  mutation path — full reuse of the command layer built for the web client,
  E20); `RemoteProjectSession` checks the project out on open, applies
  mutations via `POST /commands`, and releases the lock on dispose only if
  this session acquired it (mirrors the CLI's inferred-keep rule, E19).
  Both recalculate once after load and once per mutation batch, matching
  every existing host (E2).
- Tools are grouped by entity, not 1:1 with `ProjectCommand` (~13 tools
  covering all ~35 command variants, plus `create_project`/`open_project`) —
  flatter tool list, smaller schema per tool, still a direct field-for-field
  mapping onto the command records so there's no parallel data model to
  maintain. Reads (`get_project`,
  `list_tasks`, `get_task`, `list_resources`, `get_task_drivers`,
  `get_usage`, `get_report`) reuse Core's view/report/tracking/usage
  building blocks directly for local mode and the server's matching GET
  endpoints for remote mode — same "projections are consumer-owned"
  pattern as CLI/server (E22), just a third consumer.
- Gotcha #1 worth flagging for future tool additions: the MCP C# SDK's
  reflection-based schema builder treats a nullable parameter as *required*
  unless it also has a `= null`/literal default — nullability alone isn't
  enough. Missing defaults surfaced as a runtime "arguments dictionary is
  missing a value" error during manual smoke testing, not a compile error.
- Gotcha #2, also found by live smoke testing rather than a unit test: the
  SDK swallows every tool exception to a generic "An error occurred invoking
  '<tool>'" string by default — only `ModelContextProtocol.McpException`'s
  `Message` is considered safe to forward to the client. All of this
  codebase's user-facing exceptions (`ProjectSessionException`,
  `ArgumentException`, `KeyNotFoundException`, `Commands.CommandException`)
  were being silently flattened to that generic string until `Program.cs`
  added a `WithRequestFilters(f => f.AddCallToolFilter(...))` that catches
  them and rethrows as `McpException`. Every hand-written validation message
  in this codebase (`"No task with uid {uid}."`, the create/open guards,
  etc.) depends on this filter to actually reach the model.
- Verified live for **both** modes, via a hand-rolled JSON-RPC/stdio client:
  local mode against a real example `.p27` file (initialize, tools/list,
  `get_project`, `list_tasks`, `task_write` add), then reopening the same
  file with `p27 task list --json` and confirming the CLI saw the
  MCP-added task; remote mode against a throwaway project on a real
  `Project27.Server` (DevAuth, localhost) — `get_project`, `list_tasks`,
  `task_write` add, `get_task_drivers`, `get_usage`, `get_report` all
  round-tripped through the server's checkout/commands/view/schedule/usage/
  drivers/reports endpoints, and `p27 task list` against the same server
  confirmed the write persisted. Both are genuine cross-host round-trips,
  not just unit tests. The idle-start path (no `--file`/`--project`) got the
  same live treatment for both modes: launched idle, confirmed `get_project`
  surfaces the real "no project open" message, `create_project` →
  `task_write` → a second `create_project` (confirmed rejected with no
  orphan file/checkout), each cross-checked via the CLI against the same
  file/server. `Project27.Mcp.Tests` covers `LocalProjectSession`,
  `ProjectSessionHost` (create/open guards, the no-orphan-file regression),
  and the tool classes directly (18 tests) — `RemoteProjectSession` has no
  automated test yet (would need a server fixture; the CLI/Server test
  projects' `WebApplicationFactory` harness is the natural base for a
  follow-up), so its live-server smoke tests above are its only coverage
  today.
- Counts at epic close: Core 237, Storage 3, Interop 9, Cli 90, Server 31,
  Mcp 18 (.NET 388).

## Tracking-parity epic (2026-07-18) — deviations #13/#14/#16/#17/#19/#20/#23/#28/#29

Spec: `docs/spec/13-tracking-parity.md`; rationale: E36. Scope locked with the
owner: #20 = *scalar actuals now, time-phased buckets later* (seam documented
in spec 13); leveling gets split-based + configurable order + minute
granularity; #14 = unify scheduling with the decile tables.

- Contour deciles single-sourced in `WorkContour.Deciles()`
  (`AverageUtilization` derived); the scheduler's closed form provably equals
  the decile walk — no date changes.
- `Assignment.Cost` = per-day usage slices priced by the band in force that
  day (`Timephased.WorkCost/MaterialCost`); buckets sum exactly to cost.
  Variable material consumption: `MaterialRateUnit` (+`MaterialQuantity`),
  CLI `--per`, command `unitsPer`.
- `ScheduleMask` confines split-task assignment work/dates to scheduled
  segments (resource calendars now shape split/manual assignments; #16).
- Scalar actuals: `Assignment.ActualWorkMinutes/ActualCost` (null = derived
  from % complete → old numbers unchanged); task rollups + fields
  `actualWork/remainingWork/actualCost`; ACWP = `task.ActualCost`; BCWS placed
  by resource accrual (#19). CLI `assign set --actual-work/--actual-cost`
  ('none' clears).
- `SplitSurgery.PushWork` (completed work never moves) shared by
  `RescheduleUncompletedWork` (#23, split tasks now handled) and
  `Level(LevelingOptions)` with `Order` (id/standard/priority),
  `Granularity` (day/minute = exact excess), `SplitInProgress` (#29);
  leveling skips unresolvable conflicts instead of stopping;
  `LevelingResult.SplitTasks` added. CLI `level run --order --granularity
  --split-in-progress`.
- Persistence **schema v7** (assignment `materialRateUnit`/`actualWorkMinutes`/
  `actualCost`); commands `setAssignment` + `level` extended, inverses done;
  projection carries the three new assignment fields.
- `CliHarness` now scrubs ambient `P27_*` env vars — without it a developer's
  `P27_SERVER` flips all file-mode CLI tests into server mode.
- Web: inspector gains per-select (material), actual work/cost inputs;
  "Level with options…" dialog (order/granularity/split-in-progress);
  `formatUnits` in `lib/format.ts`.
- Counts at epic close: Core 237, Storage 3, Interop 9, Cli 90, Server 28
  (.NET 367) + web 43 (Vitest).

## Checkout/lock/history UI now shows display names, not user ids (2026-07-15)

"Checked out by", the version-history "By" column, and project members all
used to render the raw `userId` (a JWT `sub`/dev-user id) — harmless under
DevAuth where id and name nearly coincide, but opaque under real OIDC. The
server had no way to resolve an arbitrary user's id to a name (`DisplayName`
in `ProjectEndpoints.cs` only reads the *caller's own* claims), so a new
`users(user_id, display_name)` table was added to `RelationalServerStore`,
populated best-effort by an `/api` endpoint filter on every authenticated
request (`IServerStore.RecordUser`, swallows `DbException` so a write
hiccup never fails the actual request). `LockDto`, `MemberDto`, and the new
`SnapshotDto` (history response) now carry both `userId` and a resolved
`displayName`/`savedByName`, falling back to the id for users who have
never authenticated (e.g. a member added but not yet signed in). Web reads
the new fields in `ProjectList.tsx`, `ProjectView.tsx`'s lock banner, and
`Managers.tsx`'s history table. Covered by two new
`Project27.Server.Tests` cases using a mixed-case dev-user header (id and
name only diverge that way under DevAuth) plus the never-signed-in fallback.

## Blank tasks retracted; cosmetic spacing added (2026-07-14, see E35)

The empty-name "blank spacer row" feature is gone — it let a zero-duration,
empty-name task participate in linking/assignment/CPM like any real task,
which is what let blank rows leak into the Network diagram (reported bug).
Replaced with `ProjectTask.Formatting?.SpaceAfter`, a cosmetic int on the
*existing* task (schema 6), never a row/entity of its own. Full rationale
and rejected alternatives (first-class blank `TaskKind`, uid-anchored "page
break" metadata, client-only storage) in `engineering-decisions.md` E35.

- Blank/whitespace task names are now rejected at every mutation entry
  point (`Project.AddTask`, `CommandExecutor.SetTask`, CLI `task set
  --name`) — but *not* on the `ProjectTask.Name` setter or the
  `RestoreTask` deserialization path, so schema ≤5 documents that already
  contain legacy blank tasks still load; they just can't be created or
  renamed-to-blank going forward.
- CLI parity (D4): `p27 task set <ref> --space-after N` (0 clears it).
- Web: `web/src/lib/displayRows.ts` (pure, unit-tested per E27) expands the
  task list into task rows + inert synthetic gap rows for the fixed-row
  virtualizer (`lib/virtualize.ts`); gap rows have no uid and are never
  sent to the server. `TaskSheet` and `Gantt` both consume the same
  `displayRows` list so the two panes stay row-aligned; `ProjectView.tsx`
  keeps two separate uid→index maps — `indexByUid` (domain, indexes into
  the plain `tasks` array, unchanged) and `displayByUid` (rendering,
  indexes into `displayRows`, only ever passed to `Gantt`). Toolbar
  "Space +"/"Space −" buttons operate on the current multi-selection.
- Verified live: `dotnet build`/`test` (344 passing, 0 warnings), `npm run
  build`/`test`/`lint` in `web/` (35 passing), and a real browser smoke
  test (system Chrome via a throwaway Playwright install, not a permanent
  dependency) — created a project, added two tasks, applied and cleared
  `spaceAfter` and watched the sheet/Gantt gap stay aligned, confirmed the
  Network view shows only real tasks, and confirmed clearing a task's name
  in the sheet reverts instead of sending an empty name.
