# Engineering decisions & rationale trace

Companion to `decisions.md` (which records *product* decisions D1–D9). This file
records the *engineering* decisions: what was chosen, what was rejected, why,
which constraints forced the choice, and the dead ends actually hit while
building. It is written for collaborators who were not here — assume nothing.

Conventions used below: **Chose / Rejected / Why / Trap** (the trap is what will
bite you if you "fix" the decision without understanding it).

---

## 1. Engine core

### E1. Scheduler: monotone worklist fixpoint, not topological sort

**Chose:** `ProjectScheduler` runs forward and backward passes as worklist
fixpoints — every leaf starts queued, a task is recomputed when dequeued, and
its dependents/parents are re-queued only if its dates changed. Dates only ever
move later (forward pass) or earlier (backward pass), so the iteration
converges. A guard counter (`1000 + size²`) turns non-convergence into a loud
`InvalidOperationException` ("engine bug") instead of a hang.

**Rejected:** classic topological-order CPM.

**Why:** the influence graph is not the dependency DAG. Summaries roll up from
children *and* links to/from a summary are inherited by all its leaves
(deviation #6), so a single link can couple whole subtrees in both directions;
manual tasks are fixed islands that still push successors. Deriving a correct
topological order over that combined relation is possible but fragile —
the monotone fixpoint gives the same result with no ordering proof to maintain.

**Trap:** any new scheduling input must preserve monotonicity. If you add a
rule that can move an early date *earlier* during the forward pass, the
fixpoint may oscillate and trip the guard. (Leveling delay, actuals pinning,
and assignment walks were all added as monotone: they only push dates later.)

### E2. Explicit `Recalculate()`, never auto-recalc on mutation

**Chose:** mutations touch inputs only; nothing is scheduled until
`Project.Recalculate()` is called. Hosts recalc at their boundaries: `.p27`
load, CLI verbs after mutation, server per command batch.

**Rejected:** recalculating inside every setter.

**Why:** batches (a 999-occurrence recurring task, an MSPDI import, a command
batch) would recalc N times; persistence restore would schedule half-built
aggregates; and determinism is easier to reason about when "inputs in →
`Recalculate()` → outputs out" is the only contract.

**Trap:** outputs (`Start`, `Cost`, assignment dates, EVM) are stale or null
before the first recalc. Tests that forget to call it get nulls, not errors.

### E3. Cycle detection over the influence graph, at `Link` time

**Chose:** `Project.WouldCreateCycle` does a DFS over "influence" edges:
child → parent (rollup) and link-predecessor → every node of the successor's
subtree. Checked when a link is created, so the scheduler can assume no cycles.

**Rejected:** detecting cycles during scheduling (guard-counter only).

**Why:** the user needs the error at the moment they create the bad link, with
names in the message — not a generic "did not converge" later.

### E4. Time is wall-clock `DateTime` (unspecified kind), minutes are `decimal`

**Chose:** no time zones anywhere in the engine; all date arithmetic funnels
through the calendar engine; working amounts are decimal **minutes** at every
raw layer (durations, work, slack, lag), converted to days/hours only for
display.

**Rejected:** `DateTimeOffset`/UTC normalization; `TimeSpan`; `double`.

**Why:** project plans are wall-clock artifacts ("work starts at 08:00")
— zone math adds DST cliffs with no user value at this layer. `decimal`
because money is derived from minutes; `double` would leak binary noise into
costs. `TimeSpan` conflates elapsed and working time, which are different
number lines here.

**Trap:** see E8 for the decimal-dust consequences.

### E5. Working-time intervals are start-inclusive, end-exclusive — and the
zero-width boundary

`IsWorkingTime(17:00)` is false for a 13:00–17:00 interval, but 17:00 is a
valid *finish* (interval ends count when walking backward). Consequence:
"finish Wed 08:00" and "finish Tue 17:00" are the same instant in working
time (zero width between them) and either can appear depending on which bound
drove the calculation. This is documented as deviation #8 rather than
"fixed", because normalizing every result to interval ends costs a pass and
breaks the symmetry of `AddWork`/`WorkBetween`.

**Trap:** it resurfaced in phase 4 — a leveling/assignment *delay* landing
exactly on an interval end must be snapped forward (`NextWorkingTime`) because
a *start* belongs at the next interval even though the instant is "valid".
If a computed **start** looks a day early at 17:00, this is why.

### E6. `IWorkSchedule` + extension-method arithmetic (phase 4 refactor)

**Chose:** the working-time arithmetic that lived as instance methods on
`WorkCalendar` was moved verbatim into `WorkScheduleArithmetic` — extension
methods over a two-member interface `IWorkSchedule { Name; GetDaySchedule(day) }`.
`ScheduleIntersection` implements it by intersecting two schedules' intervals
per day.

**Rejected:** (a) duplicating the walking logic for intersections;
(b) making `WorkCalendar` compose intersections internally (it's a
user-visible entity with persistence identity — a synthetic intersected
calendar would leak into calendar lists and documents).

**Why:** assignment scheduling needs task-calendar × resource-calendar
arithmetic. Extension methods kept **every existing call site compiling
unchanged** (`calendar.AddWork(...)` binds to the extension), which is what
made the refactor safe mid-project.

### E7. The task-type triangle runs at **edit time**, not schedule time

**Chose:** `Work = Duration × Units × avg(Contour)` is enforced by the
mutation surface — `Assignment.SetWork/SetUnits/SetContour`, the
`ProjectTask.Duration` setter, and `Project.Assign/Unassign` — via
`EffortTriangle`. The scheduler never rebalances the triangle; it just walks
whatever inputs exist.

**Rejected:** resolving the triangle inside `Recalculate()`.

**Why:** MSP's semantics are *edit-order dependent* ("which corner did you
touch last"), which is unrepresentable in a pure recalculation; and the
scheduler must stay a pure function of inputs (E2).

**Trap #1:** persistence restore must **bypass** the triangle (documents store
all three corners). This works by *ordering*: the mapper restores tasks (and
their durations) before any assignments exist, and assignments through
internal raw setters. If you reorder the mapper, imports will silently
rebalance.
**Trap #2:** the `Duration` setter fires the triangle; internal code that must
not (scheduler rollups, actual-span rewrites) uses `DurationMinutes`'s
internal setter or `SetDurationFromMinutes` instead.

### E8. Money and conservation: divide last, telescope rounded cumulatives

Two dead ends, both from `decimal` division:

1. Cost as `minutes × ratePerMinute` gave `50/60 = 0.8333… × 960 = 799.99968`.
   **Fix:** `Rate.CostForMinutes` computes `minutes × amount / minutesPerUnit`
   — division last, exact for whole units.
2. Time-phased buckets computed per-day as `total × shareOfDay` summed to
   `479.999…95` / `480.000…10`. **Fix:** `Timephased` keeps *rounded
   cumulative* work/cost (8 dp) and emits differences — the telescoping sum is
   exactly the total by construction.

**Trap:** if you add a new distribution (e.g. usage editing), use the
telescoped-cumulative pattern, not per-slice rounding.

### E9. Assignment-driven scheduling: `finish = max(duration walk, assignment walks)` — duration is not rewritten

**Chose:** when a task has real work assignments, its finish is the latest of
the plain duration walk (task calendar) and each assignment's walk on its own
schedule. The *entered duration stays untouched* even when a restrictive
resource calendar stretches the span (deviation #18); the backward pass uses
an inverse per-assignment walk that is exact when that assignment binds.

**Rejected:** MSP's behavior of rewriting the duration field from the
assignment span.

**Why:** inputs staying inputs is what makes snapshots re-loadable and the
triangle sane; the bar and slack still reflect reality because they derive
from the computed finish.

### E10. Actuals pin the schedule ahead of everything

`ComputeEarly` checks actuals **before** manual mode and constraints: an
actual start pins the start, an actual finish pins both ends (and rewrites
`DurationMinutes` to the actual span, like manual tasks do). Backfill happens
in `FinalizeDates` (%>0 ⇒ actual start = start; %=100 ⇒ actual finish), which
is an *input mutation during recalc* — accepted, precedent set by manual
tasks, because "% complete without an actual start" is not a stable state.

**Trap:** `RescheduleUncompletedWork` deliberately reuses the split machinery
(completed part + gap to the cutoff) instead of introducing a second
interruption concept. In-progress tasks that are *already* split are skipped
(deviation #23) — splitting a split is representable but the semantics were
not worth defining yet.

### E11. Leveling: the ping-pong dead end

First implementation ordered victims by (priority, most slack, latest start,
row). With two equal peers, delaying A gives B zero slack, so the next
iteration picks A again… B… — both accumulated ~300 days of delay before the
guard tripped. **Fix:** insert "already carries leveling delay" as the second
ordering key. Once someone is the victim, they stay the victim until their
conflict clears. Whole-day delay steps (not minute-level) keep iteration counts
sane; the day granularity matches the overallocation detection (per-day demand
vs `MaxUnits × day capacity`).

---

## 2. Persistence & interop

### E12. Snapshot documents, not normalized rows, not events

**Chose:** the persisted form is one JSON `ProjectDocument` — *inputs only*
(no computed dates), schema-versioned, wrapped in SQLite for `.p27` files and
in a `snapshots` history table on the server. One serializer
(`ProjectDocumentSerializer`) is the single wire format everywhere.

**Rejected:** normalized relational schema (joins for every load, migrations
for every phase); event sourcing (replay cost, versioning every mutation).

**Why:** the aggregate is small (thousands of tasks, not millions), always
loaded whole (the scheduler needs everything anyway), and "inputs only +
recalculate on load" makes forward compatibility mostly free: v1 files load
in the v5 mapper because new members default.

**Trap:** *additive* schema evolution only — new members with safe defaults,
version gate widened (`is not (>= 1 and <= 5)`). Renaming/retyping a member is
a breaking change requiring real migration code that does not exist yet.

### E13. Restore paths are internal methods that bypass invariants

`RestoreTask`, `RestoreLink`, `RestoreAssignment`, `RestoreTracking`,
`RestoreEntries`, `RestoreSplitParts` skip validation and side effects
(triangle, milestone inference, name-uniqueness timing) because documents are
trusted to be self-consistent — they were produced by the same code.
`InternalsVisibleTo` grants them to `Project27.Interop` too, so MSPDI import
can restore exact assignment values.

**Trap:** never route restore through public mutators "for cleanliness" — see
E7 trap #1. Conversely, never expose restore methods publicly; they can build
invalid aggregates.

### E14. MSPDI: the mappings that are *not* what you'd guess

- MSPDI enum codes differ from ours: ConstraintType is `0 ASAP, 1 ALAP,
  2 MSO, 3 MFO, 4 SNET, 5 SNLT, 6 FNET, 7 FNLT` (ours orders SNET/SNLT
  earlier); link Type is `0 FF, 1 FS, 2 SF, 3 SS`. Explicit mapping functions
  in `Mspdi.cs`; do not "simplify" to enum casts.
- OutlineLevel is 1-based there, 0-based here (deviation #10) — the writer
  adds one, the reader subtracts via hierarchy reconstruction.
- Link lag is in **tenths of minutes**; elapsed lags ride `LagFormat 5`.
  Percent lags have no MSPDI form and are materialized to working minutes at
  export (deviation #32).
- Import cannot keep task UIDs (our `AddTask` assigns fresh ones), so
  assignments are matched **by document position**, which is stable because
  tasks import in document order.
- Calendar exceptions use the classic `WeekDay DayType=0 + TimePeriod` shape,
  not the newer `<Exceptions>` element — broader compatibility, one code path.

### E15. CSV export rides the view engine

No bespoke CSV column logic: `CsvExporter` takes a `ViewDefinition` and quotes
the *formatted* cells. Grouped views become a `Group` column because heading
rows inside CSV break consumers.

---

## 3. Server & CLI

### E16. Two write paths on purpose: PUT document and POST commands

Phase 6 shipped snapshot writes only (`PUT document` with `If-Match`) because
the CLI embeds the engine — fetch, mutate in-process, check in — and needed no
granular API (product amendment D6a). Phase 7 added `POST commands` for the
web. Both remain: the CLI keeps whole-document round-trips (simplest correct
thing for a client that has the engine), the web sends command batches (it has
no engine). They share the same lock/version discipline, so they can't corrupt
each other: check-in requires holding the lock **and** the version you read.

**Trap:** the version check is what makes stolen locks safe. Don't "optimize"
`SaveSnapshot`'s expected-version comparison away.

### E17. Dialect-neutral SQL over an ORM

`RelationalServerStore` is one class of literal SQL (TEXT/INTEGER columns,
Guids as strings, timestamps as ISO-8601 `"O"` strings, upserts via
`ON CONFLICT`) with providers supplying only `DbConnection`s. Works unchanged
on SQLite and PostgreSQL.

**Rejected:** EF Core (migrations, model ceremony for four tables), Dapper
(another dependency for string SQL we can write ourselves), per-dialect SQL
branches.

**Trap:** every statement must stay in the common subset. `ALTER TABLE …
ADD COLUMN IF NOT EXISTS` is Postgres-only, SQLite needs try/catch — schema
evolution here is manual (see the snapshots `label` migration).

### E18. DevAuth as a forwarding policy scheme + startup guard

One `AddPolicyScheme("Smart")` forwards per request: `X-Dev-User` header (and
DevAuth enabled) → DevAuth handler; otherwise JWT bearer. The server *throws at
startup* if DevAuth is enabled outside the Development environment — a
constructor-time guard, not a request-time check, so a misconfigured
production deployment cannot come up at all.

### E19. CLI remote mode: sync-over-async, and the stateless lock heuristic

- `RemoteClient` uses `SendAsync(...).GetAwaiter().GetResult()`. Plain
  `HttpClient.Send` (sync) is unusable because the test seam routes through
  `WebApplicationFactory`'s TestServer handler, which only implements
  `SendAsync`. There is no SynchronizationContext in the CLI, so no deadlock.
- Should a mutating verb *release* the lock after saving? Yes if this
  invocation acquired it, no if the user ran `p27 checkout` earlier. The CLI
  is stateless between invocations, so this is inferred from the checkout
  response: `AcquiredAt != RefreshedAt` ⇒ the lock pre-existed ⇒ keep it.
  A crashed run can leave a kept lock; `p27 unlock` recovers. Chosen over
  local state files (sync/cleanup problems) and over a server-side "session"
  concept (worse).
- Both the CLI (`p27`) and the server define top-level `Program`; the test
  project referencing both disambiguates with `<ProjectReference
  Aliases="server">` + `extern alias server;`. You cannot rename either: one
  is the tool's assembly name, the other is WebApplicationFactory's anchor.

### E20. Commands: JSON polymorphism, absent-vs-clear, uid addressing

- `[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]` + one
  `[JsonDerivedType]` per record. Chosen over a `{op, args}` envelope with
  manual dispatch: the attributes give schema-checked payloads for free.
- JSON cannot distinguish "member absent" from "member null" through
  `System.Text.Json` POCOs, so *clearing* an optional field is an explicit
  boolean (`clearDeadline`, `clearStatusDate`, …). Custom-field values are the
  one exception: a `Dictionary<string, string?>` where null means clear,
  because the dictionary entry's *presence* is the "touched" signal.
- Tasks are addressed by **uid** (stable), never row number (reassigned on
  outline changes). Resources/calendars by unique name — they have no row
  concept and names are enforced unique (deviation #11 exists partly for
  this).
- The executor does **not** recalculate (E2); the host recalcs once per batch.

### E21. Undo/redo: inverses computed server-side, stacks client-side

The client cannot build inverses (it lacks pre-state truth), so
`CommandInverter.ApplyWithInverse` captures the inverse *on the server, from
the aggregate, before applying*. The commands endpoint returns the reversed
inverse batch; the web pushes it on an undo stack. Undo = POST the inverse
batch — and because *that* response carries its own inverse, redo falls out
with zero extra machinery. Destructive ops (`removeTask`, `removeResource`,
`level`, baselines, reschedule, calendar edits) return null inverse = undo
barrier: the stack clears rather than lie about undoability. `RemoveTask`
restore was deliberately not implemented — resurrecting a subtree with links,
assignments, baselines, and custom values is a full serializer in disguise;
the confirm dialog covers the risk.

### E22. Projections are consumer-owned; unification was rejected twice

Three projection layers exist: CLI `JsonShapes` (stable `--json` contract),
server `ScheduleProjection` (web's wire shape), and the Core field catalog
(views/reports/CSV). Architecture originally wanted one Core projection.
After the field catalog landed (9a), unifying the other two was evaluated and
**rejected**: both wire contracts were stable, consumer-specific, and churning
them bought nothing users can see. The catalog *is* the unification for
everything new (views, reports, CSV, web table view use it). Revisit only with
a concrete driver.

---

## 4. Fields, views, formulas

### E23. Field catalog: `object?` raw values, kind-driven behavior

`FieldDefinition(Key, Caption, Kind, Accessor)` returns raw values (string /
decimal / DateTime? / bool / int); `FieldKind` drives formatting, comparison,
and filter-literal parsing. Chosen over generic `FieldDefinition<T>` (kills
homogeneous catalogs) and over pre-formatted strings (kills sorting/filtering).
Durations/work are decimal minutes at this layer (E4). `Resolve(project, key)`
takes the project *specifically* so custom fields and aliases could join later
without signature churn — that foresight paid off in 9b.

EVM values are fields too, recomputed per access without memoization: CLI-scale
projects make that fine; measure before caching.

### E24. Formula evaluator: the cycle guard must be thread-static

Formulas reference fields; custom formula fields *are* fields; so evaluation
re-enters through `FieldCatalog` accessors, and a parse-time or
call-stack-local depth check cannot see the recursion (each accessor call
starts a fresh evaluation). The guard is a `[ThreadStatic]` active-evaluation
counter. Definition-time parsing still catches syntax errors eagerly.
Duration literals (`2d` → minutes via project settings) are an extension over
MSP's language, added because filters already had them and mixed syntax would
be crueler than a deviation (#26).

### E25. Views flatten on sort/group (deviation #25)

Sorting inside a hierarchy ("keep outline structure") multiplies edge cases
(summaries between sorted leaves, group headings vs subtree order). Flattening
to leaves under sort/group is deterministic and matches one of MSP's own modes.
Unsorted views keep outline order with summaries.

---

## 5. Web client

### E26. Zero runtime dependencies beyond React

No grid, Gantt, state, or router libraries (product constraint D2 for
Gantt/grid; extended to everything because each dependency is a supply-chain
and upgrade liability). Consequences that look odd but are intentional:
hash-based routing in `App.tsx`, hand-rolled virtualization
(`lib/virtualize.ts`), custom SVG Gantt, `localStorage` state helpers.

**Amendment (2026-07-13):** `oidc-client-ts` is an approved exception. OIDC
Authorization Code + PKCE, token refresh/rotation, and callback handling are
security-sensitive enough that hand-rolling them is a worse liability than the
dependency itself — a subtly wrong `state`/PKCE check or refresh race is an
auth bypass, not a UI bug. The library is provider-agnostic (any standard
OIDC/OAuth2 issuer), and the SPA still never validates token signatures —
that stays server-side in `JwtBearer` (D5). No other exceptions are implied;
this does not reopen the router/grid/state-library constraints.

### E27. Pure-logic modules + thin components

Anything with arithmetic or a state machine lives in `web/src/lib/*` as pure
functions with Vitest coverage (timescale math, windowing, drag state
machines, network ranking, lane packing, formatting). Components stay thin
because **there is no browser in the dev loop** — this environment cannot run
Playwright — so correctness must live where `vitest run` can reach it.
Components are verified by type-checks, lint, and live API smoke tests.

### E28. SSE over fetch-streams, not EventSource

`EventSource` cannot send headers, and auth here is header-borne (bearer or
`X-Dev-User`). So the client reads the SSE stream via `fetch` +
`ReadableStream` with a hand-rolled `event:`/`data:` parser. Cookie-based auth
would unlock native EventSource but was rejected (CSRF surface, JWT ubiquity
in the OIDC world). Server-side, nginx needs `proxy_buffering off` for SSE —
already in `web/nginx.conf`.

### E29. Split view scroll-sync: two scrollers mirroring, not one scroller

First attempt: one vertical scroller containing both panes (grid layout with
external headers). Died on the interaction between per-pane *horizontal*
scrolling and sticky headers. Final: each pane scrolls independently and
mirrors `scrollTop` to the other in `onScroll` (guarded by "only set if
different" to stop feedback). Headers are sticky *inside* their panes so they
scroll horizontally with content.

### E30. Editing round-trip: server truth after every batch

Every edit posts a command batch and **replaces the whole schedule** from the
response — no optimistic local mutation. One round trip per edit is the
latency cost; in exchange there is no client-side reconciliation logic and the
web can never drift from engine semantics. Undo stacks (E21) piggyback on the
same responses.

---

## 6. Cross-cutting build & test lore

### E31. Warnings-as-errors with `latest-recommended` analyzers shapes names

Analyzer rules changed public names — accept them rather than fight:
`FilterNode.AllOf/AnyOf/Negation` (CA1716 reserves And/Or/Not),
`FieldKind.WholeNumber` (CA1720 bans Integer), `FormulaNode.Invocation`
(CA1716 reserves Call), `CliException` (CA1710 suffix rule), interface
members need explicit `public` (IDE0040), CA1859 pushes concrete return
types on private helpers.

### E32. Pinned vulnerable transitives

`SQLitePCLRaw.bundle_e_sqlite3 3.0.3` (GHSA-2m69-gcr7-jv3q via
Microsoft.Data.Sqlite) and `Microsoft.OpenApi 2.10.0`
(GHSA-v5pm-xwqc-g5wc via Microsoft.AspNetCore.OpenApi) are explicit
`PackageVersion` pins with comments. NU1903 is an *error* here; removing the
pins breaks the build by design.

### E33. Test-harness seams (all deliberate, all minimal)

- CLI tests run the real command tree **in-process** via
  `Parse(args).Invoke(new InvocationConfiguration { Output, Error })` —
  no process spawning, exact stdout/stderr assertions.
- `RemoteClient.HandlerFactory` (internal static) lets CLI tests route HTTP
  into a TestServer. Only one test class may use it (static seam + xunit
  class-level parallelism).
- Server tests: `WebApplicationFactory` + DevAuth + per-fixture temp SQLite;
  `ServerFixture` has a parameterless ctor because xunit fixtures cannot take
  arguments (optional-parameter ctor fails resolution — dead end hit);
  the stale-lock variant is a static factory method instead.
- xUnit v3 specifics that cost time: `Assert.Throws<T>` is exact-type
  (JsonException vs derived JsonReaderException failed), analyzer xUnit1051
  demands `TestContext.Current.CancellationToken` on every CT overload,
  and there are no global usings — every test file needs `using Xunit;`.

### E34. Assorted dead ends worth not repeating

- `Where(set.Contains)` on a value-type receiver → CS1113 (no extension-method
  delegates on value types); use a lambda.
- Building a command batch as a collection expression that *queries the
  project* for uids evaluates the query **before** earlier commands in the
  batch have run. Resolve uids from prior `Apply` return values instead.
- MSP contour decile tables in `Timephased.Deciles` must keep the same
  averages as `WorkContourExtensions.AverageUtilization` — the triangle uses
  the average, the distribution uses the deciles; if they diverge, bucket sums
  stop matching assignment work.
- `dotnet test proj1 proj2` is not a thing (MSB1008); loop or use the solution.
- `DateTime.Today`-relative logic in reports (`upcoming`) needs a status-date
  override for testability — the report takes `StatusDate ?? Now`, tests set a
  status date.

### E35. Blank rows retracted as tasks; cosmetic spacing lives on the task as a formatting bag

**Chose:** the empty-name "blank spacer row" task kind was removed outright —
`ProjectTask.Name`/`SetTaskCommand.Name` now reject blank/whitespace at every
mutation entry point (`Project.AddTask`, `CommandExecutor.SetTask`, the CLI
`task set --name` path — deliberately *not* the `Name` setter or the
`RestoreTask` deserialization path, so schema ≤5 documents with legacy blanks
still load; they just can't be created or renamed-to-blank anymore). Purely
cosmetic vertical spacing is now `ProjectTask.Formatting?.SpaceAfter` (schema
6, `TaskFormattingDocument`) — a nullable bag on the *existing* task, not a
new row/entity. The web never sends it to the server as a display concept:
`web/src/lib/displayRows.ts` expands the real task list into task rows plus
inert synthetic gap rows client-side, purely for the fixed-row-height
virtualizer (E27); gap rows have no uid, are never selected, never receive
keyboard focus, and are invisible to Network/Gantt link logic.

**Rejected:**
1. Blank rows as a first-class `TaskKind.Blank` that still lives in the
   outline as a real, uid-addressed task, with the engine (`Link`,
   `AddAssignment`, CPM) hardened to reject/exclude it. This was the
   original fix under consideration — it centralizes correctness in Core,
   but was overtaken by the product call to drop the feature entirely rather
   than keep policing a "task that isn't really a task."
2. "Page break after task #uid" as metadata anchored to a task uid but
   modeled as a Core-level construct in its own right. Rejected for the same
   reason as (1) plus new edge cases (anchor task deleted, gap before the
   first row) that a plain per-task property doesn't have.
3. Storing spacing purely client-side (`localStorage`/a UI-preferences
   blob), never persisted server-side. Rejected: this is a shared,
   checked-out/checked-in document (D6); spacing one editor adds should
   survive a checkout from a different machine or by a different user, not
   silently reset.

**Why:** the original blank-row feature let an empty-name, zero-duration
task participate in everything a real task can — `Link` and `AddAssignment`
had no guard against it, so it could pick up predecessors/successors and be
pulled into the critical-path graph. The Network diagram bug (blank rows
rendering as nodes) was a symptom of that, not the root cause: `network.ts`
already excludes summaries but nothing excluded blanks, and the same
`task.name === ''` convention was independently re-implemented in
`TaskSheet.tsx` and `Gantt.tsx` and simply missing in `network.ts` and in the
engine. A property that was never a graph node in the first place can't leak
into CPM, WBS, Network, or MSPDI/CSV export — there's no entity to
remember to filter.

**Trap:** `CommandInverter.PrepareInverse` runs *before* `CommandExecutor.Apply`
(captures pre-mutation state), so the `SpaceAfter` inverse is
`task.Formatting?.SpaceAfter ?? 0`, not a post-apply read. The executor
collapses `Formatting` back to `null` via `TaskFormatting.IsDefault` (checks
*every* field), not `SpaceAfter == 0` specifically — when the next cosmetic
field (color, etc.) is added, extend `IsDefault`, not the call site, or the
bag will stop collapsing correctly once two fields disagree on default-ness.

---

### E36. Shell completion resolved in-process; System.CommandLine's own completion measured and rejected

**Chose:** a `__complete` command (kubectl/cobra shape) whose candidates come from
`Completion/CompletionEngine.cs` walking the tree `CliRoot.Build()` returns, plus
`p27 completion <shell>` printing an embedded script.

**Rejected:** System.CommandLine 2.0.10's `Option.CompletionSources` +
`ParseResult.GetCompletions()`, and the `[suggest]` directive over it.

**Why:** both of its paths were probed and both are unusable *for values containing
spaces*, which is the central case here — projects are `"Alpha Project"`, resources
are `"Alice Smith"`:

| Path | Measured behaviour |
|------|--------------------|
| `Parse(string).GetCompletions()` | `InvalidOperationException` ("Sequence contains no matching element") on **any** quoted value. Unquoted-with-space silently completes the wrong symbol. |
| `Parse(string[]).GetCompletions()` | An option's `CompletionSources` **never fire**; `--project <TAB>` returns sibling commands. Option-value completion only works via `TextCompletionContext`, i.e. only via the throwing path. |

The engine only ever *introspects* the real symbols (`Subcommands`, `Options`,
`Aliases`, `Arity`, `Description`), so the command surface is still declared once.
The shell hands us argv it has already dequoted, so no tokenizer runs on our side
and the quoting bug has no place to occur.

**Trap:** recursive options (`--file`, `--server`, `--json`…) are **not** present in a
subcommand's `.Options` — `task add --<TAB>` must union the current command's options
with the recursive ones of every ancestor, or the global flags vanish at depth.

**Trap:** `Arity.MinimumNumberOfValues == 0` — not the type — is what says an option
does not consume the next word. `Option<bool>` is arity 0..1, so testing the maximum
would make `--json <TAB>` complete a value instead of the next subcommand.

**Trap:** completion runs `CliContext` for real, because `P27_SERVER`/`P27_PROJECT`
put the CLI in server mode with nothing on the command line to show it. Use
`OpenProjectForCompletion` (timeboxed, no lock) — never `OpenProject`, whose remote
save path checks the project out. Pressing TAB must not take a lock.

**Trap:** a test that drives the script must run p27 from the **CLI's own output
directory**, not from the test binary's. `Project27.Cli.Tests` references the server, so
its output is built against the ASP.NET shared framework and MSBuild prunes assemblies
that framework provides — `Microsoft.Extensions.Configuration` among them. The test host
loads those from the framework, but the `p27.dll` copied alongside declares only
`Microsoft.NETCore.App` and aborts with `FileNotFoundException` (exit 134). Whether a
given assembly is pruned depends on the installed runtime, so this passes locally and
fails on CI. The .csproj captures the CLI's output path as assembly metadata for the shim.

**Trap:** the script sends p27's stderr to `/dev/null` — right at a prompt, fatal in a
test. When the above bit, all five tests reported `Actual: ""` and nothing else, and a
plausible-but-wrong cause (a missing apphost) reproduced the same symptom locally. The
harness now probes `command -v p27`, captures p27's stderr, and prints both on failure;
that turned a guess into the exact exception in one CI run. Diagnose from the failure,
not from the first theory that reproduces the symptom.

**Trap:** in the bash script, read `__complete`'s lines in the completion function
itself. A `while read … < <(helper)` runs the helper in a subshell, so a directive it
sets is lost and path completion silently does nothing — this shipped broken until a
real shell was driven.

**Trap:** the bash script must stay **bash 3.2**-clean — macOS still ships 3.2 as
`/bin/bash`, and it rejects negative array subscripts outright (`${a[-1]}` →
"bad array subscript", so the `:none` directive ends up in `COMPREPLY`). Verifying
against Homebrew's bash 5 hides this; `CompletionScriptTests` runs `/bin/bash` for
exactly that reason.

**Trap:** fzf only delegates a triggerless TAB back to the completion it replaced if
that completion existed when **fzf** loaded. bash-completion lazy-loads our script on
the first `p27<TAB>`, long after — so `__fzf_defc` records no original and plain TAB
returns nothing. The script therefore checks `FZF_COMPLETION_TRIGGER` itself and calls
`_p27_completion` directly rather than trusting fzf's delegation.

**Trap:** in the zsh fzf helper, `${(z)lbuf}` splits into words but **keeps the quotes**,
so `-p "Alpha Project"` reaches `__complete` quoted and matches nothing. `${(Q)${(z)lbuf}}`
strips them — the normal `$words` path is already dequoted and needs no such care. The
quoting bug this whole design avoids can still sneak back in through fzf.

---

*When you add a significant engineering decision, append an E-record here in
the same Chose/Rejected/Why/Trap shape — especially the traps.*
