# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
# .NET (net10.0, central package management)
dotnet build Project27.slnx                                          # must stay at 0 warnings
dotnet test                                                          # all suites
dotnet test tests/Project27.Core.Tests                               # one suite
dotnet test tests/Project27.Core.Tests --filter "FullyQualifiedName~OutlineTests"   # one class/test

# Run the hosts
dotnet run --project src/Project27.Cli -- task list                  # CLI (p27) on the .p27 file in cwd
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Project27.Server   # :5240, DevAuth on
docker compose up --build                                            # server + web (SQLite, DevAuth)
dotnet run --project src/Project27.Mcp -- --file plan.p27             # MCP server (p27-mcp), stdio transport

# Web — all of these run from web/
npm run dev            # Vite; proxies /api to $P27_SERVER (default http://localhost:5240)
npm test               # Vitest (all)
npx vitest run src/lib/outline.test.ts                               # one test file
npm run lint           # oxlint
npm run build          # tsc -b + bundle; type errors are build errors
```

`TreatWarningsAsErrors` + `latest-recommended` analyzers are on: a warning fails the
build. Web "done" means build + test + lint all green.

Against a running server: `p27 --server http://localhost:5240 --dev-user alice -p Alpha task add "Design" -d 2d`.
DevAuth users are alice/bob/carol via the `X-Dev-User` header; the server refuses to
start with DevAuth outside Development.

## Architecture

`Project27.Core` is a pure library — no I/O, no async, no host or storage types — and
everything the product *is* lives there, so the CLI and server cannot diverge
behaviorally. `Storage` (IProjectStore + SQLite `.p27` files) and `Interop`
(MSPDI XML, CSV) sit beside it; `Cli`, `Server`, and `Mcp` (MCP tool server, `p27-mcp`)
are the three hosts; `web/` is a React 19 + TS SPA over the server's REST API. Tests
mirror `src/` one-to-one.

Cross-cutting rules that are expensive to discover by reading code:

- **Scheduling is explicit.** Mutate the aggregate, then call `Project.Recalculate()`.
  Nothing auto-recalculates; costs, assignment dates, and slack are its outputs.
- **`OutlineLevel` is 0-based**, hidden root is −1 (deviation #10).
- **Two write paths on the server, on purpose** (D6a): snapshot (`checkout` →
  `GET/PUT document` with `If-Match`) for the CLI, and command-oriented
  `POST /projects/{id}/commands` for the web. Both go through Core; the command layer
  computes inverses server-side for undo/redo.
- **Editing is checkout/check-in** (D6): one editor per project, readers never blocked,
  SSE notifies clients of lock changes.
- **Every web operation must have a CLI equivalent** (D4) — parity is a product
  requirement, not a nice-to-have.
- **Web has zero runtime deps beyond React** (E26): pure logic lives in `web/src/lib/`
  and is unit-tested; components are thin over it. The Gantt/timeline are hand-built SVG.

## Documentation map

The docs are the source of truth and are kept current — read them rather than
re-deriving from code.

- `docs/engineering-decisions.md` — E1–E34, the rationale trace. **Read before changing
  engine, scheduling, or serialization code**; it records why the non-obvious choices are
  non-obvious and which approaches already failed.
- `docs/decisions.md` — D1–D9, product/architecture decisions locked with the owner.
  Binding until explicitly revisited; record amendments there.
- `docs/progress.md` — current state, phase log, and the "engine facts that bite" list
  (summary durations, calendar presets, the effort triangle, xUnit v3 quirks). Update it
  when you finish a major task.
- `docs/spec/` — per-phase specs (01–14) plus `deviations.md`, the numbered list of
  intentional divergences from MS Project. The initial implementation is finished, all
  roadmap phases are complete, so next major tasks are better called "epics" but should
  be documented like phases were.
- `docs/architecture.md` (system view), `docs/guide.md` (user guide), `docs/roadmap.md`.

## Conventions

Conventional commits. YAML files use `.yaml`, never `.yml`. Namespaces `Project27.*`;
CLI binary `p27`; local project file `.p27`. Engine behavior is locked by
golden-scenario tests — if one fails, you changed semantics.
