# backend

The caster website for the tournament: a Go service that takes telemetry pushed by the mod's game
server, keeps the current match in memory and fans it out over WebSocket to a live caster
dashboard and OBS browser-source overlays. It also serves the bracket, the public tournament
overview, an operator console that sends commands and config changes back to the game server, and
a home page where anyone can create a booth of their own for 24 hours.

The event instance ran at `sn.caes.ar`.

## Layout

```
cmd/caster/          the service
cmd/record/          capture a live feed to an NDJSON file
cmd/replay/          push a recording back into /ingest, without the game
cmd/soak/            drive a booth under load and report what broke
cmd/worldmesh/       build the 3D map's terrain mesh from a Subnautica install
internal/config/     environment -> validated Config
internal/caster/     routes, ingest, the in-memory store, the WebSocket hub, limits, booths
internal/telemetry/  the push contract, parsed and version-checked
internal/tournament/ Postgres store: attributions, bracket, roster, command queue, configs, booths
internal/soak/       the load driver behind cmd/soak
internal/world/      reads the game's terrain octrees and builds the isosurface
internal/web/        the embedded front-end (static/) and its node tests (tests/)
```

Dependencies: the Go standard library, `github.com/coder/websocket` and `github.com/lib/pq`. The
front-end has no build step and no npm dependencies; three.js is vendored under
`internal/web/static/vendor/`. Files under `static/` are embedded with `//go:embed`, so a change
there needs a rebuild of the binary.

The telemetry contract the mod pushes is documented in
[`docs/telemetry/README.md`](../docs/telemetry/README.md).

## Booths and tokens

One process serves several **booths**. A booth is one tournament's live state with three secrets:

| Token | Who holds it | What it opens |
| --- | --- | --- |
| ingest | the game server | `POST /ingest`, `POST /console` |
| view | casters, OBS | `GET /live`, `GET /camera` (follow), `GET /bracket`, the dashboard, events and overlays |
| operator | the tournament operator | every write, `GET /console`, `GET /rules/{revision}`, driving `GET /camera` |

Tokens go in `Authorization: Bearer <token>`. The view token may also be passed as `?token=`,
because a browser cannot set headers on a WebSocket; the dashboard removes it from the address bar
once read and keeps it in `localStorage`, scoped per booth.

Every booth route answers both bare (`/ingest`) and under `/b/{booth}/` (`/b/ada/ingest`). A bare
path is the reserved `tournament` booth. The booth is chosen by the token presented, never by
anything in a payload, and a refusal looks the same whether the booth exists or not.

Booths come from two places:

- **Configured** booths, from the environment (`LMS_BOOTHS`, or the single `tournament` booth).
- **Created** booths, from `POST /booths` on the home page. These need a database, live 24 hours,
  get a random 10-character id and three 256-bit tokens (stored as SHA-256 hashes), and are swept
  every five minutes. Expiry is also checked on every request.

## Pages

All pages also work under `/b/{booth}/`, and work out their booth from their own address.

| Path | Page | Token |
| --- | --- | --- |
| `/home.html` | create a booth; directory of every page and OBS source | none |
| `/` | caster dashboard: clock, zone, player table, event timeline, flat or 3D map with depth cut | view |
| `/events.html` | the full event feed the booth still holds | view |
| `/overlay.html?panel=…` | OBS browser sources, see below | view |
| `/bracket.html` | standings, groups, the final; operators attribute kills and adjust points here | view (operator to write) |
| `/tournament.html` | public overview: what is on now, the stages, the schedule in the reader's time zone | none if the bracket is published, else view |
| `/console.html` | operator console: commands, connected players, roster, config editor | operator |

### OBS overlays

Add each as a Browser source with the view token in the URL:

| Panel | Address | Size |
| --- | --- | --- |
| status strip | `/overlay.html?panel=status&token=…` | 480 × 90 |
| elimination cards | `/overlay.html?panel=toasts&token=…` | 560 × 260 |
| match board | `/overlay.html?panel=board&token=…` | 360 × 420 |
| tournament table | `/overlay.html?panel=table&token=…` | 400 × 520 |
| 3D map | `/overlay.html?panel=map&token=…` | 1920 × 1080 |

`/overlay.html` without a panel shows a setup card listing these addresses. Overlays never show an
error on camera: a dropped feed keeps the last frame, and outside a match every panel except the
table draws nothing.

The map source takes `camera=crater|top|rim` for its shot (default `crater`). With
`control=<channel>` it follows a caster: open the dashboard at `/?control=<channel>` in a browser
that has the operator token saved, switch to 3D, and the source mirrors that camera and depth cut.
One driver per channel. The map loads a ~57 MiB terrain mesh, so leave OBS's "Shutdown source when
not visible" off.

## HTTP and WebSocket endpoints

| Route | Token | Purpose |
| --- | --- | --- |
| `GET /healthz` | none | `{"status":"ok","service":"lms-caster"}`. Process-wide, not per booth |
| `POST /booths` | none | create a booth. `201` with `id`, the three tokens, `expiresAt`, `dashboardUrl`, `ingestUrl`. `429` per source address over `LMS_BOOTH_RATE`; `503` at `LMS_MAX_BOOTHS` or without a database |
| `POST /ingest` | ingest | one telemetry push. `202` with `accepted`, `newEvents`, `lastSequence`, `commands`, and optionally `rules` and `bracket`. `429` with `Retry-After` over `LMS_INGEST_RATE` |
| `GET /live` | view | WebSocket feed. `?after=<seq>` resumes from a cursor. Closes with `1013` at `LMS_MAX_LIVE_CLIENTS`; idle connections are pinged every 30 s |
| `GET /camera?control=<channel>` | view / operator | WebSocket for a caster-driven map camera. Followers use the view token; the driver offers subprotocol `lms.camera.operator` plus the operator token. A second driver is closed with `1013` |
| `GET /bracket` | view, or none if published | standings, groups, the final's seats and the attribution count. Also `HEAD` |
| `POST /attributions` | operator | record who killed whom. Append-only; the latest correction wins |
| `POST /adjustments` | operator | give or take points (±100) for a player in a group, with a reason |
| `PUT /groups` | operator | replace the bracket: `{"groups":[{number,name,series,members}],"final":[{series,seats}]}` |
| `POST /commands` | operator | queue an `lms` command for the game server |
| `POST /console` | ingest | the game server's report: who is connected, its command vocabulary, command results, the config it is running |
| `GET /console` | operator | what the console page draws |
| `PUT /people` | operator | replace the roster. Removing anyone needs `"removing": <n>` or the write is refused with `409` |
| `POST /overrides` | operator | an exception to the derived ghost rule |
| `PUT /rules` | operator | a new revision of the tournament config (YAML). The game server validates and adopts it in the lobby |
| `GET /rules/{revision}` | operator | one stored revision, to restore it via `PUT /rules` |

The service never connects to the game server. Commands, config revisions and the current group's
roster ride back on the reply to a push. The mod reports progress in two request headers:
`X-Lms-Commands-Ran` (the last command id it ran) and `X-Lms-Rules-Revision` (the config revision it
runs). A push without them is offered no commands or rules. Queued commands expire after
`LMS_COMMAND_TTL`.

Ingest is idempotent: only events above the stored sequence are taken, and a log that restarts at
sequence 1 is treated as a restarted game server.

While a group is playing, the roster reconciler compares who should be a ghost (everyone connected
who is not in that group, plus overrides) with what the game server reports, and queues `ghost` /
`unghost` commands for the difference. It does nothing without a database, when the game server
does not accept commands, before a match is pushed, or when the playing group has no members.

## Configuration

Everything has a default, so the container starts with no environment. A variable that is set but
invalid stops startup with an error naming it. Tokens are taken verbatim; other values are trimmed.
An unset token turns that function off rather than leaving it open.

| Variable | Default | Meaning |
| --- | --- | --- |
| `LMS_PORT` | `8080` | listen port |
| `LMS_LOG_LEVEL` | `info` | `debug`, `info`, `warn`, `error`. `/healthz` requests log at debug |
| `LMS_LOG_FORMAT` | `json` | `json` or `text` |
| `LMS_SHUTDOWN_TIMEOUT` | `15s` | how long in-flight requests get after SIGTERM. Must be positive |
| `LMS_INGEST_TOKEN` | *(none)* | ingest token of the `tournament` booth. Ignored when `LMS_BOOTHS` is set |
| `LMS_VIEW_TOKEN` | *(none)* | view token of the `tournament` booth. Ignored when `LMS_BOOTHS` is set |
| `LMS_OPERATOR_TOKEN` | *(none)* | operator token of the `tournament` booth. Ignored when `LMS_BOOTHS` is set |
| `LMS_BOOTHS` | *(none)* | comma-separated booth ids (`ada,grace`): lower-case letters, digits and dashes, at most 40 characters. Unset means the single `tournament` booth |
| `LMS_BOOTH_<ID>_INGEST_TOKEN` | *(none)* | ingest token of booth `<ID>` (upper case, dashes as underscores, e.g. `LMS_BOOTH_ADA_INGEST_TOKEN`). Required for every id in `LMS_BOOTHS` |
| `LMS_BOOTH_<ID>_VIEW_TOKEN` | *(none)* | view token of booth `<ID>` |
| `LMS_BOOTH_<ID>_OPERATOR_TOKEN` | *(none)* | operator token of booth `<ID>`. Unset makes the booth read-only |
| `LMS_PUBLIC_BRACKETS` | *(none)* | comma-separated booth ids whose `GET /bracket` needs no token (`tournament` for the default booth). Only the bracket is published. Unknown ids are ignored |
| `LMS_INGEST_RATE` | `20` | authenticated pushes per second to `/ingest` and `POST /console`, across all booths, with a 2 s burst. `0` disables |
| `LMS_MAX_LIVE_CLIENTS` | `64` | concurrent `/live` clients. `0` is unlimited |
| `LMS_MAX_BOOTHS` | `64` | booths held at once, configured and created. `0` is unlimited |
| `LMS_BOOTH_RATE` | `0.05` | booth creations per second per source address (one every 20 s), with a 2 s burst rounded up to one. `0` disables |
| `LMS_CLIENT_IP_HEADER` | *(none)* | header a trusted proxy writes the client address into (e.g. `X-Forwarded-For`); its rightmost entry is used. Only for the booth creation limit. Unset uses the connection's address |
| `LMS_FINAL_SEATS` | `5` | players seated in the final when the bracket has no final spec |
| `LMS_COMMAND_TTL` | `2m` | how long a queued command waits to be collected. `0` never expires |
| `LMS_DATABASE_URL` | *(none)* | full Postgres DSN. Takes precedence over the `DB_*` variables |
| `DB_HOST` | *(none)* | Postgres host. Unset (and no `LMS_DATABASE_URL`) means no database |
| `DB_PORT` | `5432` | Postgres port |
| `DB_NAME` | *(none)* | required when `DB_HOST` is set |
| `DB_USER` | *(none)* | required when `DB_HOST` is set |
| `DB_PASSWORD` | *(none)* | password, taken verbatim |
| `DB_SSLMODE` | `disable` | Postgres `sslmode` |

Two booths may not share a token; that is a startup error. Limits switched off with `0` are logged
as warnings at startup, as is every booth missing a token.

Used by the tools and tests rather than the service: `LMS_INGEST_TOKEN` and `LMS_VIEW_TOKEN`
(`cmd/replay`, `cmd/soak -url`), `SUBNAUTICA_INSTALLATION_PATH` (`cmd/worldmesh` and the world
tests), `LMS_TEST_DATABASE_URL` (the Postgres test suite).

## Postgres

The database is optional. Without one, the live path (ingest, feed, dashboard, overlays) works
unchanged; the bracket shows what the mod pushed, writes are refused with `503`, and booths cannot
be created. A database that is unreachable at startup is logged and treated the same way.

Migrations in `internal/tournament/migrations/` are embedded and applied at startup, each in its
own transaction, tracked in `schema_migrations`. Every row carries the booth that owns it, and
handlers only reach the database through a store scoped to one booth.

## Running locally

```bash
LMS_LOG_FORMAT=text LMS_INGEST_TOKEN=dev-ingest LMS_VIEW_TOKEN=dev-view LMS_OPERATOR_TOKEN=dev-op \
  go run ./cmd/caster
# http://localhost:8080/  (dashboard)    http://localhost:8080/home.html
```

Feed it without the game:

```bash
LMS_INGEST_TOKEN=dev-ingest go run ./cmd/replay ../docs/telemetry/example.json http://127.0.0.1:8080/ingest
```

To use a database, start one and set `LMS_DATABASE_URL`:

```bash
docker run --rm -e POSTGRES_PASSWORD=lms -p 5433:5432 -d postgres:17
export LMS_DATABASE_URL='postgres://postgres:lms@127.0.0.1:5433/postgres?sslmode=disable'
```

## Tools

```bash
# capture a live feed, one telemetry document per line
go run ./cmd/record "ws://127.0.0.1:8080/live?token=$LMS_VIEW_TOKEN" match.ndjson

# push it back; a file with one document is a still frame
LMS_INGEST_TOKEN=… go run ./cmd/replay [-interval 1s] [-loop] match.ndjson http://127.0.0.1:8080/ingest

# load test: a booth in this process, or a running one with -url (needs LMS_INGEST_TOKEN and LMS_VIEW_TOKEN)
go run ./cmd/soak -duration 10m -rate 1 -clients 12
go run ./cmd/soak -url https://example.org -duration 2h

# rebuild internal/web/static/map/worldmesh.{bin.gz,json} from a Subnautica install
go run ./cmd/worldmesh [-install <path>] [-cell 4] [-stats]
```

`cmd/soak` prints a line per `-report` interval and exits non-zero if the run was not clean. Its
default fixture is `../docs/telemetry/example.json`.

The flat map's `static/map/terrain.png` and the marker stalks' `static/map/heightmap.{png,json}`
are copies of `docs/data/minimap.png` and `docs/data/heightmap.{png,json}`, made by
`../scripts/make-minimap` and `../scripts/make-heightmap`. Like `cmd/worldmesh`, they need the game
installed.

## Tests

```bash
go vet ./... && go test ./...
node --test internal/web/tests/*.test.js
```

The Postgres suite in `internal/tournament` is skipped unless `LMS_TEST_DATABASE_URL` is set:

```bash
docker run --rm -e POSTGRES_PASSWORD=lms -p 5433:5432 -d postgres:17
LMS_TEST_DATABASE_URL='postgres://postgres:lms@127.0.0.1:5433/postgres?sslmode=disable' \
  go test ./internal/tournament/
```

`../scripts/check` runs both suites together with the .NET and Python tests.

## Docker

```bash
docker build --build-arg REVISION="$(git rev-parse HEAD)" -t lms-caster .
docker run --rm -p 8080:8080 -e LMS_INGEST_TOKEN=… -e LMS_VIEW_TOKEN=… -e LMS_OPERATOR_TOKEN=… lms-caster
```

The image is distroless and runs as non-root. It contains `/caster` (the entrypoint) and `/soak`.
There is no `HEALTHCHECK`; probe `GET /healthz`. If a `telemetry-example.json` is placed in this
directory before the build, it is copied to `/telemetry-example.json` for `/soak -fixture`.

`REVISION` is stamped into the binary so each page's source link names the exact commit. Only a hex
commit id of 7 to 64 characters is accepted; without one, the link points at the repository and the
revision reads "unknown".

## Licence

`backend/` is licensed **AGPL-3.0-or-later** ([`LICENSE`](LICENSE)), separately from the GPL-3.0
mod: it links no Nitrox code and talks to the game only over HTTP and WebSocket.

To meet AGPL §13, every page carries a link to its source at the running revision. The link is
inserted server-side where a page contains `<!-- source-offer -->`; on `overlay.html` it is on the
setup card, not on the panels that appear on camera. If you run a modified version, point
`SourceURL` in `internal/caster/source.go` at your own source.

Third-party components (three.js, coder/websocket, lib/pq) and their licences are listed in
[`NOTICE`](NOTICE). The map terrain is rendered from the game's own files by this repository's
tools.
