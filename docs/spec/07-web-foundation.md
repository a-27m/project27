# Phase 7 — Web foundation

React + TypeScript SPA (`web/`, decision D2) against the phase-6 server, plus the
two server-side pieces it needs: the **Core command layer** and a **computed
schedule projection**.

## Core command layer (`Project27.Core.Commands`)

Every fine-grained mutation is a serializable command record; `CommandExecutor`
applies a batch to a `Project` aggregate. JSON is polymorphic on `"op"`
(camelCase discriminators): `addTask`, `setTask`, `removeTask`, `moveTask`,
`indentTask`, `outdentTask`, `link`, `setLink`, `unlink`, `splitTask`,
`unsplitTask`, `setProject`.

- Tasks are addressed by **uid** (stable), not row number.
- Durations travel as strings in engine syntax (`"3d"`, `"4eh"`); dates as ISO
  8601; lags as `{kind, value}` minutes/percent.
- "Absent field = unchanged"; clearing an optional field uses an explicit
  `clearX: true` flag (JSON cannot distinguish absent from null reliably).
- The executor does **not** recalculate; the host applies the batch, then runs
  `Recalculate()` once.
- Inverse commands (undo/redo) are deliberately deferred to phase 12; the
  record shapes are already granular enough to support them.
- Resource/assignment commands arrive with the usage views (phase 9); the CLI
  remains on whole-snapshot writes (D6a) until then.

## Server additions

```
GET  /api/projects/{id}/schedule          computed projection (reader)
POST /api/projects/{id}/commands          apply a command batch (editor, lock required)
```

- `/schedule` loads the latest snapshot, recalculates, and returns
  `{version, project, tasks[], links[]}` — tasks carry uid, row, outline level,
  wbs, flags (summary/milestone/critical/active/manual), duration text, dates,
  slack, segments, and predecessor references; links carry uids, type, lag.
  (A stopgap projection owned by the server; the Core field catalog unifies
  projections in phase 9.)
- `/commands` requires the caller to **hold the lock** (web checks out first).
  Body: `[{op: …}, …]`. The server loads the latest snapshot, applies the
  batch, recalculates, saves version+1 (keeping the lock), publishes `checkin`,
  and returns `{version, createdUids[], schedule}` — one round trip per edit.
  422 with the engine's message when a command is invalid (cycle, bad ref, …);
  409 when the lock is not held.

## Web app

Vite + React 19 + TypeScript, no UI framework and no Gantt/grid dependencies
(custom per D2). `VITE_API_URL` points at the server (Vite dev proxy for
`/api` by default).

- **Auth flow**: a sign-in screen that stores either a bearer token or a
  DevAuth user in `localStorage`; every request carries
  `Authorization: Bearer …` or `X-Dev-User: …`. `GET /api/me` validates.
- **Shell**: project list (name, version, role, lock holder) → project view.
- **Project view** — the split view:
  - Left: **task sheet**, custom virtualization (fixed row height, windowed
    rendering), columns ID / Name (indented by outline level) / Duration /
    Start / Finish / Predecessors; inline edit of name and duration.
  - Right: **Gantt** (SVG): day/week timescale header, task bars from
    segments, summary brackets, milestone diamonds, dependency lines,
    critical-path coloring, today line.
  - One shared vertical scroll; a draggable splitter divides sheet and chart.
- **Editing model**: read-only until **Checkout**; while holding the lock every
  edit posts a command batch and swaps in the returned schedule; **Check in**
  releases the lock. SSE (`/events`) refreshes readers on `checkin` and
  updates the lock banner on `checkout`/`lock-released`.
- **Gantt interactions** (drag state machines are pure TS, unit-tested):
  - drag a bar horizontally → `setTask` with a start-no-earlier-than
    constraint at the dropped date (auto tasks) — MSP's drag behavior;
  - drag from a bar's link handle onto another bar → `link` (FS).

## Testing

- Pure logic is unit-tested with Vitest: timescale math, virtualization
  windowing, drag state machines, API payload mapping.
- `npm run build` (tsc + vite) and `npm test` must pass; both are part of the
  phase's definition of done alongside `dotnet test`.
- Playwright browser smoke is deferred until a CI environment with browsers
  exists; tracked for phase 12.
