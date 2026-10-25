# Reference data

Facts extracted from an installed copy of Subnautica, committed so that building and testing the
mod does not need the game. Regenerate them after a game update with the scripts named below; each
script needs a Subnautica install.

| File | Contents | Produced by |
| --- | --- | --- |
| `biome-names.txt` | The 19 biome names in the game's biome table | copied from the game |
| `biome-overrides.tsv` | Every per-batch biome override string in the world data | `./scripts/extract-biome-data` |
| `biome-override-prefabs.tsv` | Atmosphere prefabs that batch roots take their biome from | `./scripts/extract-biome-data` |
| `atmosphere-biomes.tsv` | The biome string of every atmosphere prefab, read in game | the `lmsbiomes` console command |
| `biome-override-families.tsv` | Prefix rules mapping override strings to tournament biomes | authored |
| `minimap.png` | Bathymetry for the in-game minimap and the booth's 2D map | `./scripts/make-minimap` |
| `heightmap.png`, `heightmap.json` | Seabed elevation for the booth's 3D map, and its encoding | `./scripts/make-heightmap` |

## Biomes

A biome is a label: it names the place a player is in on the HUD, in telemetry and in the audit log.
It does not affect damage or the zone.

`LargeWorld.GetBiome()` returns a biome string from one of two sources:

1. A per-batch override, if the world batch has one. It comes either from the batch's own data
   or, when the batch points at an atmosphere prefab, from that prefab's `AtmosphereVolume`, which
   replaces the batch's string once the prefab has loaded.
2. The biome table (`biomes.csv`), looked up through `biomeMap.bin`, a 2D grid of 4 m cells.
   Because the grid is 2D, deep areas report the biome at the surface above them; overrides are what
   name the Lost River, the lava zones and similar places correctly.

It returns null when the world is not loaded or a block has no properties.

### `biome-names.txt`

The `name` column of `Subnautica_Data/StreamingAssets/SNUnmanagedData/Build18/biomes.csv`. The list
is compiled into `Lms.Core` (`KnownBiomes`), and a test checks the two match. Three names resolve but
are not playable: `void` (outside the map), `unassigned` (the map's null value) and `arctic` (not part
of this game's map).

### `biome-overrides.tsv`

Every `overrideBiome` string stored on a `LargeWorldBatchRoot` in
`Build18/BatchObjectsCache/batch-objects-X-Y-Z.bin`, one file per batch.

| Column | Meaning |
| --- | --- |
| `overrideString` | The raw string. |
| `tournamentBiome` | The biome it resolves to. Authored; preserved when the file is regenerated. |
| `batches` | How many batches carry it. |
| `surfaceBiome` | What the 2D biome table says at those batches. |

`extract-biome-data` exits non-zero if the game contains a string that no rule maps.

### `biome-override-prefabs.tsv`

Atmosphere prefabs referenced by batch roots. The prefab's biome cannot be read offline (it lives in
an addressable bundle), so `overrideBiome` is filled in from `atmosphere-biomes.tsv` and preserved
when the file is regenerated. A dash means the prefab has no biome, which clears the batch's own
string so the 2D table applies.

| Column | Meaning |
| --- | --- |
| `atmospherePrefab` | Prefab class id. |
| `overrideBiome` | The prefab's biome string, or `-`. |
| `batches` | How many batches point at it. |
| `replacesLiteral` | How many of those batches also carry their own string, which the prefab replaces. |

### `atmosphere-biomes.tsv`

Written by the `lmsbiomes` command, typed into the developer console of a running session (no
tournament config needed). It loads every `WorldEntities/Atmosphere/` prefab, reads
`AtmosphereVolume.overrideBiome` the way the game does, resolves it, and writes
`lms-atmosphere-biomes.tsv` next to the mod. The header lines give a summary and the build that
produced it.

| Column | Meaning |
| --- | --- |
| `prefabPath` | Prefab path. |
| `classId` | Prefab class id. |
| `overrideBiome` | The prefab's biome string. |
| `resolved` | The tournament biome it resolves to. |
| `kind` | `Playable`, `OutsideMap`, `Unmapped` or `NotInThisGame`. |
| `outcome` | `Resolved`, a resolver failure name, `NoBiome` (the volume has none), `NoVolume` (no `AtmosphereVolume`) or `PrefabMissing` (failed to load). |

Only prefabs referenced by a batch root affect `GetBiome`; the rest feed the game's atmosphere
system (for example the Aurora's interior and the Precursor rooms), which the mod does not sample.

### `biome-override-families.tsv`

Prefix rules for override strings that do not start with a table name, such as `ILZChamber`
(`inactiveLavaZone`) or the `LostRiver_` family. Compiled into `Lms.Core` (`KnownBiomes`); a test
checks the file and the code agree and that every string in `biome-overrides.tsv` resolves.

### How a string is resolved

`Lms.Core.Biomes.BiomeResolver.Resolve`, all comparisons case-insensitive:

1. An exact match against the biome table.
2. The longest matching family prefix from `biome-override-families.tsv`.
3. The longest table name the string starts with (`grassyPlateaus_Grass` → `grassyPlateaus`).
4. Otherwise unresolved: `NoBiomeReported` (null), `BlankName` or `UnknownName`.

The raw string is kept either way. In game, `BiomeSampler` samples the biome under the local player
four times a second and logs each distinct string once; unresolved strings are also shown on screen.

A few batches carry both a string and an atmosphere prefab that resolve to different biomes, so near
the Safe Shallows / Grassy Plateaus edge and the Blood Kelp trench the reported biome can depend on
whether the prefab has finished loading.

## Maps

Both images are 512 × 512 and cover the world from (−2048, −2048) to (2048, 2048) metres. Pixel
(0, 0) is world (−2048, −2048), x increases to the right and z increases downward, so the images
are stored south-up; the renderers flip them to north-up.

### `minimap.png`

The depth band of the seabed in each column, sampled against a fixed ladder of depths. It ships
beside the client mod as `lms-minimap.png`, where the HUD draws it with the player's position and
the zone rings. The booth uses a copy as `backend/internal/web/static/map/terrain.png`.

```bash
./scripts/make-minimap --size 512    # writes docs/data/minimap.png
```

### `heightmap.png` and `heightmap.json`

Seabed elevation from a per-column downward scan, used by the booth's 3D map. The PNG stores the
elevation big-endian in the red and green channels (8-bit channels survive a browser canvas intact)
and a terrain flag in blue: 255 where the column has terrain, 0 where the scan found none.
`heightmap.json` describes the encoding so the renderer does not hard-code it:

| Field | Meaning |
| --- | --- |
| `size` | Image width and height in pixels. |
| `worldMetres` | World extent covered, in metres. |
| `metresPerPixel` | Horizontal resolution. |
| `elevTop`, `elevBottom` | Elevation in metres at `sampleMax` and at 0; samples are linear in between. |
| `sampleMax` | Largest sample value (65535). |
| `encoding` | Description of the channel layout. |
| `hasTerrain` | Blue-channel value that marks terrain. |
| `verticalStepMetres` | Vertical resolution of the scan. |
| `imageIsSouthUp` | `true`: row 0 is the southern edge. |

```bash
./scripts/make-heightmap --size 512  # writes docs/data/heightmap.png and heightmap.json
cp docs/data/heightmap.png docs/data/heightmap.json backend/internal/web/static/map/
```
