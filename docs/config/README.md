# Tournament config

`lms-tournament.yaml` holds the rules a match runs on: the match cadence, the shrinking zone, combat,
spawning, the casting-booth feed and how a round starts. The live match is driven separately with the
[`/lms` chat commands](#chat-commands).

Three configs ship in this directory:

| File | Purpose |
| --- | --- |
| `tournament.example.yaml` | Reference for authoring a tournament. Every section, damage off. |
| `tournament.playtest.yaml` | A full-length round: 10-minute opening, a phase every 6:40, damage and PvP on. |
| `tournament.test.yaml` | One-minute cadence for watching the mechanics locally. Also a test fixture: the C# tests assert its values. |

## Where it goes

The server and every client each read their own copy, named `lms-tournament.yaml`:

| Copy | Location | Read by |
| --- | --- | --- |
| Server | beside the server executable (the Nitrox launcher's `net9.0/`) | the game server |
| Client | beside the mod's assemblies (the launcher's `lib/net472/`) | every client |
| Standalone server build | `Nitrox.Server.Subnautica/bin/Release/net9.0/lib/` | a server started from its own build output |

`./scripts/install-mod` copies a config into all three when none exists
(`--config example|playtest|test`, default `test`); `--refresh-config` overwrites existing copies.

Put the same file everywhere. Each side uses only the sections it needs (see the
[section table](#sections)), and only the server's copy decides where the zone is.

- No config on the server: no tournament runs; `/lms` says so.
- No config on a client: the client only shows its biome readout. No wall is drawn, nobody is
  damaged and stasis rings hold nobody.
- Invalid config on either side: treated as missing. The log lists every problem with the field
  path that caused it, for example `match.cycleLength` or `zone.phases[1].radius`.

### Configs pushed from the booth

The casting booth can send a new config. The server validates it with the same loader, writes it
over its own copy (keeping one `.bak`), and adopts it only in the lobby; a config sent mid-match
waits for the next lobby. Once adopted, the server sends the document to every connected client and
to clients that join later, so everybody runs the server's rules. Two exceptions: `ghosts` is not
re-read (the booth manages ghosts itself), and switching `flags.enforceContainment` off does not
unpatch a rifle on a client that already patched it; that takes a relaunch.

## Value formats

| Kind | Format |
| --- | --- |
| Duration | `800ms`, `90s`, `5m`, `1h` or `00:05:00`. Decimals use a dot. Negative durations are refused. |
| Number | Plain YAML number, dot as decimal separator. |
| Point | `[x, y, z]` in metres. `y` is height, so underwater is negative. |
| Colour | `"#RRGGBB"` or `"#RRGGBBAA"`, quoted (unquoted, YAML treats `#` as a comment). The `#` is optional. |
| Names | Game names as the game spells them: `TechType` names for items and blueprints, player names as the client reports them. |

Unknown keys are errors, in every section.

## Sections

| Section | Required | Read by | Controls |
| --- | --- | --- | --- |
| [`match`](#match) | yes | server | phase cadence and the end of a match |
| [`zone`](#zone) | yes | both | the shrinking sphere, its damage and colours |
| [`ring`](#ring) | no | client | the stasis rifle's field |
| [`prawn`](#prawn) | no | server | prawn suit arms against players |
| [`blade`](#blade) | no | server | knife and heat blade against players |
| [`spectator`](#spectator) | no | client | what an eliminated player sees |
| [`caster`](#caster) | no | client | nameplates and the follow camera for ghosts |
| [`spawn`](#spawn) | no | server | escape pod placement |
| [`telemetry`](#telemetry) | no | server | the push to the casting booth |
| [`audit`](#audit) | no | server | position sampling in the audit log |
| [`disconnect`](#disconnect) | no | server | what leaving mid-match costs |
| [`flags`](#flags) | no | both | enforcement switches |
| [`ghosts`](#ghosts) | no | server | players who are present but not in the match |
| [`start`](#start) | no | server | how `lms round` starts a round |
| [`medkit`](#medkit) | no | client | first aid kit cooldown |

## `match`

```yaml
match:
  cycleLength: 5m
  warningLead: 1m
  openingLength: 10m
  victoryWindow: 10s
```

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `cycleLength` | duration | required | Length of one cycle. One phase fires per cycle. Above zero. |
| `warningLead` | duration | required | How long before each phase the warning starts. Shorter than both `cycleLength` and `openingLength`. |
| `openingLength` | duration | `cycleLength` | Length of the first window, before phase 1 fires. Above zero. |
| `victoryWindow` | duration | `10s` | How long the wall stays stopped after a win before `MatchEnded` is recorded. `0s` records it immediately. |

A match is won when one player (or none) is left standing, provided at least two were standing at
the same time; a solo match is never won and runs to the final collapse. The wall stops at the win.
A won match refuses `start`, `pause`, `resume` and `skip`; `reset` and `next` still work. A match
the wall finished logs `ZoneClosed` before `MatchEnded`; a won match logs only `MatchEnded`.

## `zone`

```yaml
zone:
  damageInterval: 2s
  fullExposureAt: 150
  colours:
    now: "#FFD65C"
    next: "#7CFF5C1A"
  opening:
    centre: [100, -20, -350]
    radius: 1800
    damagePerTick: 1
  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
  destinationSeed: 4071
  phases:
    - radius: 1200
      shrinkFor: 5m
      damagePerTick: 3
```

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `damageInterval` | duration | `2s` | How often a player outside the wall takes `damagePerTick`. Above zero. |
| `fullExposureAt` | metres | `150` | Distance outside the wall at which the screen effect is at full strength. Visual only. |
| `colours` | mapping | see below | Colours of the wall and of the next sphere. |
| `opening` | mapping | required | The sphere the match starts inside. |
| `phases` | list | required | The spheres the zone closes to, widest first, one per cycle. |
| `destinations` | list | none | Pool of places a match may finish. When present, phase centres are derived and phases must not have a `centre`. Must not be empty. |
| `destinationSeed` | integer | `0` | Seed for drawing from `destinations`. Any whole number. |
| `destinationOrder` | `drawn` or `listed` | `drawn` | `drawn` shuffles the pool with the seed; `listed` plays it in file order. Needs `destinations`; `listed` refuses a `destinationSeed`. |

The zone is a true sphere: depth counts as much as horizontal distance. A sphere centred near the
surface is half sky, so compare arenas by the water they hold, which `./scripts/check-zone` reports.

### `zone.opening`

| Field | Type | Meaning |
| --- | --- | --- |
| `centre` | point | Centre of the starting sphere. |
| `radius` | metres | Radius. Above zero. |
| `damagePerTick` | health | Damage per tick outside the opening sphere. Above zero. |

### `zone.phases[]`

| Field | Type | Meaning |
| --- | --- | --- |
| `centre` | point | Where this sphere closes to. Required without `destinations`, refused with them. |
| `radius` | metres | Above zero. |
| `shrinkFor` | duration | How long the wall takes to move from the previous sphere to this one. |
| `damagePerTick` | health | Damage per tick outside the wall while this phase is live. Above zero. |

The loader checks each phase against the one before it (the opening for phase 1) and names the
phase that fails:

- the radius is smaller;
- the sphere lies entirely inside the previous one;
- `shrinkFor` is no longer than `match.cycleLength`;
- `damagePerTick` is higher.

After the last phase, one more cycle shrinks the final sphere to nothing (the final collapse),
taking the last phase's `shrinkFor` and biting at its `damagePerTick`. A match therefore lasts the
opening plus one cycle per phase plus the collapse. `/lms status` shows `phase 2/2` and then
`final collapse`.

The loader cannot tell whether a sphere holds water or whether players can outswim the wall; run
[`./scripts/check-zone`](#checking-a-squeeze) for that.

### `zone.destinations[]`

| Field | Type | Meaning |
| --- | --- | --- |
| `name` | text | Name shown to casters and recorded in the `ZoneDestinationDrawn` audit record. Shown to contestants only with `flags.showDestination`. |
| `centre` | point | Where the final phase lands. |

One destination is drawn per group. Each phase moves its centre from the opening centre toward the
destination in proportion to how much it shrinks, so every sphere stays inside the previous one.
A destination farther from the opening centre than `opening.radius` minus the last phase's radius is
refused at load, with the overhang in metres.

With `drawn`, the draw is keyed on the group number and the seed: no place repeats until the pool is
used up, `reset` replays the same place, and `next` moves on. Every match's audit log records the
place and the seed. With `listed`, group N plays entry N and groups past the end replay the last
entry; the audit log records a seed of 0.

### `zone.colours`

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `now` | colour | `#FFD65C` | The current wall. |
| `next` | colour | `#7CFF5C1A` | The sphere the wall is closing to. |

Each falls back on its own. The minimap rings use the same colours but are always drawn opaque.
The wall keeps the game material's brightness and takes the hue from the colour; alpha controls
how solid it looks.

## `ring`

The stasis rifle's field holds anyone who swims in until it expires. These values are written onto
the game's own field, so the visible bubble is the one that holds players. Omit the section for the
game's own values.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `minRadius` | metres | `1` | Radius of a tapped shot. Above zero. |
| `maxRadius` | metres | `10` | Radius of a full charge. At least `minRadius`. |
| `minLifetime` | duration | `4s` | Lifetime of a tapped shot. Above zero. |
| `maxLifetime` | duration | `20s` | Lifetime of a full charge. At least `minLifetime`. |
| `placementMargin` | metres | `1` | How far inside the ring a player is placed when it has to move them. At least zero, less than `maxRadius`. |
| `energyCost` | number | `5` | Battery cost of a full-charge ring (a battery holds 100). A partial charge costs and yields a share. Above zero. |

Radius and lifetime scale together with the charge. A player cannot fire again while their own ring
is still standing; a refused shot spends no energy. Creative saves spend no energy at all, and the
mod logs a warning when it loads one.

## `prawn`

Prawn suit claw and drill damage against players. The attacker's client reports the swing; the
server checks it against these values and tells the victim's client what to take. Off unless
`hurtsPlayers` is true; each field falls back on its own.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `hurtsPlayers` | bool | `false` | The switch. |
| `clawDamage` | health | `20` | Damage per claw hit (players have 100; the game's claw does 50). `0` disables the claw. |
| `drillDamage` | health | `0` | Damage per drill tick. `0` disables the drill. |
| `reach` | metres | `9` | Farthest apart a hit is believed. Must be above 6.5, the game's claw reach. |
| `minInterval` | duration | `800ms` | Shortest time between two believed hits from the same prawn. Above zero. |

Every refused swing is reported on the attacker's screen (at most once a second) with the reason:
switched off, arm disabled, no match running, attacker held by a ring, victim already out, too far,
too soon, or not in a prawn suit. Every landed hit writes a `PlayerHurt` audit record naming the
victim (`player`), the attacker (`actor`), the damage (`amount`) and the remaining `health`.
Attacks on vehicles are refused.

## `blade`

Knife and heat blade damage against players, judged the same way as the prawn's arms. Off unless
`hurtsPlayers` is true. While a tournament is loaded, Nitrox's own PvP damage path is blocked on the
server and ignored by the mod's clients, so blade damage comes only from here.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `hurtsPlayers` | bool | `false` | The switch. |
| `knifeDamage` | health | `12` | Damage per knife hit. `0` disables the knife. |
| `heatBladeDamage` | health | `20` | Damage per heat blade hit, independent of the knife. `0` disables it. |
| `reach` | metres | `6` | Farthest apart a hit is believed. Must be above 4.8: the game's 2 m reach plus the gap between a swimmer's origin and their collider. |
| `minInterval` | duration | `250ms` | Shortest time between two believed hits from the same player. The game allows one swing per 0.5 s; the margin absorbs network jitter. Above zero. |
| `resistances` | mapping | `ReinforcedDiveSuit: 0.5` | Damage multiplier per piece of the victim's equipment, keyed by game item name, each in (0, 1]. Matching pieces multiply. Applied after all refusal checks. Not applied to prawn damage. |

## `spectator`

What happens to an eliminated player. The body stays where it died, which is what other clients see.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `deathCam` | duration | `5s` | How long the game's death view runs before the free camera takes over. |
| `spectate` | bool | `true` | `true`: fly a free camera after the death view. `false`: the game's own death and respawn run unchanged. |
| `flySpeed` | metres/second | `8` | Free camera speed. Keys 1 to 5 still change it in game. Above zero. |

A spectator stops broadcasting their position, cannot be hit or caged, and is not damaged by the zone.

## `caster`

What ghosts see (see [`ghosts`](#ghosts)). Contestants never receive this data.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `nameplates` | bool | `true` | Draw every contestant's name, health bar, air (seconds) and distance through terrain. Does not affect `lms follow`. |
| `followDistance` | metres | `12` | Distance the camera cuts to on `lms follow`. Above zero. |
| `followHeight` | metres | `4` | Height above the target the camera cuts to. May be negative. |
| `followClosest` | metres | `3` | Nearest the camera may be pushed afterwards. Above zero. |
| `followFurthest` | metres | `40` | Farthest the camera may drift before it is pulled back. Greater than `followClosest`. |

The cut keeps the caster's current bearing. Afterwards the camera moves with the target and the
caster can orbit and dolly freely between `followClosest` and `followFurthest`. Positions are up to
one server tick (0.25 s) old, and plates and the follow camera are interpolated a tick behind.

## `spawn`

Escape pod placement. Omit the section for stock Nitrox, which puts every player in one shared pod.
Placement applies to players joining the world for the first time; `/lms podspawn` and `/lms round`
re-seat players who already have a pod.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `ownPod` | bool | `false` | One pod per player. Each Nitrox pod has its own fabricator, medical cabinet, radio and locker. |
| `minSeparation` | metres | `0` | Wanted distance between pods. A target, never a veto. At least zero. |
| `groupSize` | number | `5` | Pods the layout is planned for, and the head count `lms round` expects. Caps nobody. At least 1. |
| `circleRadius` | metres | `0` | Above zero, puts the pods on a circle of this radius around the world origin instead of the game's start points. At least zero. |

With `circleRadius: 0` (scatter), pods go on start points the game approves of, inside
`zone.opening`. The whole group's layout is planned on the first join and handed out best spot
first, so the same world and arena always plan the same way. The server logs the closest
separation it achieved and warns when it falls short of `minSeparation`.

With `circleRadius` above zero (circle), `groupSize` pods sit exactly `2·r·sin(π/groupSize)` apart
on the sea surface, the first due north of the origin and the rest clockwise. The circle ignores
terrain, so a bad radius can put a pod in rock or on land; run `./scripts/check-zone` after changing
it. Players beyond `groupSize` get an approved start point instead.

## `telemetry`

Pushes the match to the casting booth (see [the telemetry contract](../telemetry/README.md)). Omit
the section, or `url`, to push nowhere.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `url` | text | none | The booth's ingest endpoint, `http://` or `https://`, in full. Checked at load. |
| `interval` | duration | `1s` | How often the match is pushed. Effective resolution is the server tick, 0.25 s. Above zero. |
| `timeout` | duration | `5s` | How long one push may take before it counts as failed. Above zero. |
| `acceptsCommands` | bool | `true` | Whether the booth may run `/lms` commands on this server. |

The push token is not part of the config: set `LMS_INGEST_TOKEN` in the server's environment. With
a `url` and no token the server logs it once and pushes nothing. A failed push is harmless: the next
push carries the whole match, events are resent until accepted, and the pump backs off to 30 s
while the booth is unreachable. Commands from the booth go through the same checks as typed ones.

## `audit`

The server writes one audit log per run to `lms-audit/lms-audit-<yyyyMMdd-HHmmss>.tsv` beside the
server executable. Each line is tab-separated: sequence, UTC time, match time in milliseconds,
group, generation, kind, then `key=value` fields. Keep these files; standings and disputes are
settled from them.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `positionInterval` | duration | `5s` | How often each living player's position is recorded, in match time. `0s` turns sampling off. |

Position samples are never sent to the booth; every push already carries live positions.

## `disconnect`

A player who disconnects while outside the wall is charged, when they rejoin, the damage the wall
would have done during their absence beyond the grace period. A player who was inside the wall owes
nothing. The charge is logged as `ZoneArrears`, not `ZoneBite`.

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `grace` | duration | `90s` | Forgiven absence, in match time. `0s` forgives nothing; a grace longer than a match turns the rule off. |

The charge only takes health when `flags.enforceDamage` is true; otherwise the server logs what it
would have charged.

## `flags`

| Field | Type | Default | Read by | Meaning |
| --- | --- | --- | --- | --- |
| `enforceDamage` | bool | `false` | both | Whether zone damage and disconnect charges take health. Off, the amounts are logged only. PvP damage is switched by `prawn.hurtsPlayers` and `blade.hurtsPlayers`. |
| `enforceContainment` | bool | `true` | client | Whether a stasis ring holds players. Off, the rifle behaves as in the base game. |
| `hideBeacons` | bool | `true` | client | Hide beacons and other players' location markers from contestants. Ghosts still see them. |
| `showDestination` | bool | `false` | server | Tell contestants the name of the drawn destination. Off, the name is never sent to them. |

## `ghosts`

```yaml
ghosts:
  - YourCasterName
```

A list of player names (matched case-insensitively) who are present but not in the match: no pod,
no zone damage, no drowning, no ring holds them, and they are absent from the roster, standings and
alive count. They stay in survival mode. Only the server's copy counts. Use `/lms ghost <player>`
and `/lms unghost <player>` for changes at runtime. Blank entries are errors.

## `start`

What `lms round` does when it starts a round.

```yaml
start:
  hold: 30s
  fieldRadius: 12
  kit: []
  lockedRecipes: [Seamoth, Exosuit, Cyclops]
  recipes: [all]
```

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `hold` | duration | `30s` | Delay between `lms round` and the match start. `0s` starts immediately. |
| `fieldRadius` | metres | `12` | Radius of the stasis field meant to hold players during `hold`. Not applied yet: players can move during the hold. Above zero. |
| `kit` | names | empty | Items each inventory is cleared to. Cleared items are destroyed, not dropped. Unknown names are logged and skipped. |
| `recipes` | names | empty | Blueprints granted on top of the game's own starting set. `[all]` grants every blueprint; `[]` grants nothing extra. `all` cannot be combined with other names. |
| `lockedRecipes` | names | `[Seamoth, Exosuit, Cyclops]` | Blueprints withheld from the grant. `[]` withholds nothing. |
| `vitals` | mapping | 100 each | `health`, `food` and `water`, each a whole percentage from 1 to 100. Validated but not applied yet: a round resets every contestant to the game's new-player values. |

How `recipes` and `lockedRecipes` combine:

| `recipes` | `lockedRecipes` | Grant |
| --- | --- | --- |
| omitted | omitted | the game's starting set only |
| omitted | written | every blueprint except the locked ones |
| `[all]` | any | every blueprint except the locked ones |
| `[]` | omitted | the game's starting set only |
| `[]` | written | refused at load |
| names | any | the starting set plus the named blueprints, minus the locked ones |

The grant never removes blueprints a player already knows. Blueprints scanned during play belong
to the player who scanned them.

## `medkit`

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| `cooldown` | duration | `0s` | Time after using a first aid kit before another may be used, in match time. `0s` turns the rule off. |

A refused kit is not consumed. A kit used at full health heals nothing and starts no cooldown.

## Checking a squeeze

`./scripts/check-zone [config]` (default `tournament.example.yaml`) reads the game's terrain from a
Subnautica install and checks what the loader cannot:

- Water: the share of water, rock and sky in each sphere, and the effective radius (the radius
  of a ball holding the same volume of water).
- Wall speed: the closing speed a player at the wall feels,
  `(previous radius − radius) × (1 + D / reach) ÷ shrinkFor`, where `reach` is `opening.radius`
  minus the last radius and `D` the drawn place's distance from the opening centre. Above 4 m/s
  (fin speed) warns; above 8 m/s fails. A Seaglide does about 11 m/s.
- Ceiling: `centre.y + radius`. If the opening or phase 1 leaves the sea surface outside the
  wall it fails, because rounds start on the surface. Deep later phases warn alongside `start.kit`.
- Spawn circle: with `spawn.circleRadius`, whether each pod is in water and inside the opening.

A sphere fails if its centre is in rock or above sea level, its effective radius is under 25 m, or
its wall closes faster than 8 m/s. With a destination pool, intermediate phases report the worst
case over every destination and the final sphere is checked for water at each one. When water is
the problem the tool suggests a nearby centre that works.

Options: `--preview DIR` writes a plan and section image per phase; `--self-test` checks the tool's
own arithmetic and terrain reader. The script exits non-zero on any failure.

## Chat commands

The match is driven with `/lms <verb>` in the Nitrox chat, or `lms <verb>` at the server console.
Run `/login <adminpassword>` first in each session; the command needs admin permission. An unknown
verb lists the valid ones, and a refused command explains why and shows the status.

| Command | Effect |
| --- | --- |
| `/lms`, `/lms status` | Group, state, phases fired, time to the next phase and where the wall is. |
| `/lms start` | Start the match from the lobby. |
| `/lms pause`, `/lms resume` | Hold the match and continue it. The wall stops where it is. |
| `/lms skip` | Start the next phase now and restart the cadence from this moment. No warning. |
| `/lms reset` | Replay this group from the lobby: no phases fired, opening sphere restored. |
| `/lms next` (`nextgroup`) | Move to the next group with a fresh match. |
| `/lms round` | Prepare and start a round: put contestants in survival mode, seat them on a fresh pod layout, remove derelict pods, clear inventories to `start.kit`, grant blueprints, then start the match after `start.hold`. Refuses outside the lobby, or when the group is not `spawn.groupSize` (it names who it counted). |
| `/lms round force` | `round` without the head-count check. |
| `/lms podspawn` (`pods`) | Seat everybody present on the pods the layout wants, reusing existing pods. Does not touch the clock. Players beyond `spawn.groupSize` stay where they are. Logs `PodsPlaced`. |
| `/lms podclear` | Remove pods the layout does not use, that nobody is assigned to and that nobody is within 10 m of. Logs `PodsCleared` with the number of stowed items lost. |
| `/lms podclear all` | Remove every escape pod in the world. Refuses, removing nothing, while anybody is in a pod. |
| `/lms ghost <player>` | Take a player out of the match (see [`ghosts`](#ghosts)). |
| `/lms unghost <player>` | Put them back. |
| `/lms look <player>` (`cam`) | Move every ghost's camera to a contestant once. |
| `/lms follow <player>` | Keep every ghost's camera on a contestant (see [`caster`](#caster)). |
| `/lms unfollow` | Release the follow. |
| `/lms followOne <ghost> <player>` | Keep one ghost's camera on a contestant. |

Nothing takes damage in the lobby or while paused. The in-game settings tab has a button for each
verb except the two-word forms and the verbs that take a name.
