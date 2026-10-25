// Package world reads Subnautica's compiled terrain octrees, so the booth's 3D map is built from
// the game's own voxels, caves included.
//
// It is a port of `scripts/lms_terrain.py`, which documents the format; TestGoAgreesWithPython
// keeps the two in step. The Go reader exists to walk every leaf in the world, which the
// point-query Python reader is too slow for.
package world

import (
	"encoding/binary"
	"fmt"
	"os"
	"path/filepath"
)

// The world, and where its origin sits in block space (from Nitrox's `SubnauticaMap`): blocks are
// 1 m and `block = world + (2048, 3200-160, 2048)`, so batch y 7..19 is world y -1920..+160.
const (
	BatchMetres = 160
	TreeMetres  = 32

	WorldX = 4096
	WorldY = 3200
	WorldZ = 4096

	OriginX = 2048
	OriginY = 3040
	OriginZ = 2048
)

// CacheDir is where the octrees live inside an install.
const CacheDir = "Subnautica_Data/StreamingAssets/SNUnmanagedData/Build18/CompiledOctreesCache"

// nodeBytes is the size of one octree node: a kind, a surface blend (unused), and a child index. A
// child index of zero is a leaf, so kind alone decides solidity.
const nodeBytes = 4

// Batch is one parsed 160 m file: the raw bytes plus where each 32 m tree starts. Kept as offsets
// rather than decoded nodes to save memory.
type Batch struct {
	blob    []byte
	trees   []int
	nx      int
	ny      int
	nz      int
	present bool
}

// TreesAlong is how many 32 m trees a batch holds on one axis: fewer than five at the world's edge,
// since 26 batches of 160 m overrun the 4096 m world.
func TreesAlong(worldMetres, batchIndex int) int {
	remaining := worldMetres - batchIndex*BatchMetres
	if remaining <= 0 {
		return 0
	}

	trees := (remaining + TreeMetres - 1) / TreeMetres
	if trees > BatchMetres/TreeMetres {
		return BatchMetres / TreeMetres
	}

	return trees
}

// LoadBatch parses one file. A missing file is not an error: it means no terrain was ever built
// there, which is open water or outside the world.
func LoadBatch(root string, bx, by, bz int) (*Batch, error) {
	path := filepath.Join(root, fmt.Sprintf("compiled-batch-%d-%d-%d.optoctrees", bx, by, bz))

	blob, err := os.ReadFile(filepath.Clean(path))
	if os.IsNotExist(err) {
		return &Batch{}, nil
	}
	if err != nil {
		return nil, err
	}

	batch := &Batch{
		blob:    blob,
		nx:      TreesAlong(WorldX, bx),
		ny:      TreesAlong(WorldY, by),
		nz:      TreesAlong(WorldZ, bz),
		present: true,
	}

	count := batch.nx * batch.ny * batch.nz
	batch.trees = make([]int, 0, count)

	offset := 4
	for i := 0; i < count; i++ {
		if offset+2 > len(blob) {
			return nil, fmt.Errorf("%s: ran out of bytes at tree %d of %d", path, i, count)
		}

		nodes := int(binary.LittleEndian.Uint16(blob[offset:]))
		offset += 2
		batch.trees = append(batch.trees, offset)
		offset += nodes * nodeBytes
	}

	// Every file in the game parses to the exact byte, so a mismatch means the layout is wrong.
	if offset != len(blob) {
		return nil, fmt.Errorf("%s: parsed %d of %d bytes as %dx%dx%d",
			path, offset, len(blob), batch.nx, batch.ny, batch.nz)
	}

	return batch, nil
}

// Present says whether the file existed.
func (b *Batch) Present() bool { return b.present }

// Tree returns the offset of one tree's nodes, and whether it is inside a partial batch. The index
// is `tx * ny * nz + ty * nz + tz`, established empirically; transposing x and z tears the surface
// at batch seams.
func (b *Batch) Tree(tx, ty, tz int) (int, bool) {
	if !b.present || tx >= b.nx || ty >= b.ny || tz >= b.nz || tx < 0 || ty < 0 || tz < 0 {
		return 0, false
	}

	return b.trees[tx*b.ny*b.nz+ty*b.nz+tz], true
}

// SolidAt reports whether a point inside one tree is rock. Coordinates are metres within the tree.
func (b *Batch) SolidAt(base, x, y, z int) bool {
	index, size := 0, TreeMetres
	for {
		at := base + index*nodeBytes
		kind := b.blob[at]
		child := int(binary.LittleEndian.Uint16(b.blob[at+2:]))
		if child == 0 {
			return kind != 0
		}

		size /= 2
		octant := 0
		if x >= size {
			octant |= 4
			x -= size
		}
		if y >= size {
			octant |= 2
			y -= size
		}
		if z >= size {
			octant |= 1
			z -= size
		}
		index = child + octant
	}
}
