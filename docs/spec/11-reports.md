# Phase 11 — Reports

A built-in report set rendered as **self-contained HTML** (inline CSS, inline
SVG, no external assets) by `Project27.Core.Reports.ReportBuilder` — one
renderer for the CLI, the server, and the web. Print stylesheets make the
browser's print-to-PDF the PDF/PNG export path (no headless-browser dependency;
bundling one is a packaging concern revisited in phase 12 — deviation #31).

## Report set

| Name | Contents |
|------|----------|
| `overview` | Project stat cards (dates, % complete, work, cost, SPI/CPI when baselined), milestones, the critical path |
| `critical` | Critical tasks with slack, dates, resources |
| `late` | Tasks finishing later than baseline 0 (variance table); hints when no baseline exists |
| `resources` | Per resource: assignments, work, cost, overallocated days |
| `costs` | Cost rollup by top-level task with an inline SVG cost bar chart, plus the most expensive tasks |
| `upcoming` | Tasks starting or finishing within 14 days of the status date (falls back to today) |

All tabular data comes from the field catalog / view engine, EVM, timephased,
and leveling projections — reports add no new computation.

## Surfaces

```
p27 report list
p27 report <name> [--out file.html]     # default <project>-<name>.html in cwd
GET /api/projects/{id}/reports/{name}   # text/html, reader role
```

Web: a Reports menu on the project page fetches the HTML (auth headers apply)
and opens it in a new tab via a blob URL.
