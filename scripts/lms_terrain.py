"""Reads Subnautica's compiled terrain octrees, to check a zone has water in it, not just rock.

The game's own `Landscape` height API is unreachable (nothing derives from it), so the octrees are
the source; they are real voxels, so caves count.

usage:
    from lms_terrain import Terrain
    Terrain(install).is_solid(x, y, z)

Format: `Build18/CompiledOctreesCache/compiled-batch-{x}-{y}-{z}.optoctrees`, one per 160 m batch:

    int32   version                     (4)
    repeat per tree (5x5x5 grid of 32 m trees):
        uint16  nodeCount
        nodeCount x:
            byte    type                0 = empty (water or air), non-zero = solid
            byte    blend               surface material blend; unused here
            uint16  childIndex          0 = leaf, else children at [childIndex .. +7]

The world is 4096 m across, so the last batch on x and z holds 96 m: a 5x5x3 grid. Grid extents
come from the world dimensions; parsing to the exact byte confirms them.

Tree index is `tx * ny * nz + ty * nz + tz`; the child octant is `4*(x>=s) + 2*(y>=s) + 1*(z>=s)`.
The x/z-transposed mapping also parses but tears the surface at batch seams; `self_test` checks
for that.

World space: blocks are 1 m and `block = world + (2048, 3200 - 160, 2048)` (Nitrox's
`SubnauticaMap`), so batch y 7..19 is world y -1920..+160.
"""
import functools
import math
import pathlib
import struct

BATCH_METRES = 160
TREE_METRES = 32
TREES_PER_SIDE = BATCH_METRES // TREE_METRES
# SubnauticaMap.DimensionsInMeters, and BatchDimensionCenter = (4096/2, 3200 - 160, 4096/2).
WORLD_METRES = (4096, 3200, 4096)
ORIGIN = (2048, 3040, 2048)
CACHE_DIR = "Subnautica_Data/StreamingAssets/SNUnmanagedData/Build18/CompiledOctreesCache"

_NODE = struct.Struct("<BBH")


class MissingTerrain(Exception):
    """The install has no octree cache, so nothing here can answer anything."""


def trees_along(axis: int, batch_index: int) -> int:
    """How many 32 m trees a batch holds on one axis. Fewer than five at the edge of the world."""
    remaining = WORLD_METRES[axis] - batch_index * BATCH_METRES
    return max(0, min(TREES_PER_SIDE, -(-remaining // TREE_METRES)))


class Terrain:
    """Solidity queries against an installed world. One instance holds a parsed-batch cache."""

    def __init__(self, install: pathlib.Path, cache_size: int = 96):
        self.root = pathlib.Path(install) / CACHE_DIR
        if not self.root.is_dir():
            raise MissingTerrain(f"no octree cache at {self.root}")
        # Bounded: the whole cache is 1.2 GB and a large sphere touches most of it.
        self._batch = functools.lru_cache(maxsize=cache_size)(self._load)

    def _load(self, bx: int, by: int, bz: int):
        path = self.root / f"compiled-batch-{bx}-{by}-{bz}.optoctrees"
        if not path.exists():
            return None

        shape = (trees_along(0, bx), trees_along(1, by), trees_along(2, bz))

        blob = path.read_bytes()
        offset = 4
        trees = []
        for _ in range(shape[0] * shape[1] * shape[2]):
            count = struct.unpack_from("<H", blob, offset)[0]
            offset += 2
            trees.append((blob, offset))
            offset += count * 4

        # An exact-length parse is what validates the layout.
        if offset != len(blob):
            raise ValueError(f"{path.name}: parsed {offset} of {len(blob)} bytes as {shape}")

        return trees, shape

    def is_solid(self, x: float, y: float, z: float) -> bool:
        """True if the block containing this world point is rock rather than water or air."""
        bxx = math.floor(x) + ORIGIN[0]
        byy = math.floor(y) + ORIGIN[1]
        bzz = math.floor(z) + ORIGIN[2]

        loaded = self._batch(bxx // BATCH_METRES, byy // BATCH_METRES, bzz // BATCH_METRES)
        if loaded is None:
            # No file means no terrain was ever built there: open water, or outside the world.
            return False

        trees, (_nx, ny, nz) = loaded
        lx, ly, lz = bxx % BATCH_METRES, byy % BATCH_METRES, bzz % BATCH_METRES
        tx, ty, tz = lx // TREE_METRES, ly // TREE_METRES, lz // TREE_METRES
        if tx >= _nx or ty >= ny or tz >= nz:
            # Past the edge of a partial batch: outside the world, so not rock.
            return False

        blob, base = trees[tx * ny * nz + ty * nz + tz]

        x_, y_, z_ = lx % TREE_METRES, ly % TREE_METRES, lz % TREE_METRES
        index, size = 0, TREE_METRES
        while True:
            kind, _blend, child = _NODE.unpack_from(blob, base + index * 4)
            if child == 0:
                return kind != 0
            size //= 2
            octant = 0
            if x_ >= size:
                octant |= 4
                x_ -= size
            if y_ >= size:
                octant |= 2
                y_ -= size
            if z_ >= size:
                octant |= 1
                z_ -= size
            index = child + octant

    def surface_height(self, x: float, z: float, step: int = 4, top: int = 160, bottom: int = -1920):
        """Highest solid block in this column, or None for open water all the way down."""
        for y in range(top, bottom - 1, -step):
            if self.is_solid(x, y, z):
                return y
        return None
