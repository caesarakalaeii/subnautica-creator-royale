# Telemetry contract

The game server pushes the live match to the casting booth (the Go service in
[`backend/`](../../backend/README.md)) as a JSON document. The booth stores it and forwards it to
the dashboard and OBS overlays over WebSocket.

Contract version: `v` = 6.

[`example.json`](example.json) is the canonical payload. `Lms.Core.Tests.Telemetry.GoldenPayloadTests`
checks that the C# encoder writes exactly those bytes, and `backend/internal/telemetry` checks that
Go parses the same file. Changing the format means changing `example.json`, both test suites and
`TelemetrySnapshot.Version` together.

## Rules for readers

1. `v` comes first. A reader that does not support the version must reject the document.
2. Optional values are `null`, never absent. Every field is written on every push. Positions
   are `null` rather than `0` when there is none, because `(0, 0, 0)` is a real place.
3. Numbers are truncated to millimetres, as in the audit log. `NaN` and infinities are sent as
   `null`.
4. Times are UTC ISO 8601 with milliseconds and `Z`. `at` is wall-clock time; `elapsedMs` and
   `untilNextMs` are match time, which stops while the match is paused.
5. Each push is a complete snapshot plus the events since the previous push. A reader that
   connects mid-match has the full state after one push. Events carry the audit log's sequence
   number, so gaps are detectable and duplicates can be dropped.

## Document

```json
{
  "v": 6,
  "at": "2026-10-24T12:34:56.789Z",
  "match": { … },
  "zone": { … },
  "standingsRevision": 2,
  "standings": { … },
  "players": [ … ],
  "events": [ … ],
  "pods": [ … ]
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `v` | int | Contract version. |
| `at` | time | When the snapshot was taken. |
| `match` | object or null | Match state. |
| `zone` | object or null | Current zone. |
| `standingsRevision` | int | Revision of the standings table. Always present. |
| `standings` | object or null | The tournament table, sent only when it changes and on a 30 s heartbeat. |
| `players` | array | One row per contestant. Ghosts are never listed. |
| `events` | array | Audit log records since the previous push. |
| `pods` | array | Every escape pod in the world. |

### `match`

| Field | Type | Meaning |
| --- | --- | --- |
| `group` | int | Which group's match. |
| `generation` | int | Which attempt at that group's match; `reset` starts a new one. |
| `state` | string | `NotStarted`, `Running` or `Paused`. A won match shows `Paused` during the victory window. |
| `elapsedMs` | int | Match time. |
| `phase` | int | Phases fired so far; 0 before the first. |
| `phases` | int | Total phases including the final collapse. |
| `untilNextMs` | int or null | Time until the next phase; `null` when nothing is due (lobby, pause, end). |
| `alive` | int | Players still in the match. |

### `zone`

| Field | Type | Meaning |
| --- | --- | --- |
| `now` | sphere | The current wall. |
| `next` | sphere or null | The sphere it is closing to; `null` during the final collapse. |
| `damagePerTick` | number | Health lost per `zone.damageInterval` outside the wall. |

A sphere is `{ "x", "y", "z", "r" }` in metres; `y` is height, negative underwater.

### `players[]`

| Field | Type | Meaning |
| --- | --- | --- |
| `name` | string | Player name. |
| `alive` | bool | Still in the match. Eliminated players keep their row with `false`. |
| `x`, `y`, `z` | number | Position in metres. |
| `depth` | number | Metres below the surface; negative above it. |
| `biome` | string or null | Biome name (see [docs/data](../data/README.md)). |
| `health` | number | Health, out of 100. |
| `oxygen` | number | Air remaining, in seconds. |
| `food`, `water` | number | The game's food and water values. |
| `vehicle` | string or null | Vehicle they are in. |
| `held` | string or null | Item in hand. |
| `rifle` | bool | Carrying a stasis rifle. |
| `rifleCharge` | number | Stasis rifle charge, 0 to 1. |
| `frozen` | bool | Held by someone else's stasis field. |
| `insideZone` | bool | Inside the current wall. |
| `distanceOutside` | number | Metres outside the wall; 0 inside. |
| `inventory` | array | `{ "item", "count" }` per item type. |
| `equipment` | array | `{ "slot", "item" }` per equipped item. |

Position and vitals come from the server's own roster. `biome`, `vehicle`, `held`, `inventory`,
`equipment`, `rifle`, `rifleCharge`, `frozen` and elimination are reported by each player's own
client, when they change.

### `events[]`

| Field | Type | Meaning |
| --- | --- | --- |
| `seq` | int | Audit log sequence number. Restarts at 1 when the game server restarts. |
| `group`, `generation` | int | The match the event belongs to (not inferred from the snapshot). |
| `at` | time | Wall-clock time. |
| `elapsedMs` | int | Match time. |
| `kind` | string | Audit record kind, for example `PhaseStarted`, `ZoneBite`, `PlayerHurt`, `PlayerEliminated`, `RingFired`, `PlayerCaged`, `MatchEnded`. |
| `player` | string or null | The player the event is about. |
| `actor` | string or null | The other player: attacker, ring owner or credited killer. |
| `phase` | int or null | Phase at the time. |
| `amount` | number or null | Damage or other quantity. |
| `detail` | string or null | Kind-specific detail, such as the cause of an elimination. |
| `x`, `y`, `z` | number or null | Where it happened. |
| `biome` | string or null | Biome where it happened. |

`PlayerPosition` samples from the audit log are never sent; the player rows already carry live
positions.

### `standings`

```json
"standingsRevision": 2,
"standings": {
  "matches": 2,
  "pointsPerKill": 2,
  "entries": [
    {"place": 1, "name": "Vela", "points": 9, "played": 2, "wins": 1, "best": 1, "kills": 2, "outlasted": 5}
  ],
  "games": [
    {"group": 1, "generation": 1, "entries": [
      {"name": "Vela", "points": 5, "placement": 1, "kills": 1, "outlasted": 3}]}
  ]
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `matches` | int | Finished matches the table was built from. |
| `pointsPerKill` | int | Points per kill; an outlasted player is worth one point. |
| `entries` | array | One row per player, already in table order. |
| `entries[].place` | int | Place; tied players share it. |
| `entries[].points` | int | `kills × pointsPerKill + outlasted`. |
| `entries[].played` | int | Matches this player was in. |
| `entries[].wins` | int | First places, shared ones included. |
| `entries[].best` | int | Best single placement. |
| `entries[].kills`, `entries[].outlasted` | int | Components of `points`. |
| `games` | array | One result per finished group (latest attempt only), each with its own scored `entries` (`name`, `points`, `placement`, `kills`, `outlasted`). A player's `points` across `games` sum to their row. |

Rows are ordered by points, then wins, then best placement, then name. Do not re-sort or
renumber them. The table is built on the game server from the audit log.

How to read `standingsRevision` with `standings`:

| Revision | `standings` | Meaning |
| --- | --- | --- |
| same as held | `null` | Unchanged; keep the current table. |
| new | object | Replace the table. |
| new | `null` | There is no table any more (for example, the only finished group was reset). |
| same as held | object | Heartbeat or resend; take it. |

### `pods[]`

| Field | Type | Meaning |
| --- | --- | --- |
| `x`, `y`, `z` | number | Pod position. |
| `assigned` | int | Players assigned to it, connected or not. 0 means an unused pod. |

## Pushing: `POST /ingest`

The game server pushes every `telemetry.interval` (see the [config reference](../config/README.md#telemetry))
with `Authorization: Bearer <token>`, where the token is `LMS_INGEST_TOKEN` in the game server's
environment and the booth's ingest token (also `LMS_INGEST_TOKEN` for a single booth; see
[backend/README.md](../../backend/README.md) for multi-booth setups). Bodies are limited to 1 MiB.

| Status | Meaning |
| --- | --- |
| `202` | Accepted. |
| `400` | Not parseable, or an unsupported contract version. |
| `401` | Missing or wrong token. |
| `413` | Body over 1 MiB. |
| `503` | The booth has no ingest token configured. |

Pushes are idempotent: the booth keeps only events with a sequence number it has not seen. An event
sequence that starts again at 1 is treated as a restarted game server.

The reply is the booth's own format, not part of the contract:

```json
{"accepted": true, "newEvents": 3, "lastSequence": 43, "commands": []}
```

| Field | Meaning |
| --- | --- |
| `accepted` | Whether the push was stored. |
| `newEvents` | Events that were new to the booth; always present. |
| `lastSequence` | Highest sequence number the booth holds. |
| `commands` | `/lms` commands queued in the booth, `[{ "id", "verb" }]`, oldest first. Always an array. |
| `rules` | Present only when an operator has saved a config the server is not running: `{ "revision", "yaml", "by" }`. Only the newest revision is offered. |
| `bracket` | Present only when the booth has a bracket: `{ "groups": [{ "number", "name", "members" }] }`. The mod uses the current group's members when preparing a round. |

The mod reports progress in request headers:

- `X-Lms-Commands-Ran: <id>`: the highest command id it has run. A push without this header is
  offered no commands (older builds, or `telemetry.acceptsCommands: false`). The booth resends
  commands until acknowledged and drops them after `LMS_COMMAND_TTL` (default 2 minutes); the mod
  ignores ids at or below its watermark, which is per server run.
- `X-Lms-Rules-Revision: <revision>`: the config revision the server is running.

Commands run with the same checks as commands typed in game. Configs are validated by the mod, not
the booth.

## Console report: `POST /console`

A separate document (its own version, `v` = 1), sent with the ingest token when it changes and at
least every 30 seconds.

```json
{
  "v": 1,
  "at": "2026-10-24T12:00:00.000Z",
  "acceptsCommands": true,
  "ran": 7,
  "rulesRevision": 0,
  "config": "match:\n  cycleLength: 5m\n…",
  "connected": [{"name": "Ada", "inMatch": true}, {"name": "Caster", "inMatch": false}],
  "verbs": [{"verb": "podclear all", "takesSubject": false, "mustBeTyped": true}],
  "answers": [{"id": 7, "verb": "round force", "answer": "group 2 is prepared: Ada, Bea, Cy, Dov."}],
  "rules": [{"revision": 7, "adopted": false, "final": false,
             "message": "written to this server's own config file. A match is running, so it will be taken up at the next lobby.",
             "problems": []}]
}
```

| Field | Meaning |
| --- | --- |
| `acceptsCommands` | `telemetry.acceptsCommands` on this server. |
| `ran` | Highest command id run. |
| `rulesRevision` | Config revision running; 0 is the server's own file. |
| `config` | The running config, verbatim. |
| `connected` | Everyone connected, with whether they are in the match. The only place ghosts appear. |
| `verbs` | The available `/lms` verbs, whether each takes a name and whether it must be typed. |
| `answers` | The reply to each booth command, verbatim. |
| `rules` | Outcome per pushed config revision. `final: false` means written and waiting for the lobby; `problems` are `{ "problem" }` entries with the loader's field paths. |

## Reading: `GET /live`

A WebSocket protected by the booth's view token (`LMS_VIEW_TOKEN` for a single booth), a separate
secret from the push token. Without a view token the feed is off.

```
ws://<host>/live?token=<view token>&after=<last seq seen>
```

The token goes in the query because browsers cannot set headers on a WebSocket handshake. The first
message is the latest snapshot with the events after `after`; every later message is a push
forwarded unchanged. A client that falls too far behind is disconnected and should reconnect with
`after` set to the last sequence number it saw.

## Recording and replaying

```bash
cd backend
go run ./cmd/record "ws://127.0.0.1:8080/live?token=$LMS_VIEW_TOKEN" match.ndjson
LMS_INGEST_TOKEN=… go run ./cmd/replay match.ndjson http://127.0.0.1:8080/ingest
```

`record` appends one document per line. `replay` pushes a recording (or `example.json`) back to a
booth, so the front end can be developed without the game.

## Trust

Clients report their own rows, and a modified client can report false values. Telemetry is for
broadcast; the audit log on the game server is the record.
