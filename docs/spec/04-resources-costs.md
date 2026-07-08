# Phase 4 — Resources & costs

Work, material, and cost resources; cost rate tables; assignments with work
contours; the task type × effort-driven scheduling triangle; resource calendars in
scheduling; cost rollup. Extends the engine (phase 1–2), storage schema (v2), and
the `p27` CLI.

## Resource model

| Field | Types | Notes |
|-------|-------|-------|
| `UniqueId` | all | Stable numeric id, resource-scoped counter (like task UIDs) |
| `Name` | all | **Unique per project, case-insensitive** (deviation #11) |
| `Type` | — | `work` \| `material` \| `cost` |
| `Initials`, `Group` | all | Free text |
| `MaxUnits` | work | Peak availability, decimal (1.0 = 100%). Informational until leveling (phase 10) |
| `MaterialLabel` | material | Unit-of-measure label ("t", "m³") |
| `Calendar` | work | Optional; null = availability follows the task/project calendar |
| `Accrual` | work, material | `start` \| `prorated` (default) \| `end`; time-phasing lands in phase 8/9 |
| Cost rate tables | work, material | Tables **A–E** (below); cost resources have no rates |

### Cost rate tables

Each of tables A–E holds effective-dated entries `{from, standardRate,
overtimeRate, costPerUse}`. The entry in force is the one with the greatest
`from` ≤ the assignment start; before the earliest entry, rates are zero.
Table A starts with a single zero entry at the epoch; `resource set --rate …`
edits that base entry.

Rates are `<amount>[/h|/d|/w|/mo|/y]` (default `/h`) for work resources and a
plain per-unit amount for material resources. Conversion uses project time
settings: `/d` = MinutesPerDay, `/w` = MinutesPerWeek, `/mo` = DaysPerMonth ×
MinutesPerDay, `/y` = 52 × MinutesPerWeek (deviation #15).

Overtime rates are stored but not costed until actuals arrive in phase 8 (no
overtime work exists yet).

## Assignments

An assignment joins one leaf task (summaries and recurring parents cannot be
assigned — deviation #12) and one resource:

| Field | Applies to | Meaning |
|-------|-----------|---------|
| `Units` | work | Assignment units (1.0 = 100%); must be > 0 |
| `Units` | material | Fixed quantity consumed (variable "per-day" consumption unsupported — deviation #13) |
| `WorkMinutes` | work | Person-time; the third corner of the triangle |
| `Contour` | work | `flat` (default), `back-loaded`, `front-loaded`, `double-peak`, `early-peak`, `late-peak`, `bell`, `turtle` |
| `DelayMinutes` | work | Working-time offset from task start on the assignment schedule |
| `RateTable` | work, material | A–E, default A |
| `CostInput` | cost | The assigned expense amount |

Outputs per assignment (set by `Recalculate`): `Start`, `Finish`, `Cost`.

### Contours

A contour spreads work across the assignment span. Until usage views land
(phase 9) only the **average utilization** matters to the engine: assignment
working duration = `Work / (Units × avg)`. Clean-room decile averages
(deviation #14):

| Contour | avg | | Contour | avg |
|---------|-----|-|---------|-----|
| flat | 1.00 | | early-peak | 0.45 |
| back-loaded | 0.60 | | late-peak | 0.45 |
| front-loaded | 0.60 | | bell | 0.50 |
| double-peak | 0.50 | | turtle | 0.70 |

## The scheduling triangle

`Work = Duration × Units × avg(Contour)` per work assignment. Task-level
`Type` (`fixed-units` default, `fixed-duration`, `fixed-work`) picks which
corner recalculates on edit, following MS Project's canonical table:

| Task type | Units edited → | Duration edited → | Work edited → |
|-----------|----------------|-------------------|---------------|
| fixed-units | recalc **duration** | recalc **work** | recalc **duration** |
| fixed-work | recalc **duration** | recalc **units** | recalc **duration** |
| fixed-duration | recalc **work** | recalc **work** | recalc **units** |

"Recalc duration" sets the leaf duration to the max assignment duration
(`max_i Work_i / (Units_i × avg_i)`), in task-calendar working minutes.
`fixed-work` forces effort-driven behavior and rejects `--effort-driven false`.

**Effort-driven** (`IsEffortDriven`, default **off**, matching MSP 2010+):

- Adding a work assignment *without explicit work* to a task that already has
  work assignments keeps total work constant and redistributes it by units
  (`W_i = Total × U_i / ΣU`), then applies the type rule (duration shrinks for
  fixed-units/fixed-work; units shrink for fixed-duration).
- Passing `--work` explicitly bypasses redistribution (the new work is added).
- Removing a work assignment redistributes its work over the remaining ones.
- Non-effort-driven: adding without work gives the assignment
  `Duration × Units × avg`; removal just drops the work.

Contour changes keep work and recalc duration (fixed-units/fixed-work) or
leave duration alone (fixed-duration).

Task-level work editing (distributing across assignments) is not exposed; edit
work per assignment (`assign set … --work`).

## Resource calendars in scheduling

Work assignments are scheduled on the **assignment schedule**:

- resource has no calendar, or `task.IgnoresResourceCalendars`, or non-work
  resource → task calendar (or project calendar);
- resource calendar only → the resource calendar;
- both task and resource calendars → their **intersection** (working where
  both work).

For an auto-scheduled, non-split leaf with work assignments (work > 0), the
scheduler walks each assignment on its schedule from the task start (plus
delay) and the task finish is the latest of the plain duration walk and every
assignment finish; the backward pass uses the inverse walk. Split and manual
tasks pin assignment dates to the task dates (deviation #16). Material and
cost assignments never affect dates.

## Costs

- Work assignment: `Work[h] × standardRate(at assignment start) + costPerUse`.
  The rate in force at the assignment **start** applies to the whole
  assignment; per-band prorating needs time-phased data (phase 8) —
  deviation #17.
- Material assignment: `Units × standardRate + costPerUse`.
- Cost assignment: `CostInput`.
- Task: `FixedCost` (+ `FixedCostAccrual` field: start/prorated/end) + Σ own
  assignment costs; summary cost = own `FixedCost` + Σ active children.
- Work rollup: leaf work = Σ work-assignment work; summary = Σ active
  children. Material/cost assignments contribute no work.
- Project totals = rollup over top-level tasks.

## Persistence (schema v2)

`ProjectDocument.SchemaVersion` = 2; v1 documents load with no resources.
New: `Resources` (`ResourceDocument` incl. rate tables) and `Assignments`
(`AssignmentDocument`); `TaskDocument` gains `Type`, `IsEffortDriven`,
`FixedCost`, `FixedCostAccrual`, `IgnoresResourceCalendars`. Restore paths
bypass triangle recalculation (documents store all three corners).

## CLI surface

```
p27 resource add <name> [--type work|material|cost] [--max-units 200%]
                 [--rate 50/h] [--overtime-rate 75/h] [--cost-per-use 100]
                 [--material-label t] [--calendar <name>] [--initials] [--group]
p27 resource list | show <name> | remove <name>
p27 resource set <name> [--name] [--max-units] [--rate] [--overtime-rate]
                 [--cost-per-use] [--material-label] [--calendar <name>|none]
                 [--initials] [--group] [--accrual start|prorated|end]
p27 resource set-rate <name> --from <date> [--table A..E] [--rate] [--overtime-rate] [--cost-per-use]
p27 resource remove-rate <name> --from <date> [--table A..E]

p27 assign add <task> <resource> [--units 50%] [--work 20h] [--contour bell]
               [--delay 1d] [--table B] [--cost 500]
p27 assign list [<task>]
p27 assign set <task> <resource> [--units] [--work] [--contour] [--delay] [--table] [--cost]
p27 assign remove <task> <resource>

p27 task set … [--type fixed-units|fixed-duration|fixed-work]
               [--effort-driven true|false] [--fixed-cost 1000]
               [--accrual start|prorated|end] [--ignore-resource-calendars true|false]
```

Units parse as `50%` or `0.5`. Resources are referenced by name
(case-insensitive) or `uid:<n>`. `task show` gains type, effort-driven, work,
cost, fixed cost, and an assignment table; `project show` gains total work and
cost; `--json` shapes extend accordingly (`work` in hours, e.g. `"16h"`;
costs as decimals).
