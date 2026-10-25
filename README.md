# Subnautica Creator Royale

A last-man-standing tournament mod for **Subnautica** on
[Nitrox](https://github.com/SubnauticaNitrox/Nitrox) multiplayer, and the live caster website that
goes with it. Built for the Subnautica Creator Royale, a 25-creator event played on 24–25 October 2026.

## What it does

**In the game**

- **Shrinking zone.** A visible sphere closes on a destination every phase and damages anyone
  outside it on a tick that grows harder each phase, until it closes completely. Depth counts as
  much as distance. Players see the wall, a ghost of the next sphere, a bearing to the centre and a
  minimap. Destinations can be drawn from a pool so every group plays a different endgame.
- **Disconnects don't help.** A player who leaves is charged for the time their last position was
  outside the wall when they return, a round is not decided while someone is inside their
  reconnect grace, and a player who died stays out when they rejoin.
- **Rounds in one command.** `lms round` seats the group on a circle of escape pods, clears
  inventories to the starting kit, resets vitals, grants every blueprint except a locked list,
  puts everyone back into survival mode and starts the match when the opening hold ends.
- **Stasis ring.** A stasis field holds anyone who swims into it until it expires, so fights can be
  forced. A held player is pulled out of vehicles and bases.
- **Server-judged PvP.** Prawn suit claw and drill, survival knife and thermoblade hits are reported
  by the attacker, judged by the server against its own positions and applied by the victim's
  client. Damage values are config. Refused hits are shown to the attacker.
- **No beacons or player markers** for contestants, enforced by the server.
- **First aid kit cooldown** (optional).
- **Ghosts** for casters: invisible to contestants, immune to the match, flying the free camera with
  name plates for every contestant through terrain, and `lms follow` to put every caster camera on
  one player.
- **Audit log.** One tab-separated line per event in `lms-audit/`; standings are folded from it, and
  a restarted server picks the tournament up where it left off.

**On the website** (`backend/`)

- A live caster **dashboard**: match clock, both zone spheres, a row per player with vitals, depth,
  biome, vehicle, equipment and inventory, an event timeline, and a flat or full 3D map of the
  world (an isosurface of the game's own terrain, caves included) with a depth cut.
- **OBS overlays**: status strip, elimination cards, match board, tournament table and a map shot
  that a caster can drive remotely.
- **Bracket** and public **tournament** pages, an operator **console** that edits the live config
  and sends commands, and self-service **booths** so anyone running their own server can get a
  dashboard for a day.

## Repository layout

```
src/Lms.Core/         tournament logic, netstandard2.0, no game or Unity types
src/Lms.Core.Tests/   MSTest tests for Lms.Core
src/Lms.Game/         client half: Harmony patches into Subnautica (net472)
src/Lms.Server/       server half, loaded by the Nitrox server (net9.0)
nitrox-fork/          the patch applied to Nitrox 1.8.1.0
backend/              Go caster service with its embedded web front-end
docs/config/          tournament config reference and example configs
docs/telemetry/       the game-to-website telemetry contract
docs/data/            biome tables, minimap and heightmap derived from the game
scripts/              build, install and map tools
```

All decisions live in `Lms.Core`, where they are unit-testable without the game; `Lms.Game` and
`Lms.Server` only connect it to Subnautica and Nitrox.

## Building

Requirements: .NET 10 SDK, Go 1.26, Node 24, Python 3 with PyYAML. `nix develop` provides all of
them.

```bash
./scripts/check          # every test that runs without the game: dotnet, python, go, node
```

The game side needs a Subnautica install (found in the default Steam location, or set
`SUBNAUTICA_INSTALLATION_PATH`, or copy `local.props.example` to `local.props`):

```bash
./scripts/nitrox-fork    # clone Nitrox 1.8.1.0 into ./nitrox, apply the patch, build it
./scripts/install-mod    # build the launcher and install both halves of the mod with a config
./scripts/check-game     # build the fork and the mod and check both are deployed
./scripts/run-launcher   # start the patched Nitrox launcher
```

Start the game from the Nitrox launcher. `Lms.Game` and `Lms.Server` are not part of `Lms.slnx` or
CI because they compile against the game's assemblies and the patched Nitrox build.

Map data tools (need the game install): `./scripts/make-minimap`, `./scripts/make-heightmap`,
`cd backend && go run ./cmd/worldmesh`, and `./scripts/check-zone` to check that a config's zone
and pod circle are playable on the real terrain.

## Running a tournament

The rules live in `lms-tournament.yaml`, installed beside both the Nitrox server and the client;
see [docs/config/README.md](docs/config/README.md) for every field. The server owns the zone and
the match clock and tells every client where the wall is.

The match is driven from the Nitrox chat by an admin (log in with `/login` first):

```
/lms status | start | pause | resume | skip | reset | next
/lms round [force]              prepare and start a round for the connected group
/lms podspawn | podclear [all]  lay the pods out again / remove stray pods
/lms ghost <player> | unghost <player>
/lms look <player> | follow <player> | unfollow
```

To feed the website, set `telemetry.url` in the config and `LMS_INGEST_TOKEN` in the server's
environment. See [backend/README.md](backend/README.md) for running the caster service.

## Licence

- `src/`: **GPL-3.0-or-later** ([LICENSE](LICENSE)). The Nitrox patch is under Nitrox's own
  licence, GPL-3.0.
- `backend/`: **AGPL-3.0-or-later** ([backend/LICENSE](backend/LICENSE)). It shares no code with
  Nitrox and talks to the mod over HTTP and WebSocket. Every page it serves links to its source.
  Third-party components are listed in [backend/NOTICE](backend/NOTICE).

Subnautica is a trademark of Unknown Worlds Entertainment. This project is not affiliated with
Unknown Worlds or the Nitrox team.
