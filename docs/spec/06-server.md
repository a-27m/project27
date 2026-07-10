# Phase 6 — Server

ASP.NET Core minimal-API host (`Project27.Server`) wrapping the same engine as the
CLI: OIDC + DevAuth authentication, per-project roles, checkout/check-in locking
(D6), SQLite or PostgreSQL storage, OpenAPI, SSE change events, and CLI `--server`
mode.

## API shape: snapshot-oriented v1

The write model of API v1 is the **project document snapshot** (the same
schema-versioned JSON stored inside `.p27` files): checkout → GET document →
mutate → PUT document (check-in). The CLI embeds the engine, so every existing
verb works against a server project unchanged — fetch, mutate in-process,
check-in.

The command-oriented write surface sketched in `architecture.md`
(`POST /projects/{id}/commands`) is **deferred to phase 7**, where the web client
that needs fine-grained edits arrives together with the Core command layer
(amendment D6a in `decisions.md`).

## Authentication (D5)

Two schemes, selected by configuration:

- **OIDC / JWT bearer** — `Auth:Authority` + `Auth:Audience`; any compliant
  provider. User identity = `sub` claim; display name = `name` or
  `preferred_username`.
- **DevAuth** — enabled by `Auth:DevAuth: true`. The caller picks a user with the
  `X-Dev-User: <id>` header from the static list `Auth:DevUsers` (defaults
  `alice`, `bob`, `carol`). Issues the same claims shape as OIDC. The server
  **throws at startup** if DevAuth is enabled outside the `Development`
  environment.

Every `/api/*` endpoint requires authentication; there is no anonymous surface
except `/openapi/v1.json` in Development.

## Authorization: per-project roles

`owner` / `editor` / `reader` per project, stored in the members table. The
creator becomes `owner`. Role gates:

| Action | reader | editor | owner |
|--------|:-:|:-:|:-:|
| list / read info, document, events | ✓ | ✓ | ✓ |
| checkout, check-in, cancel own lock | | ✓ | ✓ |
| steal another user's lock | | stale only | ✓ (always) |
| manage members, delete project | | | ✓ |

A lock is **stale** once idle (no checkout refresh/check-in) longer than
`Locking:StaleAfterMinutes` (default 30).

## Endpoints

```
GET    /api/projects                          list projects visible to the caller
POST   /api/projects                          {name, start} → creates + owner role
GET    /api/projects/{id}                     info: name, version, lock, own role
DELETE /api/projects/{id}                     owner only
GET    /api/projects/{id}/document            latest snapshot (ETag: version)
POST   /api/projects/{id}/checkout            acquire/refresh lock → {version, lockedBy…}
PUT    /api/projects/{id}/document            check-in: If-Match: <version>, body = document;
                                              validates by loading + Recalculate; bumps version;
                                              releases the lock unless ?keep=true
DELETE /api/projects/{id}/lock                release own / steal stale (editor) / steal any (owner)
GET    /api/projects/{id}/members             list {userId, role}
PUT    /api/projects/{id}/members/{userId}    {role} — owner only; cannot demote the last owner
DELETE /api/projects/{id}/members/{userId}    owner only; cannot remove the last owner
GET    /api/projects/{id}/events              SSE: checkout | lock-released | checkin
GET    /api/me                                caller identity {id, name}
```

Errors are RFC 9457 problem details: 401 unauthenticated, 403 role, 404 unknown
or invisible project, 409 lock conflicts and version mismatches, 422 document
validation failures.

Concurrency: check-in requires holding the lock **and** `If-Match` equal to the
version the checkout returned; a stolen lock therefore cannot silently clobber a
concurrent check-in.

## Storage

`IServerStore` (server-side; distinct from the file-oriented `IProjectStore`):

- **SQLite** (default, `Storage:Provider: sqlite`, `Storage:Path`) — single file,
  small installs and tests.
- **PostgreSQL** (`Storage:Provider: postgres`, `Storage:ConnectionString`) —
  same schema, Npgsql.

Tables: `projects(id, name, created_by, created_at, version)`,
`snapshots(project_id, version, document, saved_by, saved_at)` (history kept —
baseline for phase 8 interim plans), `members(project_id, user_id, role)`,
`locks(project_id, user_id, acquired_at, refreshed_at)`. All timestamps UTC.

## SSE

`GET /api/projects/{id}/events` streams `event: <kind>` + JSON data lines from an
in-memory per-project broadcaster (single-node scope; multi-node fan-out is out of
scope until it is needed). Readers use it to learn of new check-ins; phase 7 web
clients subscribe for live refresh.

## CLI `--server` mode (D8)

Global options: `--server <url>` (or `P27_SERVER`), `--token <jwt>` (or
`P27_TOKEN`), `--dev-user <id>` (DevAuth). With `--server`, `--file` is replaced
by `--project <name|id>`:

```
p27 --server http://host project list | create | delete
p27 --server http://host --project Alpha task add "Design" -d 2d
p27 --server http://host --project Alpha checkout | checkin | unlock [--force]
```

Read verbs GET the document and render via the in-process engine. Mutating verbs
run checkout → GET → mutate → PUT (release) atomically per invocation; an
explicit `p27 checkout` holds the lock across invocations (subsequent check-ins
pass `?keep=true` until `p27 checkin`).

## Testing

Integration tests host the server in-process (`WebApplicationFactory`) with
DevAuth + SQLite: auth guards, role matrix, lock lifecycle incl. staleness and
steal, version conflict on check-in, document round-trip, SSE smoke. CLI
server-mode tests reuse the same in-process server through an injected
`HttpMessageHandler`. Postgres store shares SQL with SQLite through a provider
abstraction; a live-Postgres test run is gated behind `P27_TEST_POSTGRES`.
