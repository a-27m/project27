# Roadmap

Thirteen phases; each ships working, tested functionality and keeps the solution
green. Order optimizes for de-risking the scheduling engine first (everything else
hangs off it), then putting hosts around it, then broadening feature parity.
Phases map 1:1 to the session task list.

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
| 10 | Advanced scheduling | Resource leveling, inactive tasks, master/subprojects, cross-project links, resource pools, task inspector/drivers |
| 11 | Reports | Dashboard/report set, PDF/PNG export, CLI report generation, print layouts |
| 12 | Polish & packaging | Undo/redo surfaced everywhere, options parity, WCAG 2.2 AA, user docs, docker-compose deploy, `dotnet tool` packaging |

## Working agreements

- A phase is done when: tests pass, CLI exposes the new surface (from phase 3 on),
  docs/spec updated, deviations from MS Project behavior recorded in
  `docs/spec/deviations.md`.
- Feature specs are written per phase into `docs/spec/` as the phase starts —
  a single up-front spec of the entire product would go stale before use.
- Web parity for engine features lands in the phase that introduces the view
  surface (7, 9, 11), not in the engine phases.
