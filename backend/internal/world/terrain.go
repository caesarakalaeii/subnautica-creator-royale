package world

import (
	"fmt"
	"os"
	"path/filepath"
)

// Terrain answers solidity against an installed world and caches every batch it reads. Unbounded,
// since it is meant to walk the whole world once; the full cache is about 1.2 GB.
type Terrain struct {
	root    string
	batches map[[3]int]*Batch
}

// Open fails only when the octree cache is absent, meaning install is not a Subnautica install.
func Open(install string) (*Terrain, error) {
	root := filepath.Join(install, CacheDir)

	info, err := os.Stat(root)
	if err != nil || !info.IsDir() {
		return nil, fmt.Errorf("no octree cache at %s: this needs a Subnautica install", root)
	}

	return &Terrain{root: root, batches: make(map[[3]int]*Batch)}, nil
}

func (t *Terrain) batch(bx, by, bz int) (*Batch, error) {
	key := [3]int{bx, by, bz}
	if found, ok := t.batches[key]; ok {
		return found, nil
	}

	loaded, err := LoadBatch(t.root, bx, by, bz)
	if err != nil {
		return nil, err
	}
	t.batches[key] = loaded

	return loaded, nil
}

// Forget drops a cached batch so a finished part of a walk releases its memory.
func (t *Terrain) Forget(bx, by, bz int) {
	delete(t.batches, [3]int{bx, by, bz})
}

// IsSolid reports whether the block containing this world point is rock rather than water or air.
// Uses floor division and modulus, since block coordinates go negative and truncation would pick
// the wrong batch. Matches `lms_terrain.Terrain.is_solid` (see TestGoAgreesWithPython).
func (t *Terrain) IsSolid(x, y, z int) (bool, error) {
	bx := x + OriginX
	by := y + OriginY
	bz := z + OriginZ

	loaded, err := t.batch(floorDiv(bx, BatchMetres), floorDiv(by, BatchMetres), floorDiv(bz, BatchMetres))
	if err != nil {
		return false, err
	}
	if !loaded.Present() {
		return false, nil
	}

	lx := floorMod(bx, BatchMetres)
	ly := floorMod(by, BatchMetres)
	lz := floorMod(bz, BatchMetres)

	base, ok := loaded.Tree(lx/TreeMetres, ly/TreeMetres, lz/TreeMetres)
	if !ok {
		// Past the edge of a partial batch: outside the world, so not rock.
		return false, nil
	}

	return loaded.SolidAt(base, lx%TreeMetres, ly%TreeMetres, lz%TreeMetres), nil
}

func floorDiv(a, b int) int {
	quotient := a / b
	if a%b != 0 && (a < 0) != (b < 0) {
		quotient--
	}

	return quotient
}

func floorMod(a, b int) int {
	remainder := a % b
	if remainder != 0 && (remainder < 0) != (b < 0) {
		remainder += b
	}

	return remainder
}
