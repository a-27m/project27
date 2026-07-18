# Project27

Project management with MS Project desktop scope: a clean-room CPM engine with a
CLI, a REST server, and a React web client. See `docs/guide.md` for a user
guide, `docs/` for architecture/decisions/roadmap and per-phase specs,
`docs/engineering-decisions.md` for the engineering rationale trace (read it
before changing engine/serialization code), and `docs/progress.md` for status. `docker-compose.yaml` starts server +
web for evaluation; the CLI packs as a dotnet tool
(`dotnet pack src/Project27.Cli`).

## Build & test

```sh
dotnet build Project27.slnx     # 0 warnings expected (warnings are errors)
dotnet test                     # all suites
```

## CLI (`p27`)

```sh
dotnet run --project src/Project27.Cli -- init Alpha --start 2026-01-05
dotnet run --project src/Project27.Cli -- task add "Design" -d 2d
dotnet run --project src/Project27.Cli -- task add "Build" -d 3d
dotnet run --project src/Project27.Cli -- link add 1 2
dotnet run --project src/Project27.Cli -- task list
```

Works on the single `.p27` file in the current directory (or `--file`).
Add `--json` for machine output; `p27 --help` lists every verb.

## Server

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Project27.Server
```

Development enables **DevAuth** (`appsettings.Development.json`,
`Auth:DevAuth: true`): authenticate by sending `X-Dev-User: alice` (users:
alice, bob, carol). Production requires OIDC (`Auth:Authority`/`Auth:Audience`)
and refuses to start with DevAuth enabled. Storage defaults to SQLite
(`Storage:Path`); set `Storage:Provider: postgres` + `Storage:ConnectionString`
for PostgreSQL. OpenAPI (Development): `/openapi/v1.json`.

CLI against the server:

```sh
p27 --server http://localhost:5240 --dev-user alice project create Alpha
p27 --server http://localhost:5240 --dev-user alice -p Alpha task add "Design" -d 2d
```

## Web

```sh
cd web
npm install
npm run dev        # Vite dev server; proxies /api to $P27_SERVER (default http://localhost:5240)
npm test           # Vitest
npm run build      # type-check + production bundle
```

Sign in with a dev user (server must run in Development) or a bearer token.

## MCP server (`p27-mcp`)

Exposes the engine's task/schedule/resource/calendar operations as [MCP](https://modelcontextprotocol.io/)
tools (stdio transport) for AI clients like Claude Desktop or Claude Code.

```sh
dotnet run --project src/Project27.Mcp -- --file plan.p27
# or against a server project:
dotnet run --project src/Project27.Mcp -- --server http://localhost:5240 --project Alpha --dev-user alice
```

Serves one project for the process's lifetime. Point it at a `.p27` file
(`--file`/`P27_FILE`, or the sole `.p27` in the launch directory) or a
checked-out server project (`--server`/`P27_SERVER` + `--project`/
`P27_PROJECT`, `--dev-user`/`P27_DEV_USER` or `--token`/`P27_TOKEN`) — or omit
`--file`/`--project` to launch idle and let the model call `create_project`
or `open_project` once it knows what it's working with. See
`docs/spec/14-mcp-server.md`.

## Examples

[`examples/`](examples/README.md) has nine ready-to-open `.p27` files: seven
small single-feature demos (critical path, calendars, resource leveling,
cost/materials, baselines & EVM, custom fields, recurring tasks & splits) and
a 131-task realistic product-launch project exercising most of the engine at
once.
