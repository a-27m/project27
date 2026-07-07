# Architecture

## Overview

Project27 is a project-management system with MS Project Professional desktop scope
(see `decisions.md` D4), built as a modular core consumed by two hosts:

```
                ┌────────────────────────────────────────────┐
                │                Project27.Core               │
                │  domain model · calendars · CPM scheduler   │
                │  tracking/EVM · leveling · field catalog    │
                │  commands + undo/redo · view projections    │
                └───────▲────────────────────────▲───────────┘
                        │                        │
        ┌───────────────┴───────┐    ┌───────────┴─────────────┐
        │   Project27.Storage   │    │    Project27.Interop    │
        │ IProjectStore + SQLite│    │  MSPDI XML r/w · CSV    │
        │ (Postgres in Server)  │    └───────────▲─────────────┘
        └───────▲───────────────┘                │
                │                                │
   ┌────────────┴───────────┐      ┌─────────────┴─────────────┐
   │      Project27.Cli     │      │      Project27.Server     │
   │ in-process engine on   │      │ ASP.NET Core REST + OIDC  │
   │ local .p27 files, or   │──────▶ checkout/check-in, roles, │
   │ REST client (--server) │ REST │ Postgres, OpenAPI         │
   └────────────────────────┘      └─────────────▲─────────────┘
                                                 │ REST + SSE
                                   ┌─────────────┴─────────────┐
                                   │        web/ (React+TS)    │
                                   │ task sheet · Gantt · views│
                                   └───────────────────────────┘
```

## Core (`Project27.Core`)

Pure .NET library: no I/O, no async, no host or storage types. Everything the
product *is* lives here so CLI and server can never diverge behaviorally.

Key parts:

- **Domain model** — `Project` aggregate: tasks (outline tree), resources,
  assignments, calendars, baselines, custom-field definitions and values, options.
  The aggregate is the consistency boundary; there are no partial recalculations
  visible from outside.
- **Calendar engine** — working-time arithmetic over base/resource/task calendars
  with weekly patterns and exceptions. All scheduling math funnels through it.
- **Scheduler** — clean-room CPM (D7): forward/backward pass, constraints,
  dependency types with lead/lag, task types × effort-driven interaction,
  assignment-level scheduling with contours, slack, critical path. Deterministic:
  same aggregate in ⇒ same schedule out.
- **Command layer** — every mutation is a command object (rename task, set
  dependency, level resources…). Commands produce inverse commands ⇒ multi-level
  undo/redo for free on every host, and a natural audit/API surface.
- **View projections** — tables, filters, groups, sorts evaluated in Core against
  the field catalog, so CLI `--json`, web grids, and reports all consume identical
  projections.

Time is handled as follows: wall-clock timestamps are `DateTime` (unspecified kind)
interpreted in the project's time zone; the calendar engine is the only component
doing date arithmetic.

## Storage (`Project27.Storage`)

`IProjectStore` abstraction with two implementations:

- **SQLite** — the local `.p27` project file (single file, WAL off for portability).
  Used by the CLI and small server installs.
- **PostgreSQL** — server deployments (lives with the server project to keep Core
  host-free).

Persistence is snapshot-based per check-in/save (documents, not event sourcing),
with a schema-versioned migration path.

## Server (`Project27.Server`)

ASP.NET Core minimal APIs.

- **Auth**: OIDC (any compliant provider) + `DevAuth` (D5). Claims → per-project
  roles: owner/editor/reader.
- **Editing model**: checkout/check-in (D6). Checkout materializes the aggregate
  in server memory; commands are applied via the Core command layer; check-in
  persists a new snapshot and releases the lock. Readers get the last checked-in
  snapshot. SSE notifies open clients of check-ins and lock changes.
- **API**: REST, resource-oriented for reads, command-oriented for writes
  (`POST /projects/{id}/commands`), OpenAPI published.

## CLI (`Project27.Cli`)

`System.CommandLine` verbs (D8). Local mode opens `.p27` via SQLite storage and the
in-process engine; `--server` mode issues the same commands over REST. Output
formatting is a thin renderer over Core view projections: aligned tables for
humans, `--json` for machines.

## Web (`web/`)

React + TypeScript (Vite). Custom SVG Gantt/timeline and virtualized editable
grids. State: server snapshots + optimistic local command queue while holding the
checkout lock; full recalc results come back from the server per command batch.

## Testing strategy

- **Engine**: exhaustive unit tests for calendar math; golden-scenario tests for
  the scheduler (fixture project in, expected dates/slack out) — these lock D7
  semantics and gate refactors.
- **Interop**: MSPDI round-trip golden files.
- **Server**: integration tests with DevAuth + SQLite; contract tests generated
  from OpenAPI for the web client.
- **Web**: component tests for grid/Gantt interaction logic; Playwright smoke on
  the primary workflows.

## Solution layout

```
Project27.slnx
Directory.Build.props / Directory.Packages.props   central config
docs/                                              this documentation
src/Project27.Core            engine + domain (no dependencies)
src/Project27.Storage         IProjectStore + SQLite
src/Project27.Interop         MSPDI, CSV
src/Project27.Cli             p27 binary
src/Project27.Server          REST host (+ Postgres store)
tests/<project>.Tests         mirrors src
web/                          React + TS SPA
```

Projects are added in the phase that first needs them (see `roadmap.md`); the
solution always builds green.
