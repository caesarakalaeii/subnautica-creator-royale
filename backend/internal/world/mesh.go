package world

// Mesh is an indexed triangle mesh in world metres.
type Mesh struct {
	Positions []float32
	Indices   []uint32
}

// Vertices is how many vertices the mesh has.
func (m *Mesh) Vertices() int { return len(m.Positions) / 3 }

// Triangles is how many triangles the mesh has.
func (m *Mesh) Triangles() int { return len(m.Indices) / 3 }

// corners is the number of corners of a dual cell; as bit flags, bit 0 is x, bit 1 is y, bit 2 is z.
const corners = 8

// edges are the twelve edges of a cube, each a pair of corner indices differing in one bit.
var edges = [12][2]int{
	{0, 1}, {2, 3}, {4, 5}, {6, 7}, // along x
	{0, 2}, {1, 3}, {4, 6}, {5, 7}, // along y
	{0, 4}, {1, 5}, {2, 6}, {3, 7}, // along z
}

// SurfaceNets extracts the isosurface of an occupancy field: one vertex per cell the surface
// crosses, at the average of its edge crossings, and a quad per crossing field edge. It needs no
// lookup table and is manifold by construction, but closes walls thinner than one cell.
func SurfaceNets(g *Grid, iso float32) *Mesh {
	cells := (g.NX - 1) * (g.NY - 1) * (g.NZ - 1)
	index := make([]int32, cells)
	for i := range index {
		index[i] = -1
	}

	cellAt := func(i, j, k int) int { return (j*(g.NZ-1)+k)*(g.NX-1) + i }

	mesh := &Mesh{}
	cell := float32(g.Cell)

	var value [corners]float32
	for j := 0; j < g.NY-1; j++ {
		for k := 0; k < g.NZ-1; k++ {
			for i := 0; i < g.NX-1; i++ {
				inside, outside := 0, 0
				for c := 0; c < corners; c++ {
					value[c] = g.Fraction(i+c&1, j+(c>>1)&1, k+(c>>2)&1)
					if value[c] >= iso {
						inside++
					} else {
						outside++
					}
				}
				if inside == 0 || outside == 0 {
					continue
				}

				// The average of the interpolated edge crossings, which makes the surface smooth.
				var sx, sy, sz float32
				crossings := 0
				for _, edge := range edges {
					a, b := value[edge[0]], value[edge[1]]
					if (a >= iso) == (b >= iso) {
						continue
					}

					t := (iso - a) / (b - a)
					ax := float32(edge[0] & 1)
					ay := float32((edge[0] >> 1) & 1)
					az := float32((edge[0] >> 2) & 1)
					bx := float32(edge[1] & 1)
					by := float32((edge[1] >> 1) & 1)
					bz := float32((edge[1] >> 2) & 1)

					sx += ax + (bx-ax)*t
					sy += ay + (by-ay)*t
					sz += az + (bz-az)*t
					crossings++
				}

				cx, cy, cz := g.Centre(i, j, k)
				index[cellAt(i, j, k)] = int32(mesh.Vertices())
				mesh.Positions = append(mesh.Positions,
					cx+sx/float32(crossings)*cell,
					cy+sy/float32(crossings)*cell,
					cz+sz/float32(crossings)*cell)
			}
		}
	}

	// A quad for every field edge that crosses the surface, joining the four cells around it. Winding
	// follows which end is rock, so normals face the water.
	quad := func(a, b, c, d int32, flip bool) {
		if a < 0 || b < 0 || c < 0 || d < 0 {
			return
		}
		if flip {
			b, d = d, b
		}
		mesh.Indices = append(mesh.Indices,
			uint32(a), uint32(b), uint32(c),
			uint32(a), uint32(c), uint32(d))
	}

	for j := 0; j < g.NY; j++ {
		for k := 0; k < g.NZ; k++ {
			for i := 0; i < g.NX; i++ {
				here := g.Fraction(i, j, k) >= iso

				if i+1 < g.NX && j >= 1 && k >= 1 && here != (g.Fraction(i+1, j, k) >= iso) {
					quad(index[cellAt(i, j-1, k-1)], index[cellAt(i, j, k-1)],
						index[cellAt(i, j, k)], index[cellAt(i, j-1, k)], here)
				}
				if j+1 < g.NY && i >= 1 && k >= 1 && here != (g.Fraction(i, j+1, k) >= iso) {
					quad(index[cellAt(i-1, j, k-1)], index[cellAt(i, j, k-1)],
						index[cellAt(i, j, k)], index[cellAt(i-1, j, k)], !here)
				}
				if k+1 < g.NZ && i >= 1 && j >= 1 && here != (g.Fraction(i, j, k+1) >= iso) {
					quad(index[cellAt(i-1, j-1, k)], index[cellAt(i, j-1, k)],
						index[cellAt(i, j, k)], index[cellAt(i-1, j, k)], here)
				}
			}
		}
	}

	return mesh
}
