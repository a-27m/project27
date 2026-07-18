# Epic 13 — Tracking & scheduling parity (deviations #13/#14/#16/#17/#19/#20/#23/#28/#29)

Closes or narrows nine deviations that were deferred until phases 8 (tracking)
and 9 (usage views) existed. Scope locked with the owner 2026-07-18:

- **#20 actuals**: *scalar now, buckets later* — per-assignment scalar actuals in
  this epic; time-phased (per-bucket) actual editing is the documented next seam.
- **#28/#29 leveling**: split-based leveling of started tasks, configurable
  victim order, finer step granularity.
- **#14 contours**: unify scheduling with the decile distribution.

## What changed

### Contours (#14) — one source of truth

The per-decile tables moved from `Usage.Timephased` to
`WorkContourExtensions.Deciles()`; `AverageUtilization()` is now *derived*
(`deciles.Sum()/1000`). Scheduling keeps the closed form
`duration = work / (units × avg)` — and this is not an approximation: the decile
distribution is defined over **working-time tenths of the assignment span**, so a
bucket-by-bucket walk of the deciles consumes exactly that span on any calendar,
uniform or not (the walk and the closed form are algebraically identical:
`Σᵢ units·rᵢ·D/10 = units·avg·D = work`). A golden test locks the averages to
the tables. What remains of #14 is MSP's "Contoured" state — literal per-day
values after a manual usage edit — which arrives with bucket editing (see #20
seam below).

### Cost engine (#17, #13)

`Assignment.Cost` for work resources is now `Timephased.WorkCost`: each day's
contoured work slice priced by the rate band in force at that day's start, plus
per-use at the start band. `Timephased.ForAssignment` prices buckets with the
same per-day rule, so **buckets sum exactly to the assignment cost** (the work
slices already telescope through rounded cumulatives; day costs are priced from
those exact slices). Single-band resources are unchanged by construction.

Material assignments gained variable consumption (`Assignment.MaterialRateUnit`,
e.g. units 10 + `Day` = "10/day"): total quantity =
`units × taskDuration / minutesPer(unit)` (`Rate.MinutesPer`), accrued linearly
over the span's working time and priced per day by the band in force
(`Timephased.MaterialCost` / `MaterialSlices`). The accrual setting places only
the per-use fee for variable consumption; fixed quantities behave as before.

### Split/manual assignment dates (#16)

`Timephased.AssignmentSchedule` masks the assignment schedule (task × resource
calendar) with the task's scheduled segments (`ScheduleMask`, minute-granular
day clipping), so split-task work never lands in a gap — previously usage
spread work across gaps. The scheduler's non-walk path
(`PinnedAssignmentDates`) narrows assignment dates to the first/last working
instants of that masked schedule inside the task span, falling back to the raw
task span when the narrowed span would be empty. Deliberately *not* done:
extending a split task's segments when a restrictive resource calendar leaves
too little working time inside them — the entered segments stay inputs (same
philosophy as deviation #18).

### Scalar actuals (#20)

`Assignment.ActualWorkMinutes` / `Assignment.ActualCost` are nullable inputs;
null means "derived from the task's percent complete" (the pre-epic behavior,
so untouched projects compute identical numbers). Effective values and
`RemainingWorkMinutes` roll up to `ProjectTask.ActualWorkMinutes/ActualCost/
RemainingWorkMinutes` (fixed cost accrues by percent complete) and into the
field catalog (`actualWork`, `remainingWork`, `actualCost`). EVM's ACWP is now
`task.ActualCost`.

**The bucket seam (next epic):** time-phased actual editing needs a stored
per-assignment bucket list (date → actual work), a "Contoured" contour state
(#14 residue), schema v8, an editable usage grid (web) and a
`usage set <task> <resource> <date> <work>` CLI verb. `AssignmentDocument` and
the command layer were shaped so those additions are purely additive.

### BCWS by accrual (#19)

`EarnedValue` places each baselined assignment's cost by its resource's accrual
(Start → due once the baseline span begins; End → due at baseline finish;
Prorated → linear over the baseline span's working time). The remainder of the
task's baseline cost (fixed cost + assignments never baselined) follows the
task's `FixedCostAccrual`. All-prorated projects compute exactly the old value.

### Reschedule of split in-progress tasks (#23)

`SplitSurgery.PushWork(task, calendar, from, resumeAt)` rewrites split parts so
the work at/after a point — never completed work — resumes at a target instant:
inside a part it splits it; on a boundary it widens the preceding gap; later
segments shift as a block with their relative structure intact.
`RescheduleUncompletedWork` now uses it for *all* started tasks (split or not;
the unsplit case degenerates to the old split-at-completion behavior), which
also fixed a latent crash: a started zero-duration task no longer hits
`SplitAt(0)`.

### Leveling (#28, #29)

`Project.Level(LevelingOptions)`:

- `Order`: `IdOnly` (highest row first), `Standard` (already-delayed, slack,
  latest start, row), `PriorityStandard` (default; priority then standard).
  The already-delayed preference stays in all orders — it is what keeps victim
  choice stable across iterations.
- `Granularity`: `Day` (default, unchanged) or `Minute` — the delay step is the
  conflicted day's exact excess demand (min 1), so a half-overloaded day costs
  a half-day delay, not a full one.
- `SplitInProgress`: started tasks (actuals pin their start, so delay can't
  move them) become candidates and are leveled by `SplitSurgery.PushWork` —
  remaining work at/after the conflicted day moves past it, whole-day steps.
  These are ordinary splits; `ClearLeveling` removes delays only.

The leveling loop no longer stops at the first unresolvable conflict (e.g. a
day where every contributor is protected or the work is completed): it skips it
and levels the later ones, reporting what remains. `LevelingResult` gained
`SplitTasks`.

## Surfaces (D4 parity)

| Capability | Core | CLI | Commands/Server | Web |
|---|---|---|---|---|
| Variable consumption | `MaterialRateUnit`, `MaterialQuantity` | `assign add/set --per d\|none` | `assign.unitsPer`, `setAssignment.unitsPer/clearUnitsPer`; projection `unitsPer` | Inspector per-select on material assignments |
| Scalar actuals | `ActualWorkMinutes/ActualCost` (+task rollups, fields) | `assign set --actual-work/--actual-cost` (`none` clears) | `setAssignment.actualWork/actualCost` + clears; projection carries both | Inspector actual work/cost inputs |
| Leveling options | `LevelingOptions` | `level run --order --granularity --split-in-progress` | `level.order/granularity/splitInProgress` | Level-with-options dialog |
| Reschedule splits | `RescheduleUncompletedWork` | existing `reschedule` verb | existing `reschedule` op | existing Plan menu action |

Persistence: **schema v7** (v1–v6 load) — `AssignmentDocument` gains
`materialRateUnit`, `actualWorkMinutes`, `actualCost`. MSPDI: the three new
fields do not round-trip (add to deviation #32's lossy edges when touched).

## Verification

- New Core suite `TrackingParityTests` (15 cases: band proration, variable
  consumption, mask/dates, actuals, accrual BCWS, split reschedule, leveling
  orders/granularity/splits, v7 roundtrip) + inverter coverage for the new
  `setAssignment` fields.
- CLI: `AssignCommandTests` (variable consumption, actuals + `none` clears),
  `LevelCommandTests` (order/granularity flags, split-in-progress).
- All existing golden scenarios unchanged — defaults preserve pre-epic
  semantics everywhere (derived actuals, prorated accrual, single-band costs,
  day/priority leveling).
