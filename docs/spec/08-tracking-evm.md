# Phase 8 — Tracking & EVM

Baselines, status date, actuals, rescheduling uncompleted work, and earned-value
fields, across engine, storage (schema v3), CLI, commands, and the schedule
projection.

## Baselines

Eleven slots (0–10, MSP parity). `Project.SetBaseline(slot, tasks?)` captures per
task: start, finish, duration, work, cost; per assignment: work, cost. A partial
capture (`tasks` given) overwrites only those tasks' entries — "roll up baselines"
variants are out of scope until demanded. `ClearBaseline(slot, tasks?)` removes
entries. Baseline 0 is *the* baseline for EVM.

An **interim plan** in MSP stores start/finish only; our baseline slots store the
full set, and the server keeps every checked-in snapshot anyway, so interim plans
are subsumed (deviation #21).

## Status date & actuals

`Project.StatusDate` (nullable; EVM falls back to the project finish). Leaf-task
tracking inputs:

| Field | Semantics |
|-------|-----------|
| `PercentComplete` 0–100 | Duration-based completion. Setting >0 without an actual start records `ActualStart = scheduled start`. 100 records `ActualFinish = scheduled finish` |
| `ActualStart` | Pins the task: the scheduler starts it exactly here, overriding dependencies and constraints (they still drive the backward pass) |
| `ActualFinish` | Forces 100% and pins the finish; duration becomes the span actual start → actual finish |
| `RemainingDuration` (derived, editable) | `Duration × (1 − %/100)`; editing it rewrites the total duration keeping the completed span fixed |

Summaries roll up: `% = Σ(child completed minutes) / Σ(child duration minutes)`
over active leaves; actual start/finish = min/max of children's.

Actual work and cost are **derived** in phase 8 (`× %complete`); explicit
per-assignment actuals arrive with usage views (phase 9) — deviation #20.

## Rescheduling uncompleted work

`Project.RescheduleUncompletedWork(after?)` (default: status date) — for every
started-but-unfinished leaf whose remaining span lies before the cutoff, the task
is split at the completion point with a gap reaching the cutoff (reusing split
machinery); unstarted tasks before the cutoff get a start-no-earlier-than
constraint at the cutoff. Completed tasks and manual tasks are untouched.

## Earned value (per task, rolled up by summing children)

With `S` = status date, `BAC` = baseline-0 cost:

- `BCWS` — BAC prorated linearly over the baseline span's working time up to S
  (clean-room proration; MSP time-phases by cost accrual — deviation #19)
- `BCWP` = BAC × %complete
- `ACWP` = actual cost (derived, see above)
- `SV = BCWP − BCWS`, `CV = BCWP − ACWP`
- `SPI = BCWP / BCWS`, `CPI = BCWP / ACWP` (null when the divisor is 0)
- `EAC = BAC / CPI` (falls back to current cost when CPI is null), `VAC = BAC − EAC`
- `TCPI = (BAC − BCWP) / (BAC − ACWP)` (null when the divisor is ≤ 0)

Tasks without a baseline-0 entry contribute zeros (MSP shows 0 too).
Computed on demand by `EarnedValue.ForTask(task, slot: 0)` /
`EarnedValue.ForProject(project)` — schedule-dependent, so recalculate first.

## Persistence (schema v3, v1/v2 still load)

`ProjectDocument` += `StatusDate`; `TaskDocument` += `PercentComplete`,
`ActualStart`, `ActualFinish`, `Baselines[{Slot, Start, Finish, DurationMinutes,
WorkMinutes, Cost}]`; `AssignmentDocument` += `Baselines[{Slot, WorkMinutes, Cost}]`.

## CLI

```
p27 baseline set   [--slot 0..10] [--tasks <refs…>]
p27 baseline clear [--slot 0..10] [--tasks <refs…>]
p27 project set --status-date <date|none>
p27 task set <ref> [--percent-complete N] [--actual-start date|none]
                   [--actual-finish date|none] [--remaining-duration 2d]
p27 schedule reschedule [--after <date>]
p27 task evm [<ref>]        # BCWS/BCWP/ACWP/SV/CV/SPI/CPI/BAC/EAC/VAC table
```

`task show` gains tracking fields and baseline-0 dates; `task list` gains a `%`
column. JSON shapes extend accordingly.

## Commands & projection

`setTask` gains `percentComplete`, `actualStart`/`clearActualStart`,
`actualFinish`/`clearActualFinish`; `setProject` gains
`statusDate`/`clearStatusDate`; new `setBaseline`/`clearBaseline` ops. The
schedule projection adds `percentComplete`, `actualStart`, `actualFinish`,
`baselineStart`, `baselineFinish`, `baselineCost` (slot 0) per task and
`statusDate` on the project.
