package world

import "encoding/binary"

// Grid is the world as an occupancy field: for each cell, what fraction of it is rock. A fraction
// rather than a flag gives the isosurface sub-cell crossings, so surfaces come out smooth.
type Grid struct {
	// Cell is the cell size in metres.
	Cell int

	// NX, NY, NZ are the cell counts, and MinY is the world elevation of the bottom row.
	NX, NY, NZ int
	MinY       int

	// counts is solid cubic metres per cell, so the fraction is counts / Cell^3.
	counts []uint16
}

// NewGrid sizes a field over the whole world between two elevations.
func NewGrid(cell, minY, maxY int) *Grid {
	grid := &Grid{
		Cell: cell,
		NX:   WorldX / cell,
		NY:   (maxY - minY) / cell,
		NZ:   WorldZ / cell,
		MinY: minY,
	}
	grid.counts = make([]uint16, grid.NX*grid.NY*grid.NZ)

	return grid
}

func (g *Grid) at(i, j, k int) int { return (j*g.NZ+k)*g.NX + i }

// Fraction is how much of a cell is rock, from 0 to 1. Outside the field is water.
func (g *Grid) Fraction(i, j, k int) float32 {
	if i < 0 || j < 0 || k < 0 || i >= g.NX || j >= g.NY || k >= g.NZ {
		return 0
	}

	full := float32(g.Cell * g.Cell * g.Cell)

	return float32(g.counts[g.at(i, j, k)]) / full
}

// Centre is the world position of a cell's centre, in metres.
func (g *Grid) Centre(i, j, k int) (float32, float32, float32) {
	half := float32(g.Cell) / 2

	return float32(i*g.Cell) - WorldX/2 + half,
		float32(g.MinY+j*g.Cell) + half,
		float32(k*g.Cell) - WorldZ/2 + half
}

// Fill adds every leaf of every batch in the world to the field. Octree nodes are aligned to their
// size and the cell size is a power of two, so a leaf never straddles cells and the sum is exact.
func (g *Grid) Fill(t *Terrain, progress func(done, total int)) error {
	minBatchY := floorDiv(g.MinY+OriginY, BatchMetres)
	maxBatchY := floorDiv(g.MinY+g.NY*g.Cell+OriginY-1, BatchMetres)
	batchesX := WorldX / BatchMetres
	batchesZ := WorldZ / BatchMetres
	if WorldX%BatchMetres != 0 {
		batchesX++
	}
	if WorldZ%BatchMetres != 0 {
		batchesZ++
	}

	total := batchesX * (maxBatchY - minBatchY + 1) * batchesZ
	done := 0

	for bx := 0; bx < batchesX; bx++ {
		for by := minBatchY; by <= maxBatchY; by++ {
			for bz := 0; bz < batchesZ; bz++ {
				batch, err := t.batch(bx, by, bz)
				if err != nil {
					return err
				}
				if batch.Present() {
					g.fillBatch(batch, bx, by, bz)
				}
				// Released immediately: each batch is visited once and the whole cache is about 1.2 GB.
				t.Forget(bx, by, bz)

				done++
				if progress != nil && done%500 == 0 {
					progress(done, total)
				}
			}
		}
	}

	if progress != nil {
		progress(total, total)
	}

	return nil
}

func (g *Grid) fillBatch(batch *Batch, bx, by, bz int) {
	for tx := 0; tx < batch.nx; tx++ {
		for ty := 0; ty < batch.ny; ty++ {
			for tz := 0; tz < batch.nz; tz++ {
				base, ok := batch.Tree(tx, ty, tz)
				if !ok {
					continue
				}

				// The tree's origin in block space.
				ox := bx*BatchMetres + tx*TreeMetres
				oy := by*BatchMetres + ty*TreeMetres
				oz := bz*BatchMetres + tz*TreeMetres

				g.walk(batch, base, 0, TreeMetres, ox, oy, oz)
			}
		}
	}
}

// walk descends one node, adding solid leaves to the field.
func (g *Grid) walk(batch *Batch, base, index, size, ox, oy, oz int) {
	at := base + index*nodeBytes
	kind := batch.blob[at]
	child := int(binary.LittleEndian.Uint16(batch.blob[at+2:]))

	if child == 0 {
		if kind != 0 {
			g.addSolid(ox, oy, oz, size)
		}

		return
	}

	half := size / 2
	for octant := 0; octant < 8; octant++ {
		dx, dy, dz := 0, 0, 0
		if octant&4 != 0 {
			dx = half
		}
		if octant&2 != 0 {
			dy = half
		}
		if octant&1 != 0 {
			dz = half
		}
		g.walk(batch, base, child+octant, half, ox+dx, oy+dy, oz+dz)
	}
}

// addSolid adds a solid cube, given in block coordinates, to the field.
func (g *Grid) addSolid(ox, oy, oz, size int) {
	// Block space to world metres, then to cells, relative to the field's own floor.
	wx := ox - OriginX
	wy := oy - OriginY
	wz := oz - OriginZ

	if wy+size <= g.MinY || wy >= g.MinY+g.NY*g.Cell {
		return
	}

	if size >= g.Cell {
		// Whole cells; alignment guarantees they are whole.
		for y := wy; y < wy+size; y += g.Cell {
			j := (y - g.MinY) / g.Cell
			if j < 0 || j >= g.NY {
				continue
			}
			for x := wx; x < wx+size; x += g.Cell {
				i := (x + WorldX/2) / g.Cell
				if i < 0 || i >= g.NX {
					continue
				}
				for z := wz; z < wz+size; z += g.Cell {
					k := (z + WorldZ/2) / g.Cell
					if k < 0 || k >= g.NZ {
						continue
					}
					g.counts[g.at(i, j, k)] = uint16(g.Cell * g.Cell * g.Cell)
				}
			}
		}

		return
	}

	// Smaller than a cell, so inside exactly one.
	i := (wx + WorldX/2) / g.Cell
	j := (wy - g.MinY) / g.Cell
	k := (wz + WorldZ/2) / g.Cell
	if i < 0 || j < 0 || k < 0 || i >= g.NX || j >= g.NY || k >= g.NZ {
		return
	}
	g.counts[g.at(i, j, k)] += uint16(size * size * size)
}

// Smooth blurs the field in place, once per pass, with a separable 1-2-1 kernel. Large uniform
// octree nodes make the field effectively binary, which yields stepped slabs; the game smooths
// these too. More than one pass starts to close thin cave passages.
func (g *Grid) Smooth(passes int) {
	scratch := make([]uint16, len(g.counts))

	blur := func(stride, count int) {
		copy(scratch, g.counts)
		for j := 0; j < g.NY; j++ {
			for k := 0; k < g.NZ; k++ {
				for i := 0; i < g.NX; i++ {
					at := g.at(i, j, k)
					// Outside the field is water, so the world's edge blurs towards empty.
					var before, after uint32
					if index := at - stride; index >= 0 && onAxis(g, i, j, k, stride, -1) {
						before = uint32(scratch[index])
					}
					if index := at + stride; index < len(scratch) && onAxis(g, i, j, k, stride, 1) {
						after = uint32(scratch[index])
					}
					g.counts[at] = uint16((before + 2*uint32(scratch[at]) + after + 2) / 4)
				}
			}
		}
	}

	for pass := 0; pass < passes; pass++ {
		blur(1, g.NX)
		blur(g.NX, g.NZ)
		blur(g.NX*g.NZ, g.NY)
	}
}

// onAxis says whether stepping one cell along the axis `stride` belongs to stays inside the field,
// so a blur does not wrap from one row into the next.
func onAxis(g *Grid, i, j, k, stride, direction int) bool {
	switch stride {
	case 1:
		return i+direction >= 0 && i+direction < g.NX
	case g.NX:
		return k+direction >= 0 && k+direction < g.NZ
	default:
		return j+direction >= 0 && j+direction < g.NY
	}
}
