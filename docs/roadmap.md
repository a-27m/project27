# Roadmap

Thirteen phases; each ships working, tested functionality and keeps the solution
green. Order optimizes for de-risking the scheduling engine first (everything else
hangs off it), then putting hosts around it, then broadening feature parity.
Phases map 1:1 to the session task list. **Current status: see `progress.md`** —
all thirteen phases are complete (5 ran after 12, as planned); work since then is
tracked as epics below.

| # | Phase | Delivers |
|---|-------|----------|
| 0 | Scaffold & docs | Repo, solution config, architecture/decisions/roadmap |
| 1 | Domain + calendar engine | Project settings, durations (incl. elapsed/estimated), calendars with exceptions & inheritance, working-time arithmetic |
| 2 | CPM scheduler | Outline/WBS, milestones, dependency types + lead/lag, constraints, deadlines, task types × effort-driven, slack, critical path, splits, recurring tasks |
| 3 | Persistence + CLI | `.p27` SQLite format, `IProjectStore`, project lifecycle, `p27` verbs for the engine surface so far, `--json` |
| 4 | Resources & costs | Work/material/cost resources, rate tables, assignments, contours, resource calendars in scheduling, cost rollup |
| 5 → after 12 | Interop (postponed) | MSPDI XML import/export with round-trip tests, CSV export — runs after phase 12 |
| 6 | Server | REST API, OIDC + DevAuth, roles, checkout/check-in, Postgres, OpenAPI, CLI `--server` |
| 7 | Web foundation | App shell, auth flow, virtualized task sheet, custom Gantt with drag/link, split views |
| 8 | Tracking & EVM | Baselines 0–10, interim plans, status date, actuals, reschedule uncompleted work, earned value fields |
| 9 | Views & fields | Usage views (time-phased editing), network diagram, calendar/timeline views, full field catalog, custom fields with formulas/indicators, filters/groups/sorts/tables |
| 10 | Advanced scheduling | Resource leveling, inactive tasks, resource pools, task inspector/drivers. Master/subprojects and cross-project links: **extension point only** (interfaces + storage hooks), full implementation revisited at the very end (after 12/5) |
| 11 | Reports | Dashboard/report set, PDF/PNG export, CLI report generation, print layouts |
| 12 | Polish & packaging | Undo/redo surfaced everywhere, options parity, WCAG 2.2 AA, user docs, docker-compose deploy, `dotnet tool` packaging, **full web/CLI feature parity** (spec 12 matrix) |

## Epics

The thirteen phases are complete, so subsequent major work is numbered as epics and
documented the same way (a spec in `docs/spec/`, deviations recorded, CLI parity per D4).

| # | Epic | State |
|---|------|-------|
| 13 | Tracking parity (`13-tracking-parity.md`), shell completion (`13-shell-completion.md`) | Done |
| 14 | MCP server (`14-mcp-server.md`) | Done |
| 15 | Gantt maturity (`15-gantt-maturity.md`) | Specced |

### Candidate epics — no spec yet

Recorded so they are not re-derived from scratch later. Both are explicit non-goals of
epic 15: each is a view surface and a scheduling model of its own, not a Gantt polish item.

- **Critical-chain buffers.** CCPM as a first-class scheduling mode: aggressive
  (50%) task estimates, project and feeding buffers sized from the chains they protect,
  the critical *chain* (resource-constrained, not just logic-constrained) as a distinct
  concept from the critical path, and buffer-consumption fever charts for tracking.
  Touches the engine, not only the view — leveling and the CPM pass both have a stake.
- **Linear-schedule (time–distance) views.** Time on one axis, chainage/location on the
  other, for linear works — rail, highway, pipeline, tunnelling, high-rise floors. Tasks
  become sloped production lines whose gradient is a rate of progress, and the analysis
  people want is interference between crews in the same location, which no bar chart
  shows. Needs a location/chainage dimension on the task before the view is meaningful.

## Working agreements

- A phase is done when: tests pass, CLI exposes the new surface (from phase 3 on),
  docs/spec updated, deviations from MS Project behavior recorded in
  `docs/spec/deviations.md`.
- Feature specs are written per phase into `docs/spec/` as the phase starts —
  a single up-front spec of the entire product would go stale before use.
- Web parity for engine features lands in the phase that introduces the view
  surface (7, 9, 11), not in the engine phases.
