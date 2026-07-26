# Epic 15 — Gantt maturity

The Gantt is the product's face. Today it is a competent 369-line SVG component
(`web/src/components/Gantt.tsx`): bars, one baseline ghost, orthogonal links,
drag-to-reschedule, drag-to-link, a five-step px/day zoom, a two-band header. That is
roughly where a good open-source Gantt library stops. Our engine is far past that
point — calendars with exceptions and work weeks, splits, contours, ten baseline
slots, leveling delay, drivers, EVM, usage — and almost none of it is visible on the
chart. This epic closes that gap: the chart should show what the engine actually
knows, at every time resolution from an hour to a decade, on a touchscreen, without
colour being the only carrier of meaning.

Two framing constraints, both non-negotiable:

- **E26 stands.** Zero runtime dependencies beyond React. No zoom library, no gesture
  library, no charting library, no virtualization library. Pure logic goes to
  `web/src/lib/` with unit tests; components stay thin.
- **D4 stands.** Every operation the web gains here has a CLI equivalent. Anything
  that becomes document state gets a command, a server-computed inverse (D6a), a
  persistence bump, and an MSPDI round-trip decision.

## Where each new piece of state lives

The most expensive mistake available in this epic is putting state in the wrong
bucket. Every feature below is assigned to exactly one.

| Bucket | Mechanism | Precedent | Carries |
|---|---|---|---|
| **A — Ephemeral client** | `useState` in `ProjectView` | `zoomIndex`, `showBaselineGhosts` | Current scroll position, in-flight drag/gesture, hover, focus-mode target, transient "go to date" |
| **B — Per-user, server-persisted** | `GET/PUT /projects/{id}/preferences`, `ColumnPreferencesDto` + `web/src/lib/preferences.ts` | Column selections | Zoom tier + continuous factor, timescale tier config, which baseline slots are shown, colour-encoding mode, bar-text field choices, shading/progress-line toggles, overview-scrubber visibility |
| **C — Document (shared, versioned, undoable)** | Core aggregate → command + inverse → `.p27` | `TaskFormatting.SpaceAfter` (E35, deviation #33) | Bar-style rules, per-task colour role override, time markers (curtains/flag lines) |

Bucket B is a *shape* extension of the existing preferences DTO, not a new endpoint:
`ColumnPreferencesDto` grows a **`Chart`** view-settings object with every field
optional. The name matters — `Gantt` is already taken on that record for the Gantt
view's *column* keys (`ServerModels.cs:49-52`) — and the whole DTO is persisted as an
opaque per-user JSON string (`RelationalServerStore.GetPreferences/SetPreferences`,
`RelationalServerStore.cs:377/387`), so adding an optional member needs no schema
migration and old rows deserialize with `Chart = null` → defaults.

Bucket C is the only bucket that touches Core, the CLI, persistence, and interop —
and it is deliberately kept to three narrow features (15f). If a feature can live in
A or B, it lives in A or B.

## Increments

Each lands end-to-end (engine/server → CLI where applicable → web → tests → docs),
per the working agreement in `roadmap.md`.

| # | Increment | Contents |
|---|---|---|
| 15a | Time domain & horizontal virtualization | Wall-clock minutes domain, hour→year scale, windowed rendering, layered canvas backdrop, perf harness |
| 15b | Calendar fidelity | Server calendar-interval feed; non-working shading, exception rendering, calendar-aware bar carve-out, split vs. stretch distinction |
| 15c | Zoom & navigation | Zoom control element, anchored continuous zoom, pinch/wheel/keyboard, fit/selection/date navigation, overview scrubber |
| 15d | Links, selection & input model | Link identity + hit testing + selection + editing, routing with channel allocation, touch/pointer model, full keyboard & AT model |
| 15e | Schedule semantics on the bar | Baselines 1–10, slack bars, progress lines, deadline/constraint markers, driving-path focus, assignment sub-rows, overallocation ribbon |
| 15f | Formatting model | Bar-style rules (filter-expression driven), per-task colour role, time markers — Core + CLI + commands + persistence v9 |
| 15g | Colour, contrast & polish | Encoding contract, colour-vision-deficiency modes, print/export path, docs |

15a and 15b are prerequisites for everything visual; 15c depends on 15a; 15d and 15e
are independent of each other; 15f and 15g close.

---

## 15a — Time domain & horizontal virtualization

### The defect being fixed

`timescale.ts` maps dates to pixels with `(date.getTime() − start.getTime()) / DAY_MS`
on local-time `Date`s. Across a DST boundary that expression is off by an hour. At
24 px/day the error is one pixel and invisible; at hour resolution it is a visible,
irreproducible-by-timezone rendering bug. The engine's dates are wall-clock with no
timezone at all, so routing them through epoch milliseconds is wrong in principle,
not just in edge cases.

`makeScale` also sets `width = days × pxPerDay` and `ticks()` iterates every day of
the span. A three-year project at hour zoom is ~1.1 M days-equivalent of pixels and
tens of thousands of tick objects per frame.

### `web/src/lib/timescale.ts` v2 — wall-clock minutes

```ts
/** Minutes since the civil epoch 0000-01-01T00:00, computed from Y/M/D/h/m only. */
export function wallMinutes(date: Date): number
export function fromWallMinutes(minutes: number): Date

export interface TimeScale {
  /** Domain origin, in wall minutes. */
  originMinutes: number
  /** Domain extent, in wall minutes (project span + padding). */
  spanMinutes: number
  pxPerMinute: number
  /** Total content width; may exceed the rendered window. */
  width: number
}

export function xOf(scale: TimeScale, date: Date): number
export function minutesAt(scale: TimeScale, x: number): number
export function dateAt(scale: TimeScale, x: number): Date
/** Snap to the current tier's unit (minute/hour/day/week/month) rather than always a day. */
export function snapAt(scale: TimeScale, x: number, unit: SnapUnit): Date
```

`wallMinutes` uses a days-from-civil calculation (Y/M/D → day number, arithmetic only,
no `Date` math), so DST, leap seconds, and the host timezone are structurally
irrelevant. The existing day-snapping `dayAt` becomes `snapAt(scale, x, 'day')` and
its call sites in `Gantt.tsx` move to the tier's snap unit — dragging at hour zoom
must produce hour-precision constraint dates, which is exactly the point of hour zoom.

Precision: `pxPerMinute` is a float, but all *snapped* results go through integer
minute arithmetic. Never round-trip a snapped date through pixels.

**The wire and the CLI are already sub-day capable — verified, not assumed.**
`toWireDate` emits `yyyy-MM-ddTHH:mm:00` and `fromWireDate` parses the time components
(`web/src/lib/format.ts:37-54`); `Parsers.DateInput` accepts `yyyy-MM-dd HH:mm` and
`yyyy-MM-ddTHH:mm:ss`, defaulting a bare date to the project's default start/end time
(`src/Project27.Cli/Parsers.cs:25-39`). So sub-day tiers are fully **editable**, not
read-only, and D4 parity needs no new date syntax. The only thing throwing the
precision away is the client: `dayAt` rounds to whole days and `Gantt.tsx:102-105`
feeds that into `setTask`. Replacing `dayAt` with `snapAt(scale, x, tier.snap)` is
therefore the entire fix, and a CLI test asserting `--constraint-date "2026-08-12 14:30"`
survives a schedule round-trip pins the parity.

### Tick generation becomes range-scoped and tiered

```ts
export type Tier = 'minute15' | 'hour' | 'shift' | 'day' | 'week' | 'month'
                 | 'quarter' | 'halfYear' | 'year'

export interface TickBand { tier: Tier; ticks: Tick[] }

/** Ticks only for [xFrom, xTo]; cost is O(visible ticks), never O(project span). */
export function ticksInRange(scale: TimeScale, tier: Tier, xFrom: number, xTo: number,
                             opts: { weekStartsOn: number; fiscalYearStart: number }): Tick[]
```

The header renders up to **three** bands (e.g. year / month / week at week zoom;
day / hour / 15-min at hour zoom). Which three is derived from the active tier by a
pure `bandsFor(tier)` table, overridable per user (bucket B) — MS Project and Primavera
both expose this and users of either expect it.

Fiscal-year support is included here rather than deferred: `fiscalYearStart` already
matters to the engine's reporting side, and a quarter band that ignores it is wrong
for most enterprise plans.

### Horizontal virtualization — `web/src/lib/hwindow.ts`

Mirrors `virtualize.ts` on the other axis:

```ts
export interface ColumnWindow { xFrom: number; xTo: number; contentWidth: number }
export function columnWindow(scrollLeft: number, viewportWidth: number,
                             contentWidth: number, overscanPx: number): ColumnWindow
```

The SVG element's width is clamped to the window (plus overscan) and positioned with a
`transform: translateX()`, while an outer spacer div carries the full content width so
the native scrollbar keeps its correct proportions. This is the same trick the row
virtualizer uses vertically and it keeps SVG well under browser geometry limits (Chrome
and Firefox both degrade badly past ~10⁵–10⁶ px of SVG coordinate space).

The timescale header is a **second** SVG at full `scale.width`
(ProjectView.tsx:914-915), so it virtualizes too: header and body take the *same*
`ColumnWindow` and the same `translateX` from one shared state value. Two independently
derived windows will desync by a frame during pan, and a header offset from its bars by
even a few pixels is worse than no virtualization at all.

Bars, links, and ticks are filtered to the window before render. Links whose endpoints
straddle the window are clipped to it — they must still be *drawn*, or a link visibly
disappears while you pan along it.

### Layered rendering — canvas backdrop, SVG foreground

Non-working shading, gridlines, banding, and the overallocation heat strip are
non-interactive, high-cardinality, and pure functions of the scale. They move to a
single `<canvas>` painted underneath the SVG. Bars, links, markers, handles, and
anything hit-testable stay SVG.

This is not a departure from E26 — it is still hand-built, zero-dependency rendering —
and it is what makes the node budget below achievable. The canvas is painted from the
same pure geometry functions the SVG uses, so nothing is computed twice or drifts.

### Perf budget (enforced by a harness, not by vibes)

A generated 5,000-task / 12,000-link fixture lands in `web/src/lib/__fixtures__`, and
a Vitest benchmark asserts geometry-layer costs:

| Metric | Budget |
|---|---|
| Interactive SVG nodes in a frame | ≤ 1,500 |
| `ticksInRange` + `columnWindow` + bar geometry, per frame | ≤ 4 ms |
| Pan/zoom frame budget (Playwright trace, mid-range hardware) | ≥ 55 fps sustained |
| Initial Gantt paint, 5,000 tasks | ≤ 250 ms after schedule arrives |

Frame-rate numbers are measured in the Playwright suite and reported, not asserted, to
avoid a flaky CI gate; the node-count and geometry-time budgets are hard assertions.

---

## 15b — Calendar fidelity

### The gap

`grep Calendar web/src/api/types.ts` returns nothing. The web has no calendar data at
all. Weekend shading today is inferred client-side from `day.getDay()`, which is a
guess that is wrong for every non-Mon–Fri calendar, every work week, and every
exception — i.e. wrong for most real plans. Hour zoom without working-hours shading is
meaningless.

### Server: a windowed, run-length calendar feed

```
GET /projects/{id}/calendars?from=2026-01-01&to=2027-01-01[&names=Standard,Night%20Shift]
```

```jsonc
{
  "from": "2026-01-01", "to": "2027-01-01",
  "calendars": [{
    "name": "Standard",
    "base": null,
    // The resolved default week: 7 entries, minutes-from-midnight intervals.
    "week": [[], [[480,720],[780,1020]], /* … */ ],
    // ONLY days that differ from `week` — exceptions and work-week overrides.
    "days": [
      { "date": "2026-07-03", "intervals": [], "label": "Independence Day (obs.)", "kind": "exception" },
      { "date": "2026-12-24", "intervals": [[480,720]], "label": "Christmas Eve", "kind": "exception" },
      { "from": "2026-08-03", "to": "2026-08-14", "week": [/* … */], "label": "Summer hours", "kind": "workWeek" }
    ]
  }]
}
```

Design rules that matter:

- **Deviating days only.** A naive per-day array over a multi-year span is megabytes;
  the weekly pattern plus deviations is kilobytes. `WorkCalendar.GetDaySchedule(DateOnly)`
  already resolves inheritance, work weeks, and exceptions, so the projection walks the
  window once and emits a day entry only where the resolved schedule differs from the
  resolved default week.
- **Per named calendar, not one global stripe.** `ScheduleTask.calendar` is
  `string | null`; a task on "Night Shift" must shade by *its* calendar. The response
  carries every calendar referenced by the project, its tasks, or its resources.
- **Windowed and cached.** The window follows the visible range with generous padding,
  ETag'd, and refetched on the SSE project-changed event. Client keeps an LRU of
  fetched windows keyed by `(calendar, from, to)`.
- **Exception labels are carried through.** `CalendarException` has a name; it becomes
  a tooltip and, at day zoom and coarser, an optional header glyph. Users repeatedly
  ask "why is nothing scheduled that week" — the answer should be on the chart.

CLI parity: `p27 calendar show <name> --from --to --json` emits the same structure
(the CLI already has calendar verbs; this is the read-side projection of them).

### Client: `web/src/lib/calendarShading.ts`

```ts
export interface ShadingBand { x: number; width: number; kind: 'nonworking' | 'exception' | 'partial' }
export function shadingBands(scale: TimeScale, calendar: ResolvedCalendar,
                             xFrom: number, xTo: number, tier: Tier): ShadingBand[]
```

Behavioural rules:

- At `hour`/`minute15`/`shift` tiers, bands are the actual non-working *intervals*
  (nights, lunch breaks). At `day` and coarser, a day is shaded when it is wholly
  non-working, and marked `partial` (lighter, or a corner tick) when it has reduced
  hours — a half-day Christmas Eve reads differently from a full holiday.
- At `month` and coarser, per-day shading becomes noise: it collapses to nothing, and
  non-working information moves into bar geometry only.
- Bands merge adjacently before emission (run-length), so a two-week shutdown is one
  rect, not fourteen.

### Calendar-aware bars — the distinction nobody else draws correctly

Two visually similar, semantically different things must not look the same:

1. **A stretch across non-working time.** A 3-day task starting Friday finishes
   Tuesday. It is *one* segment; the weekend is not work. The bar renders as a
   continuous shape whose non-working interior is drawn in a de-emphasised
   (desaturated / hatched) treatment, so the bar's span and its work are both legible.
2. **A split.** `task.segments` has more than one entry. The bar is genuinely broken;
   the gap is empty and the pieces are joined by a thin connector line.

Today both look like a plain rect (splits are already segment-rendered; stretches are
not distinguished at all). The rendering rule: segments come from the engine and are
authoritative for *breaks*; calendar shading inside a segment is a fill treatment, never
a break. This is computed by `barCarve(scale, segment, calendar, tier)` returning
interior sub-rects, tier-gated (off above `day` tier, where a weekend is sub-pixel).

Elapsed-duration tasks (`edays`) ignore calendars by definition, and
`ignoresResourceCalendars` tasks ignore the resource intersection — both render without
carve-out, which is itself informative: an elapsed task visibly runs through the
weekend where its neighbours pause.

### Deviations recorded

- Non-working interior shading uses the **task's effective calendar** (task calendar,
  else project calendar), not the intersection with assigned resources' calendars. The
  intersection drives *scheduling* (and is visible on assignment sub-rows, 15e), but a
  task bar shaded by the union of four resources' calendars is unreadable. → deviation #34.

---

## 15c — Zoom & navigation

### Zoom model

Zoom is continuous internally and tiered in the UI. State (bucket B):
`{ tier: Tier, factor: number }` where `factor ∈ [1, tierRatio)` interpolates within a
tier, giving smooth pinch/wheel zoom that still snaps to a nameable resolution.

```ts
// web/src/lib/zoom.ts
export const TIERS: readonly { tier: Tier; pxPerMinute: number; label: string; snap: SnapUnit }[]
export function zoomBy(state: ZoomState, deltaFactor: number): ZoomState        // clamped
export function zoomToTier(tier: Tier): ZoomState
/** Zoom while holding the date under `anchorX` fixed; returns the new scrollLeft. */
export function anchoredZoom(scale: TimeScale, next: ZoomState,
                             anchorX: number, scrollLeft: number): { scale: TimeScale; scrollLeft: number }
export function fitRange(from: Date, to: Date, viewportWidth: number): ZoomState
```

`anchoredZoom` is the single most-felt detail in the whole epic: pinch and Ctrl+wheel
must keep the date under the pointer/pinch-centre pinned. Getting this wrong makes
every other improvement feel cheap. It is pure, and it is unit-tested with an invariant:
`dateAt(next, anchorX) === dateAt(prev, anchorX)` within half a pixel, across the full
tier ladder.

Ladder (px/day equivalents at `factor = 1`, subject to design tuning):

| Tier | Resolution | ≈ px/day | Typical use |
|---|---|---|---|
| `minute15` | 15 min | 46,080 | Shift-level, commissioning, outage plans |
| `hour` | 1 h | 11,520 | Day-of execution |
| `shift` | 4 h | 2,880 | Multi-shift work |
| `day` | 1 day | 24–96 | The default working zoom |
| `week` | 1 week | 8–20 | Sprint / month view |
| `month` | 1 month | 2–6 | Programme view |
| `quarter` | 1 quarter | 0.8–2 | Portfolio |
| `halfYear` | ½ year | 0.4 | |
| `year` | 1 year | 0.15 | Decade-scale capital programmes |

Sub-day tiers are only meaningful when the plan has sub-day content; the zoom control
surfaces them always but marks them when the project's `minutesPerDay` makes them
degenerate.

### The zoom control element

Replaces the current `[−] 24px/d [+]` trio. A single compact cluster, docked
bottom-right over the chart:

- **Tier ladder** — a segmented control / dropdown listing named tiers ("Hours",
  "Days", "Weeks", "Months", "Quarters", "Years"), current one marked. Named
  resolutions, never raw px/day, which is an implementation detail no planner thinks in.
- **`−` / `+`** — one tier step, with `factor` reset; long-press repeats.
- **Continuous slider** — logarithmic across the whole ladder, with tier detents.
- **Fit** — `Fit project` / `Fit selection` / `Fit to today ± N`. Keyboard: `0` fits
  project, `Shift+0` fits selection.
- **Go to date** — a date input that scrolls (and, if outside the span, extends the
  padded range). Keyboard: `g`.
- **Follow toggles** — "keep today centred", "follow selection". Both bucket B.

Keyboard/pointer bindings: `Ctrl/⌘ + wheel` zooms anchored at the pointer; plain wheel
scrolls vertically; `Shift + wheel` scrolls horizontally; `+`/`−` zoom anchored at the
viewport centre; trackpad pinch arrives as `ctrlKey` wheel events and is handled by the
same path. Touch pinch is 15d.

### Overview scrubber

A 32-px-tall mini-map above or below the chart showing the whole project span: a
density band (bar coverage per bucket, critical bars emphasised), today, status date,
baselines' outer envelope, and a draggable viewport rectangle. Click to jump, drag to
pan, drag its edges to zoom-to-range. Pure geometry in `web/src/lib/overview.ts`;
canvas-rendered. Bucket B toggle.

This is the navigation affordance that makes year-scale plans usable, and it is where
"zoom out to years" actually pays off — the scrubber gives context that scrolling
cannot.

---

## 15d — Links, selection & input model

### Links become first-class objects

Today a link is an anonymous `<polyline>`; `selectedUids` is `Set<number>` of task
uids, so a link cannot even be *named* by the selection model.

```ts
export type LinkId = `${number}->${number}`            // predecessorUid -> successorUid
export interface Selection { tasks: ReadonlySet<number>; links: ReadonlySet<LinkId> }
```

Every place that consumes `selectedUids` moves to `Selection`. Behaviour:

- **Hit testing.** Each link renders a visible stroke plus a transparent
  `stroke-width: 12` hit path (`pointer-events: stroke`). Touch gets a 24-px hit path
  (WCAG 2.2 AA 2.5.8; the current `r=4` link handle already violates it and is fixed
  here to a 24 × 24 touch target with a smaller painted dot).
- **Selection.** Click selects; `Ctrl/⌘`-click extends; clicking empty canvas clears.
  Selected links are emphasised end-to-end and both endpoint bars get a subdued
  highlight so you can see *what* you selected.
- **Editing.** With a link selected: `Delete` unlinks; the inspector shows type
  (FS/SS/FF/SF) and lag (working / elapsed / percent — the engine supports all three,
  `LagKind`), editable inline. Double-click opens the same inspector. All via existing
  `link` commands, so undo/redo works for free.
- **Keyboard.** `Tab` moves between bars; `Alt+→` / `Alt+←` step from a focused bar to
  its successor/predecessor links; `Enter` opens the inspector.
- **Hover reveals the relationship** in a tooltip: `Design → Build, FS +2d, driving`
  (driving-ness comes from the existing `GET /drivers/{uid}`).

### Routing — `web/src/lib/linkRoute.ts`

The current router is `x1 → x1+8 → bend → x2`, which overlaps bars and other links
routinely. Replace with orthogonal routing with vertical-channel allocation:

```ts
export interface RouteInput { fromX: number; fromY: number; toX: number; toY: number;
                              type: DependencyType; obstacles: Rect[] }
export function routeLink(input: RouteInput, channels: ChannelAllocator): Point[]
```

- Stubs of ≥ 8 px leave the predecessor and enter the successor, on the correct edges
  for the dependency type (SS leaves the left edge; FF enters the right; SF is drawn
  distinctly rather than as a mirrored FS).
- Vertical runs are packed into inter-row channels by the same greedy first-free
  strategy `lanes.ts` already implements for timeline lanes — factor that packing out of
  `lanes.ts` into a shared `packFirstFree` helper rather than writing it twice.
- Backward links (successor starts before predecessor finishes — legal with lead) route
  *around* the rows rather than straight through the bars.
- Corners get a 3-px radius; the geometry is emitted as a point list so canvas, SVG, and
  tests all consume the same thing.

Rendering budget: routing runs only for links with at least one endpoint in the
row × column window, and results are memoised on `(scale identity, row window, schedule
revision)`.

### Touch and pointer model — `web/src/lib/gesture.ts`

The component already uses pointer events, which is the right foundation. Missing is a
gesture layer:

```ts
export type Gesture =
  | { kind: 'idle' }
  | { kind: 'pan'; startX: number; startY: number; scrollLeft: number; scrollTop: number }
  | { kind: 'pinch'; centerX: number; startDistance: number; startZoom: ZoomState }
  | { kind: 'press'; uid: number; at: number }        // long-press pending
  | { kind: 'drag'; /* delegates to lib/drag.ts */ }

export function reduce(state: Gesture, event: PointerFrame): { state: Gesture; effect?: Effect }
```

A hand-rolled two-pointer reducer (E26), unit-tested against synthesised pointer
sequences — no library, no `hammerjs`.

Rules:

- One finger on empty canvas pans; one finger on a bar drags it (after an 8-px threshold
  on touch, 3-px on mouse — touch needs the larger slop or every tap becomes a drag).
- Two fingers pinch-zoom, anchored at the pinch centre via `anchoredZoom`, with pan on
  the same gesture.
- Long-press (500 ms, movement < 8 px) opens the context menu — the touch equivalent of
  right-click.
- **No hover-only affordances.** The link handle currently appears on `g:hover`. On
  touch it must appear on selection instead; the CSS gates on
  `@media (hover: hover)` for the hover path and on `.selected` universally.
- `touch-action: none` is scoped to the chart canvas only, never the page, so the sheet
  and the rest of the app keep native scrolling.
- Drag targets and handles meet 24 × 24 CSS px; the *painted* affordance may be smaller.

### Accessibility model

The chart is currently `<title>`-only, i.e. effectively opaque to assistive tech. This
epic must not regress the WCAG 2.2 AA claim made in phase 12, and materially improves it:

- **Decision to record (E41): the two panes expose two different AT models for the same
  rows.** The sheet is a grid (`role="grid"`, cell navigation); the chart is
  `role="application"` with spatial navigation. A screen-reader user meeting the same
  task twice under two models is a real cost, and the alternative — one grid spanning
  both panes — was rejected because a bar's information is positional (dates, overlap,
  links) and does not reduce to cells without losing exactly what the chart is for. The
  mitigation is that the chart is skippable (a skip link past it), every fact it conveys
  is also reachable in the sheet, and both panes announce the same task identity so the
  correspondence is audible.
- The SVG is `role="application"` with a labelled description; each bar is a focusable
  `role="img"`/`g` with an `aria-label` that states name, dates, duration, percent
  complete, criticality, and constraint — the same string the tooltip shows.
- Roving-tabindex within the chart; `↑/↓` move rows, `←/→` pan by one tier unit,
  `Home/End` jump to project start/finish, `Alt+→/←` traverse links.
- Keyboard rescheduling: with a bar focused, `Shift+←/→` moves by one snap unit and
  issues the same command the drag issues, so the mouse is never required for an
  operation (WCAG 2.1.1).
- Drag operations have a keyboard/menu equivalent (WCAG 2.2 AA 2.5.7 Dragging
  Movements): link creation is also available from the context menu ("Link to…"), and
  rescheduling from the inspector.
- An `aria-live="polite"` region announces the result of drags, zoom-tier changes, and
  selection changes ("Build moved to 12 Aug, 3 successors rescheduled").
- `prefers-reduced-motion` disables zoom/scroll easing and bar transitions.

---

## 15e — Schedule semantics on the bar

This is where the epic earns the claim that we go deeper into the scheduling domain
than the field. Each item is a rendering of something the engine already computes.

### Multiple baselines (1–10)

`ScheduleProjection.cs:272-274` emits only `task.Baseline()` (slot 0). Core has ten
slots (deviation #21). Wire change:

```jsonc
"baselines": [
  { "slot": 0, "start": "…", "finish": "…", "cost": 1200, "workMinutes": 4800, "savedAt": "…" },
  { "slot": 3, "start": "…", "finish": "…", "cost": 1500, "workMinutes": 5400, "savedAt": "…" }
]
```

Only populated slots are emitted; `baselineStart/Finish/Cost` stay as slot-0 aliases so
nothing breaks. Project-level baseline metadata (label, saved-at, saved-by) comes back
on the schedule payload so the picker can name them ("Baseline 3 — Re-plan, 14 Mar").

Rendering: a baseline picker (multi-select, bucket B) drives 0–3 simultaneous ghost
bars stacked under the live bar at decreasing weight, each with a slot marker; more than
three is unreadable and the UI caps it. Plus a **variance whisker** mode: instead of
ghost bars, draw a leader from baseline finish to actual finish with the variance
labelled — far denser, and the actual question people ask ("how much did we slip?").

### Slack

Total slack as a thin trailing extension past the bar's finish, free slack distinguished
(the engine gives both, `totalSlackMinutes` / `freeSlackMinutes`). Negative slack renders
*backwards* from the finish in the "late" treatment — negative float is a diagnostic and
should be impossible to miss. Toggle in bucket B.

### Progress lines

MS Project's progress lines, drawn at the status date (or at a chosen recurrence): a
vertical line that deflects left for tasks behind schedule and right for tasks ahead,
connecting each in-progress task's progress point. Computed from `percentComplete`
walked over the task's working time — `progressPoint(task, calendar)` in
`web/src/lib/progressLine.ts`, pure and tested. Multiple lines (weekly, at each baseline
save) are supported; bucket B.

### Deadlines, constraints, leveling delay

- `deadline` renders as a downward arrow marker; when finish > deadline the bar takes
  the "missed" treatment (shape + icon, not colour alone).
- Hard constraints (`mustStartOn`, `mustFinishOn`) get an anchor glyph at the constraint
  date; soft constraints get a subdued one. Hovering explains which constraint and
  whether it is currently binding.
- `levelingDelayMinutes > 0` renders as a distinct leading segment before the bar, so
  "why is this late" is answerable without opening the inspector — this is exactly the
  case where the engine knows the answer and today's chart hides it.

### Driving path / focus mode

`GET /projects/{id}/drivers/{uid}` already exists. Selecting a task and pressing `f`
(or the toolbar toggle) dims everything except the selected task, its driving
predecessors transitively, and its driven successors — the "why is this task where it
is" view. Driving links render emphasised; non-driving links in the chain are dimmed
rather than hidden, so structure is preserved. This is strictly better than a critical
path highlight, and cheap given the endpoint.

### Assignment sub-rows

A task row expands (disclosure on the bar's left edge, or `Alt+↓`) into one sub-row per
assignment, each showing the assignment's own span, contour shape (a small area profile
from the usage endpoint), units, and work. Sub-rows shade by the *resource's* calendar
intersected with the task's — which is where the intersection belongs (see deviation
#34).

This adds no *document* state, but it is **not** a Gantt-local change. Both panes are
row-aligned off a shared `displayRows` + `indexByUid` model (ProjectView.tsx:206, 213)
and mirror scroll rather than sharing one scroller (E29); `spaceAfter` gap rows already
prove that inserting rows is a shared-model concern. So:

- `web/src/lib/displayRows.ts` gains an `assignment` row kind (alongside `task` and gap
  rows), built from `task.assignments`, and **both** panes consume it.
- `TaskSheet` renders assignment cells for those rows (resource name, units, work,
  cost) — a sub-row blank in the sheet and populated in the chart looks broken.
- Expansion state (`Set<number>` of expanded task uids) is **bucket B**, alongside the
  outline collapse state `collapseStore.ts` already persists: an expansion set up to
  study a resource conflict should survive a reload, and the precedent is right there.
- Row height stays uniform — assignment rows use the same `rowHeight` as task rows, so
  `virtualize.ts` and every `index * rowHeight` computation are untouched.

### Overallocation ribbon

A per-day heat strip along the chart's bottom edge (canvas layer) showing aggregate
resource overallocation from the usage endpoint, click-through to the resource view.
This closes the loop between the Gantt and leveling: you can see the conflict on the
chart that the leveler would resolve.

### Bar text

Field-driven text in five positions (left, inside-left, inside-centre, inside-right,
right), each bound to a field-catalog key (`resourceNames`, `finish`, `percentComplete`,
a custom field, …). Placement is collision-aware — text that will not fit inside moves
outside; text that collides with a neighbour's text is dropped at that zoom rather than
overprinted. Bucket B for the field choices; the placement algorithm is pure and tested
(`barText.ts`).

---

## 15f — Formatting model (Core + CLI + persistence)

The only bucket-C work in the epic. Three features, deliberately narrow.

### Bar style rules

MS Project's "Bar Styles" dialog is the right model and we already have the machinery
to do it better: **a rule matches with a view-engine filter expression**, the exact
parser used by `p27 view --filter` (spec 09a).

```
BarStyle(Name, Match: string, Row: 0|1|2, Shape, ColorRole, Pattern, TextFields)
```

```sh
p27 barstyle list [--json]
p27 barstyle add "Late milestones" --match "milestone = true and finish > deadline" \
    --shape diamond --color danger --row 0
p27 barstyle move "Late milestones" --to 2      # order = precedence, last match wins
p27 barstyle remove "Late milestones"
```

`Project.BarStyles` is an ordered list on the aggregate; rules are evaluated per task in
order and later matches override earlier ones (MSP semantics). Because the matcher is
the existing filter engine, a rule can key off *anything* the field catalog exposes,
including custom fields and formulas — "colour by phase from `text5`", "flag anything
with CPI < 0.9" — which is well past what bar-style dialogs elsewhere allow.

Evaluation is server-side: the schedule projection emits a resolved
`styleKey: string | null` per task, so the client never ships a filter evaluator (E26
holds) and CLI/web can never diverge (the Core rule). A key alone does not render a bar,
so the schedule payload also carries the resolved style table —
`barStyles: [{ key, name, row, shape, colorRole, pattern, textFields }]` — and the
client is a pure lookup: `styleKey → style → tokens`. Tasks matching no rule get
`null` and the built-in default treatment.

### Per-task colour override

`TaskFormatting` (already the designated extensible bag, E35) gains
`ColorRole: string?` — a **semantic role name** from a curated set, not free RGB.
Rationale: an arbitrary-hex palette makes contrast and colour-vision-deficiency
guarantees impossible to keep, and every planner who has used MSP has seen a plan
rendered unreadable by hand-picked colours. Free hex is accepted on import (MSPDI) and
snapped to the nearest role, with the original preserved for round-trip where possible.
→ deviation #35.

```sh
p27 task format <uid> --color accent-3     # and --clear
p27 task format <uid> --space-after 2      # existing
```

### Time markers

Named vertical lines and date bands on the chart — releases, freezes, gate reviews,
"curtains" in MSP terms.

```
TimeMarker(Name, From, To?, Kind: line|band, ColorRole)
```

```sh
p27 marker add "Code freeze" --on 2026-09-01
p27 marker add "Holiday shutdown" --from 2026-12-24 --to 2027-01-02 --kind band
p27 marker list [--json] ; p27 marker remove "Code freeze"
```

Markers are project-level document state, appear on the chart, the scrubber, and in
reports. They are *not* calendar exceptions and do not affect scheduling — a common
confusion worth being explicit about in the user guide.

### Plumbing for all three

- Commands `setBarStyles`, `setTaskFormatting` (extend), `setTimeMarkers` with
  server-computed inverses (D6a) → undo/redo works.
- `ProjectDocument.SchemaVersion` 8 → **9**; the mapper accepts 1–9 and treats missing
  sections as empty, so older files open unchanged.
- MSPDI: bar styles and time markers do **not** round-trip (MSPDI has no equivalent that
  survives our clean-room semantics); per-task colour maps to/from MSPDI bar colour
  lossily via the role snap. → deviation #36, and an added line in deviation #32's list.
- Golden-scenario tests: formatting must provably never influence `Recalculate()` —
  a test asserts an identical schedule with and without every formatting feature set.

---

## 15g — Colour, contrast & polish

Aesthetics are Claude Design's job. This section specifies the **contract** the design
must satisfy, not the palette.

### Encoding rules

- **Never colour alone.** Every semantic distinction carries a second channel: critical
  (colour + a distinct outline/edge treatment), inactive (colour + reduced opacity +
  dashed edge), manual (colour + bracket end-caps, MSP's convention), missed deadline
  (colour + icon), baseline (position + weight), non-working interior (fill pattern +
  value shift). This is WCAG 1.4.1 and it is also just better information design —
  Bertin's separability argument, not an accessibility tax.
- **Contrast.** Bars vs. chart background ≥ 3:1 (WCAG 1.4.11 non-text contrast); bar
  text vs. its bar ≥ 4.5:1, verified for *every* bar-role × text-position pair in both
  themes. Selection and focus indicators ≥ 3:1 against both the bar and the background
  (2.4.11 Focus Not Obscured, 2.4.13 Focus Appearance).
- **Categorical palette** for grouping/colour-by-field is CVD-safe: distinguishable
  under deuteranopia, protanopia, and tritanopia simulation. The `dataviz` skill's
  palette method and its runnable contrast validator are the reference; the validator
  runs in the Vitest suite over the token file so a palette edit that breaks contrast
  fails the build. Roles are limited to ~8 categorical + semantic (critical, late,
  complete, inactive) — beyond 8, colour stops distinguishing and the UI switches to
  pattern + label.
- **Colour-vision modes** (bucket B): `default`, `deuteranopia`, `protanopia`,
  `tritanopia`, `monochrome`. These swap a token set, they do not recolour ad hoc; the
  monochrome mode is the honest test of whether the redundant encodings actually work,
  and it must remain fully usable.
- **Both themes.** Light and dark are first-class, tokens live in `web/src/tokens/`,
  and every rule above is asserted in both.

### Token contract for the design handoff

All chart colour flows through CSS custom properties in `web/src/tokens/` —
`--gantt-bar-*`, `--gantt-critical-*`, `--gantt-baseline-{0..2}`, `--gantt-link-*`,
`--gantt-nonworking-*`, `--gantt-marker-*`, `--gantt-accent-{1..8}`. No literal colours
in components, and geometry constants (bar height ratio, inset, corner radius, stub
length, channel pitch, header band heights) are likewise tokens so proportions can be
retuned without touching logic.

### Print / export

The chart's canvas + SVG layers export to a single SVG (canvas layer re-emitted as SVG
rects for print, where node count is not a frame-budget concern), feeding the existing
report pipeline (`GET /projects/{id}/reports/{name}`) and `p27 report`. Paginated across
pages by date range with repeated row labels — the artefact people actually put on a
wall. This keeps deviation #31 intact (no headless browser; print-to-PDF from the
browser).

---

## Testing strategy

| Layer | What | Where |
|---|---|---|
| Pure logic | `timescale` v2 (incl. a DST-boundary regression test), `hwindow`, `zoom`/`anchoredZoom` invariant, `gesture` reducer over synthesised pointer frames, `linkRoute`, `calendarShading`, `barCarve`, `progressLine`, `barText`, `overview` | Vitest, `web/src/lib/*.test.ts` |
| Geometry goldens | Serialized geometry arrays (not DOM snapshots) for representative scenarios at each tier | Vitest snapshots |
| Core | Bar-style matching, formatting-never-affects-schedule, persistence v8→v9 round-trip, command inverses | `tests/Project27.Core.Tests` |
| Server | Calendar feed shape/compression/windowing, multi-baseline projection, ETag behaviour | `tests/Project27.Server.Tests` |
| CLI | `barstyle`, `marker`, `task format`, `calendar show --json` | `tests/Project27.Cli.Tests` |
| E2E | Pinch-zoom anchoring, long-press menu, link select+delete, keyboard-only reschedule, 5,000-task perf trace | Playwright (already a dependency) |
| A11y | axe pass on the chart; manual VoiceOver/NVDA script for the keyboard model | Playwright + checklist in spec |

Engine behaviour is locked by golden scenarios; nothing in this epic may change one. If
a golden fails, the change is wrong.

## Deviations to record (`docs/spec/deviations.md`)

| # | Area | Ours | MSP | Why |
|---|---|---|---|---|
| 34 | Gantt shading | Bar interior shading uses the task's effective calendar; resource-calendar intersection appears only on assignment sub-rows | Shades by task calendar too, under-documented for multi-resource tasks | Legibility; the intersection is shown where it is attributable |
| 35 | Formatting | Task/bar colour is a semantic role from a curated CVD-safe set, not free RGB; imported colours snap to the nearest role | Arbitrary RGB per bar | Contrast and colour-vision guarantees cannot survive arbitrary hex |
| 36 | Formatting | Bar styles and time markers are our own model (filter-expression matched) and do not round-trip through MSPDI | Bar styles stored in the plan | Filter-expression matching is strictly more general; no MSPDI equivalent |
| 37 | Timescale | Three header bands maximum, fiscal-year aware; tiers are a fixed named ladder from 15 min to 1 year | Two bands (three in some versions), arbitrary units/counts | Named ladder keeps zoom, snapping, and shading rules coherent |

## Engineering decisions to record (`docs/engineering-decisions.md`)

Next free number is **E37** (E36 is used twice already — worth fixing while editing).

- **E37 — Wall-clock minutes, not epoch milliseconds.** Why the time domain was rewritten,
  the DST failure it removes, and why `Date.getTime()` must never appear in chart geometry.
- **E38 — Canvas backdrop + SVG foreground.** Why the split, where the boundary is
  (hit-testable ⇒ SVG), and why this is not a violation of E26.
- **E39 — Bar styles matched by the view-engine filter parser.** Why reuse beat a bespoke
  style-condition language, and why matching resolves server-side.
- **E40 — Semantic colour roles over free RGB.** The accessibility argument and the
  import-snapping compromise.
- **E41 — Two AT models over the same rows.** Why the chart is `role="application"`
  while the row-aligned sheet stays a grid, and what makes that acceptable.

## Non-goals

- Rewriting the task sheet (only the shared row model where alignment demands it).
- A WebGL renderer. The canvas + SVG split covers the stated budgets; revisit only if
  measurements say otherwise.
- Cross-project / master-schedule rendering — still the extension point from spec 10.
- Free-form annotation, drawing objects, or a diagramming layer on the chart.
- Real-time multi-cursor collaboration; checkout/check-in (D6) is unchanged.
- Critical-chain buffers and linear-schedule (time–distance) views. Both are genuinely
  interesting for our depth ambition, both are their own epic.

## Design handoff notes (Claude Design)

The design pass may change: palettes and token values, bar proportions and corner radii,
header band typography and density, the zoom control's visual form and placement, icon
and marker glyph design, motion and easing, empty/loading states, tooltip and inspector
chrome, the scrubber's visual treatment.

The design pass must not change: the state buckets, the wire formats, the tier ladder's
semantics, the redundant-encoding rule, the contrast and touch-target minimums, the
keyboard model, or the split-vs-stretch distinction. Those are correctness, not taste —
if the design needs one of them to move, that is a spec amendment, recorded here.
