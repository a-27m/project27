# Phase 12 — Polish, packaging & web parity

Closes the roadmap's polish items **and** (product decision 2026-07-11) brings
the web UI to feature parity with the CLI. Ships in increments:

| Increment | Contents | Status |
|-----------|----------|--------|
| 12a | dotnet-tool packaging, Dockerfiles + docker-compose, user guide | done |
| 12p-1 | Command ops covering the whole engine surface; server `view` + `drivers` endpoints | |
| 12p-2 | Web task inspector: every task field, predecessors, assignments | |
| 12p-3 | Web resources page, project settings, baseline/level/reschedule menus | |
| 12p-4 | Web table view (fields/filter/sort/group), custom fields & calendar managers, drivers popover | |
| 12b | Undo/redo (command inverses) in the web; accessibility pass (WCAG 2.2 AA targets) | |

## Web ⇄ CLI parity matrix

| Area | CLI (exists) | Web plan |
|------|--------------|----------|
| Project settings | `project set` (name, start, schedule-from, calendar, time settings, critical slack, status date) | Settings dialog → extended `setProject` op (12p-3) |
| Task fields | `task set` (all) | Inspector panel, tabs General / Advanced / Tracking / Resources / Predecessors / Custom (12p-2) |
| Outline & structure | add/move/indent/outdent/remove/split/unsplit | Toolbar + inspector actions (split/unsplit new ops exist) (12p-2) |
| Recurring tasks | `task add-recurring` | Add-dialog with recurrence builder → new `addRecurringTask` op (12p-4) |
| Links | `link add/set/remove` | Gantt drag (exists) + inspector predecessor editor with type/lag (12p-2) |
| Calendars | 10 `calendar` verbs | Calendar manager dialog → calendar ops (12p-1 ops, 12p-4 UI) |
| Resources | add/set/remove, rate tables | Resources page (12p-3) → resource ops (12p-1) |
| Assignments | assign add/set/remove | Inspector Resources tab (12p-2) → assignment ops (12p-1) |
| Baselines | `baseline set/clear` | Toolbar menu (ops exist) (12p-3) |
| Tracking | tracking fields, `--status-date`, `schedule reschedule` | Inspector Tracking tab + toolbar (new `reschedule` op) (12p-2/3) |
| EVM | `task evm` | Table view with the evm table (12p-4, via `view` endpoint) |
| Views/fields/filters | `view`, `field list` | Table view mode: column picker, filter bar, sort, group (12p-4, server `view` endpoint) |
| Custom fields | `customfield`, `task set --field` | Manager dialog + Custom tab in inspector (12p-4) |
| Usage | `usage` | Usage view (exists; editing stays deferred — deviation #20) |
| Leveling | `level run/clear`, `task drivers` | Toolbar menu + drivers popover (`drivers` endpoint) (12p-3/4) |
| Reports | `report` | Reports menu (exists) |
| Cross-file (`resource import`, local files) | file-based | n/a on the web (server projects); pool linking is the subprojects extension point |

## 12p-1 — API surface

New command ops (all delegate to existing engine methods; uid-addressed):
`addResource`, `setResource`, `removeResource`, `setResourceRate`,
`removeResourceRate`, `assign`, `setAssignment`, `unassign`, `addCalendar`,
`removeCalendar`, `setCalendarDay`, `setCalendarBase`, `addCalendarException`,
`removeCalendarException`, `addWorkWeek`, `removeWorkWeek`,
`defineCustomField`, `removeCustomField`, `addRecurringTask`, `reschedule`;
`setTask` gains `customValues` (name → string, parsed by field kind; null
clears); `setProject` gains the time settings and `criticalSlack`.

New endpoints (reader role):

```
GET /api/projects/{id}/view?fields=&filter=&sort=&groupBy=&table=
GET /api/projects/{id}/drivers/{uid}
```

`view` evaluates the Core view engine server-side (custom fields and formulas
included) and returns the CLI's JSON shape; `drivers` returns
`TaskDrivers.Explain`. The schedule projection additionally carries the
resource list and calendar names so the web can populate pickers without extra
round trips.

## 12b — Undo/redo & accessibility

Undo/redo: `CommandExecutor` learns to produce **inverse commands** for the
mutating ops (captured from pre-state); the web keeps an undo/redo stack while
holding the lock and replays inverses through the same commands endpoint. CLI
stays without undo (files are snapshots; `git`-style workflows cover it) —
documented deviation.

Accessibility targets (WCAG 2.2 AA): full keyboard operability of sheet and
dialogs, visible focus, ARIA roles/labels on grids and toolbars, 4.5:1 text
contrast, reduced-motion respect. Verified with axe where automatable.
