# Phase 9 — Views & fields

The field catalog, the view engine (tables, filters, sorts, groups), custom
fields with formulas and indicators, time-phased usage data, and the view
surfaces (usage grids, network diagram, calendar/timeline). Ships in
increments, each landing end-to-end:

| Increment | Contents |
|-----------|----------|
| 9a | Field catalog + view engine (filter/sort/group/tables) + CLI `view` |
| 9b | Custom fields: slots, aliases, formulas, indicators; persistence v4 |
| 9c | Time-phased usage data + usage views (CLI + web), real BCWS/ACWP proration |
| 9d | Network diagram, calendar/timeline views (web), projection unification |

## 9a — Field catalog (`Project27.Core.Fields`)

Every task attribute the product can display is a `FieldDefinition`:

```
FieldDefinition(Key, Caption, Kind, Accessor)
FieldKind: Text | Integer | Number | Percent | Cost | Work | Duration | Date | Flag
```

Keys are camelCase and stable (`"name"`, `"start"`, `"totalSlack"`,
`"baselineCost"`, `"spi"`, …). The catalog covers identity/outline, scheduling
dates, slack, tracking, baseline-0, variances (start/finish/duration/work/cost
vs baseline 0), EVM (bcws/bcwp/acwp/sv/cv/spi/cpi/bac/eac/vac), resources
(`resourceNames`), and predecessor tokens. Accessors return raw values
(string / decimal / DateTime? / bool / int); `Kind` drives formatting,
comparison, and parsing of filter literals. Durations/work/slack are decimal
**minutes** at the raw layer; renderers convert.

`FieldCatalog.Resolve(project, key)` finds built-ins, custom field ids, and
custom aliases (case-insensitive).

## 9a — View engine (`Project27.Core.Views`)

```
ViewDefinition(Fields[], Filter?, Sorts[], GroupBy?)
```

- **Filter expressions** parse from text:
  `critical = true and (duration > 3d or cost >= 1000) and name ~ "design"`.
  Operators: `= != > >= < <=`, `~` (contains, case-insensitive), combinators
  `and`, `or`, `not`, parentheses. Literals parse by the field's kind
  (`3d` → minutes via TimeSettings, dates `yyyy-MM-dd`, `true/false`,
  numbers, quoted strings).
- **Sorts**: multi-key, `field [asc|desc]` list; comparison per kind.
- **Group by**: one field; groups ordered by key, each with the field's
  formatted value as heading. (Interval grouping — "by week", cost bands —
  deferred to 9c.)
- **Tables**: named field lists — `entry`, `schedule`, `cost`, `work`,
  `tracking`, `variance`, `evm`, `summary` — mirroring MSP's built-in tables
  (clean-room field selections; deviation #24).
- Output: `ViewResult` = groups → rows (task ref + formatted cells + raw
  values). Summary rows appear unless a sort or group is active (sorting
  flattens the outline, as in MSP when "keep outline structure" is off —
  deviation #25).

## 9a — CLI

```
p27 view [--table entry|…] [--fields id,name,start,…]
         [--filter "<expr>"] [--sort "finish desc,name"] [--group-by <field>]
p27 field list [--json]     # the catalog: key, caption, kind
```

`--json` emits `{fields:[…], groups:[{key, heading, rows:[{uid, values:{…}}]}]}`
with raw values; the human table prints formatted cells.

## 9b — Custom fields

Slots per entity type task: `text1..text30`, `number1..number20`,
`cost1..cost10`, `date1..date10`, `flag1..flag20`, `duration1..duration10`
(MSP parity). A definition may add an **alias** (unique, addressable in every
field context) and either stored values or a **formula**.

Formula language (clean-room subset of MSP's, deviation #26): `[Field]`
references (built-in, custom, or alias), decimal/string/date literals,
`+ - * /`, comparisons, `and/or/not`, `IIf(cond, a, b)`, `Abs`, `Min`, `Max`,
`Round`, `Now()`, `StatusDate()`. Formula fields are computed on read;
cycles are rejected at definition time (self-reference depth guard).

**Indicators**: ordered rules `when <op> <value> then <icon>` per definition;
the first matching rule yields the icon name (rendered as text in the CLI,
as glyphs on the web later).

Persistence v4: definitions on the project, stored values per task (sparse).

```
p27 customfield define text1 --alias Phase
p27 customfield define number1 --alias Risk --formula "IIf([totalSlack] < 1d, 100, 0)"
                        [--indicator "when >= 100 then red-flag" …]
p27 customfield list | remove <id|alias>
p27 task set <ref> --field Phase="Rollout" [--field number2=42]
```

## 9c — Time-phased usage (done)

`Core.Usage.Timephased`: daily work/cost buckets per assignment — the contour's
decile pattern over the assignment span on its schedule, exactly conserving
totals; per-use/material/expense costs by resource accrual; task/summary merge
and week aggregation. CLI `p27 usage`. Note: BCWS's linear proration *is* flat
time-phasing of the baseline (no contour is stored), so EVM stands; deviation
#19 now concerns accrual only. Usage *editing* awaits a real actual-work model
(deviation #20).

## 9d — View surfaces (done)

- Server `GET /api/projects/{id}/usage?granularity=day|week` (reader role).
- Web view switcher on the project page: **Gantt** (the phase-7 split view),
  **Network** (PDM diagram: leaf tasks in dependency-rank columns, critical
  highlighting), **Timeline** (lane-packed top-level band with milestone
  callouts), **Usage** (task × time grid, work or cost, day/week).
- Layout logic (`network.ts` rank/lane assignment, `lanes.ts` interval
  packing) is pure and Vitest-covered.
- Projection unification (server `ScheduleProjection`/CLI `JsonShapes` onto
  catalog projections) intentionally deferred to phase 12 polish: both wire
  contracts are stable and consumer-specific; churning them now buys nothing.
