# Decision log

Decisions locked with the product owner on 2026-07-07, before any implementation.
Each entry is binding until explicitly revisited; record amendments here.

## D1 — Deployment model: modular core, dual-host

The scheduling engine and domain model live in a pure .NET library (`Project27.Core`)
with no I/O, no persistence, no host assumptions. Two hosts consume it:

- **CLI** (`Project27.Cli`): links the engine in-process, operates on local
  SQLite-backed project files. Works fully offline.
- **Server** (`Project27.Server`): ASP.NET Core wraps the same engine behind a REST
  API for the web frontend. PostgreSQL primary storage; SQLite acceptable for small installs.

Storage is a pluggable abstraction shared by both hosts.

## D2 — Web frontend: React + TypeScript

SPA (Vite) against the REST API. The Gantt chart, timeline, and time-phased grids
are custom-built (SVG/canvas); capable off-the-shelf Gantt components are GPL or
commercial. Accepted cost: a second language in the repo.

## D3 — Interoperability: MSPDI XML both ways, CSV export-only

Import and export of the documented Microsoft Project XML schema (MSPDI). CSV export
of any table/view. No binary `.mpp` support. Native storage is our own format (D1).

## D4 — Scope: MS Project Professional parity, phased — with exclusions

End state is the feature set of current MS Project Professional **desktop**, excluding:

- Team Planner view
- OLE and other Windows-only / legacy-adjacent integrations (COM automation,
  ActiveX, embedded objects, MAPI mail integration)
- Project Server / Project Online–only enterprise features (beyond what D6 covers)

UI is redesigned per surface (web, CLI) but every workflow must be reachable on both.

## D5 — Auth: OIDC, with a built-in development scheme

Production authenticates via an external OIDC provider. A `DevAuth` scheme is
compiled in but usable only in the Development environment: auto-login / pick-a-user
from a static list, issuing the same claims shape as OIDC. The server **refuses to
start** if DevAuth is enabled in a non-Development environment. Per-project roles:
owner / editor / reader.

## D6 — Server concurrency: checkout/check-in locking

One editor per project at a time. Explicit checkout to edit, check-in to release;
lock owner (or project owner) may steal a stale lock. Readers are never blocked.
Chosen for correctness-per-cost over optimistic merging or real-time co-editing.

### D6a — Amendment (2026-07-11): API v1 is snapshot-oriented

Phase 6 ships checkout → `PUT` document → check-in (whole-snapshot writes). The
command-oriented write surface (`POST /projects/{id}/commands`) and the Core
command layer it rides on arrive with the web client in phase 7, which is the
first consumer that needs fine-grained edits. The CLI embeds the engine, so
snapshot writes give it full server parity today.

## D7 — Engine semantics: clean-room

A principled CPM engine using MS Project's *concepts* (task types, effort-driven,
constraints, calendars, leveling) with freely improved semantics where MS Project's
behavior is quirky or undocumented. Imported real-world plans may schedule slightly
differently; deviations we know about are documented in `docs/spec/deviations.md`.

## D8 — CLI: dual-mode, git-style verbs, no TUI

Verbs like `p27 task add`, `p27 schedule recalc`, `p27 view gantt --format=json`.
Default target: local project file via the in-process engine. `--server`/profile
switches to the REST API. Human tables by default, `--json` for scripting.
Every web operation has a CLI equivalent (D4).

## D9 — Conventions

- .NET 10, C# latest, nullable enabled, warnings as errors, central package management.
- xUnit v3 for tests. Engine behavior is locked by golden-scenario tests.
- Conventional commits. YAML files use the `.yaml` extension.
- Namespaces/solution: `Project27.*`; CLI binary: `p27`; local project file: `.p27`.
