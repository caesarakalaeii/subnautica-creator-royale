# Nitrox fork

The mod runs on a patched **Nitrox 1.8.1.0**, not on stock Nitrox. The fork is kept here as a single
patch, [`patches/0001-creator-royale.patch`](patches/0001-creator-royale.patch), applied to the
upstream tag.

## Why a fork

Nitrox registers packet types by scanning its own assemblies, in index order. A custom packet
therefore has to live inside Nitrox's assemblies (`Nitrox.Model.Subnautica`, `NitroxClient`), and
the client and server must run the same build so both ends agree on the packet table. The patch
keeps that surface small: it adds the shared types and a few seams, and the tournament itself lives
in the mod (`src/`), which Nitrox loads by name at startup. Without the mod installed, the patched
build behaves like stock Nitrox.

## Reproducing it

```bash
./scripts/nitrox-fork
```

This clones Nitrox `1.8.1.0` into `./nitrox` (gitignored), creates a local branch from the tag,
applies `nitrox-fork/patches/*.patch` with `git am`, and builds `NitroxPatcher` (net472) against the
local Subnautica install. It is idempotent. `./scripts/install-mod` and `./scripts/check-game` build
the rest (server, launcher, both halves of the mod); see the [root README](../README.md).

To change the fork, commit on the branch in `./nitrox` and regenerate the patch with
`git format-patch` against `tags/1.8.1.0`, so `git am` can apply it.

## What the patch changes

**Loading the mod**

- `NitroxPatcher/LmsHook.cs`, called at the end of `Patcher.Initialize()`: loads `Lms.Game.dll` from
  the launcher's `lib/net472` (falling back to the patcher's own directory) and calls
  `Lms.Game.LmsMod.Initialize()`. Failures are logged and the game continues as stock Nitrox.
- `LmsTournament` on the server, registered in Autofac and started/stopped with `Server`: loads
  `Lms.Server.dll` from `lib/` beside the server executable (or beside it directly), calls `Lms.Server.LmsServerMod.Initialize`, and
  ticks the returned `ILmsTournamentHost` every 0.25 s. A tick that throws stops the tournament for
  the session. The host is told when each player finishes their initial sync.

**Shared packets and seams** (`Nitrox.Model.Subnautica`)

- Packets `LmsMatchState` (server to clients) and `LmsPlayerReport` (client to server), each one
  `key=value` string whose format the mod owns, with their processors and the static relays
  `LmsMatchStateRelay` and `LmsPlayerReportRelay`. The server stamps a report with the sender's
  player id and admin permission from the connection, never from the packet.
- Static seams the server fills in and the mod calls: `LmsRoster` / `LmsPlayerReading` (connected
  players with position and vitals), `LmsGameModes` / `ILmsGameMode` / `LmsPlayerMode` (read each
  player's game mode and force survival), `LmsSpawn`, `LmsPods`, `LmsCombat`. On the client:
  `LmsReporting` (send a report) and `LmsNetwork` (round-trip ping, via a new
  `LiteNetLibClient.Ping`).
- `LmsPreferences`, rendered by `NitroxPatcher/LmsSettings.cs` into Nitrox's options panel, so the
  mod can add settings and buttons without referencing `NitroxClient`.

**Server commands**

- `lms <verb> [modifier]` at `Perms.ADMIN`, passed to the tournament host (e.g. `lms podclear all`).
- `lmsphantom <n>` at `Perms.ADMIN`: adds escape pods for absent players, for testing pod placement
  on one machine.
- `pvp` refuses to toggle PvP while a tournament is loaded.

**Escape pods and spawning**

- `ILmsSpawnPlacement`: the mod decides how many players share a pod and where the next pod goes,
  choosing from Nitrox's own valid start points.
- `ILmsPodSeating`: list pods and start points, make a pod at a position, seat a player in a named
  pod, remove a pod. Backed by new `EscapePodManager` methods.
- `EscapePodManager` creates the first pod on demand rather than during world load, and registers
  pods with `WorldEntityManager`, so pods created after the world loaded are saved too.
- `RandomStartGenerator` seeds from an FNV-1a hash of the seed string instead of
  `string.GetHashCode()`, so start points are the same on every server start.
- Clients draw other players' pods as wrecked-lifepod prefabs (`LmsPodWrecks`, `LmsPodWreck`) and
  keep the real lifepod for the local player. `EscapePodEntitySpawner` no longer destroys the
  pod's `NitroxEntity` before re-tagging it, which left stale ids pointing at the player's pod.

**PvP damage**

- While a tournament is loaded, `PvPAttackProcessor` drops Nitrox's own knife PvP packets, so damage
  goes only through the mod's server-judged path.

**Per-player PDA progress**

- Scans, blueprints and encyclopedia entries are stored per player (`PlayerPDAProgress`, keyed by
  name) instead of per world, and are no longer relayed to other players. `PDAScanFinished` is
  still relayed so other clients remove the scanned fragment, but they no longer copy the progress.
  Fragment spawning no longer depends on world-wide scan state.

**Networking**

- `PacketFrame`, used by the LiteNetLib client and server: packets of 1,024 bytes or more are
  deflated when that makes them smaller (a negative length marks a deflated frame; inflated size is
  capped at 64 MiB). Smaller packets are framed as before. Covered by `PacketFrameTest`.
- `CellTiming`: optional client diagnostics for the cell request/spawn round trip. Off unless
  `NITROX_LMS_CELL_TIMING` is set or a file named `lms-cell-timing` exists beside `NitroxClient`
  or in the game directory.

**Smaller fixes**

- HUD pings whose name starts with `Lms` are skipped by the ping-preference sync.
- A returning player keeps their health through the skipped intro cinematic.

## Licence

GPL-3.0, the same licence as Nitrox. See [`../LICENSE`](../LICENSE).
