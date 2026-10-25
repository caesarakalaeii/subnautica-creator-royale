# Subnautica Creator Royale — player guide

A last-man-standing tournament mod for Subnautica, on a patched build of
[Nitrox](https://github.com/SubnauticaNitrox/Nitrox) multiplayer. Windows, self-contained: unzip
and run.

## Requirements

- **Subnautica on Steam**, launched at least once. The Epic version is not supported.
- About 1.5 GB of disk space. Nothing else to install; the .NET runtime is included.
- Your single-player saves are not touched. Multiplayer worlds live in `%APPDATA%\Nitrox\saves`.

Windows warns that the launcher is unsigned: **More info → Run anyway**.

## Install

1. Unzip somewhere permanent, e.g. `C:\CreatorRoyale\`. Not inside the Subnautica folder and not
   in a OneDrive-synced folder.
2. Run **`Nitrox.Launcher.exe`**. The mod is already inside (`lib\net472\`); there is nothing to
   copy. The launcher finds Subnautica through Steam.
3. Start the game from the launcher, not from Steam.

## Hosting and joining

One person hosts: in the launcher, create a server, start it, then join it from the play screen.
Everybody else joins the host's IP. **UDP port 11000** must be reachable on the host; forward it on
the router rather than relying on UPnP.

### Hosting from Linux

`server-linux-x64/` is a complete Linux server with the mod in it:

```bash
chmod +x Nitrox.Server.Subnautica
SUBNAUTICA_INSTALLATION_PATH=/path/to/Subnautica ./Nitrox.Server.Subnautica --save MyWorld
```

It needs the game files but nobody has to play on that machine. Type `lms` commands into its
console without the slash.

## Running a match

Commands go into the Nitrox chat. Log in as admin first with `/login <password>`; the password is
`AdminPassword` in `%APPDATA%\Nitrox\saves\<save>\server.cfg`.

| Command | What it does |
| --- | --- |
| `/lms round` | prepare and start a round: lay out the pods, seat the group, reset inventories and vitals, start when the hold ends |
| `/lms round force` | the same for a group that is not `spawn.groupSize` (4 as shipped) |
| `/lms status` | where the match is and where the wall is |
| `/lms skip` | bring the next phase forward |
| `/lms pause`, `/lms resume` | stop and restart the clock |
| `/lms reset` | play the same group again from the lobby |
| `/lms next` | move on to the next group |
| `/lms podclear all` | remove every escape pod |
| `/lms ghost <player>`, `/lms unghost <player>` | make someone an invisible, flying caster, or undo it |
| `/lms follow <player>`, `/lms unfollow` | point every caster camera at one player |

`/lms round` refuses rather than half-runs: outside the lobby, or with the wrong group size, it
names the problem and changes nothing. A second `/lms round` while one is armed is refused too,
`force` included: seating everybody twice freezes their air, food and water. `/lms reset` first to
seat them again. The same commands are buttons in Options → Nitrox, under
the tournament's match-control heading.

## How a match plays

- A sphere closes on a destination every phase; outside it you take damage every two seconds,
  harder each phase. Depth counts as much as distance. Watch the minimap and the bearing line.
- A stasis rifle field holds anyone inside it until it expires.
- Prawn suit arms, the survival knife and the thermoblade damage other players; the server judges
  every hit.
- When you die you watch the rest of the match from a free camera; your body stays where it fell.
- The last player standing wins, and the wall stops.
- Leaving does not help: time outside the wall while disconnected is charged when you return.

## Configuration

The live config is `lms-tournament.yaml`: one beside the launcher (server) and one in
`lib\net472\` (client). `config\` holds the shipped event rules and a fully commented example.
Copy one over both and restart. A mistake in the file is reported by field name in the log. Every
field is documented in [docs/config/README.md](https://github.com/caesarakalaeii/subnautica-creator-royale/blob/main/docs/config/README.md).

To feed a caster dashboard, create a booth at `https://sn.caes.ar/home.html`, paste its
`telemetry:` block into the server's config, and start the launcher with the ingest token in its
environment. In PowerShell, from the unzipped folder:

```powershell
$env:LMS_INGEST_TOKEN = "<ingest token>"; .\Nitrox.Launcher.exe
```

## Troubleshooting

The log is the newest file in `%APPDATA%\Nitrox\logs\`. Near the top you should see
`[LMS] Last Man Standing … loaded`, and once in the world, `[LMS] following the server's match`.
`running as stock Nitrox` means the mod did not load. The host's match log is
`lms-audit\lms-audit-*.tsv` beside the launcher.

Report problems at
[github.com/caesarakalaeii/subnautica-creator-royale/issues](https://github.com/caesarakalaeii/subnautica-creator-royale/issues)
with the log attached.

## Licence

GPL-3.0. The source for this exact build is linked in `BUILD.txt`. Subnautica is not included;
bring your own copy.
