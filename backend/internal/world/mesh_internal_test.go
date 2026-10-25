package world

import (
	"math"
	"testing"
)

// fill sets every cell of g from an analytic occupancy function, so the isosurface can be checked
// against a shape with a known answer. Needs no game install.
func fill(g *Grid, occupancy func(x, y, z float32) float32) {
	full := float32(g.Cell * g.Cell * g.Cell)
	for j := 0; j < g.NY; j++ {
		for k := 0; k < g.NZ; k++ {
			for i := 0; i < g.NX; i++ {
				x, y, z := g.Centre(i, j, k)
				g.counts[g.at(i, j, k)] = uint16(occupancy(x, y, z) * full)
			}
		}
	}
}

func TestTheIsosurfaceOfASphereIsASphere(t *testing.T) {
	const radius = 600

	grid := NewGrid(32, -640, 640)
	// A ramp rather than a step: a step puts every crossing halfway between cells, as real terrain
	// does not.
	fill(grid, func(x, y, z float32) float32 {
		distance := float32(math.Sqrt(float64(x*x + y*y + z*z)))
		return clamp01((radius + float32(grid.Cell)/2 - distance) / float32(grid.Cell))
	})

	mesh := SurfaceNets(grid, 0.5)
	if mesh.Vertices() == 0 {
		t.Fatal("no surface found in a field that is half full")
	}
	if mesh.Triangles() == 0 {
		t.Fatal("vertices but no triangles: the quads are not being joined")
	}

	worst := 0.0
	for v := 0; v < mesh.Vertices(); v++ {
		x := float64(mesh.Positions[v*3])
		y := float64(mesh.Positions[v*3+1])
		z := float64(mesh.Positions[v*3+2])
		off := math.Abs(math.Sqrt(x*x+y*y+z*z) - radius)
		if off > worst {
			worst = off
		}
	}

	// Within a cell of the true surface; any looser means crossings are not interpolated.
	if worst > float64(grid.Cell) {
		t.Errorf("worst vertex is %.1f m off a %d m sphere, want within one %d m cell",
			worst, radius, grid.Cell)
	}
	t.Logf("%d vertices, %d triangles, worst radius error %.2f m", mesh.Vertices(), mesh.Triangles(), worst)
}

func TestAFieldWithNoSurfaceMakesNoMesh(t *testing.T) {
	// A uniform field has no isosurface.
	for name, occupancy := range map[string]float32{"all water": 0, "all rock": 1} {
		grid := NewGrid(32, -128, 128)
		fill(grid, func(x, y, z float32) float32 { return occupancy })

		if mesh := SurfaceNets(grid, 0.5); mesh.Vertices() != 0 || mesh.Triangles() != 0 {
			t.Errorf("%s produced %d vertices and %d triangles, want none",
				name, mesh.Vertices(), mesh.Triangles())
		}
	}
}

func TestAHollowShellKeepsBothSurfaces(t *testing.T) {
	grid := NewGrid(32, -640, 640)
	fill(grid, func(x, y, z float32) float32 {
		distance := math.Sqrt(float64(x*x + y*y + z*z))
		if distance > 500 || distance < 300 {
			return 0
		}

		return 1
	})

	mesh := SurfaceNets(grid, 0.5)
	if mesh.Vertices() == 0 {
		t.Fatal("no surface found on a hollow shell")
	}

	inner, outer := 0, 0
	for v := 0; v < mesh.Vertices(); v++ {
		x := float64(mesh.Positions[v*3])
		y := float64(mesh.Positions[v*3+1])
		z := float64(mesh.Positions[v*3+2])
		if math.Sqrt(x*x+y*y+z*z) < 400 {
			inner++
		} else {
			outer++
		}
	}

	if inner == 0 {
		t.Error("the cavity's own wall is missing: this is the heightmap's failure, reproduced")
	}
	if outer == 0 {
		t.Error("the outer wall is missing")
	}
	t.Logf("%d vertices on the inner wall, %d on the outer", inner, outer)
}

func clamp01(v float32) float32 {
	if v < 0 {
		return 0
	}
	if v > 1 {
		return 1
	}

	return v
}

func TestSmoothingTurnsABinaryStepIntoAGradient(t *testing.T) {
	grid := NewGrid(8, -128, 128)
	fill(grid, func(x, y, z float32) float32 {
		if y < 0 {
			return 1
		}

		return 0
	})

	full := uint16(grid.Cell * grid.Cell * grid.Cell)
	partial := 0
	for j := 0; j < grid.NY; j++ {
		if v := grid.counts[grid.at(grid.NX/2, j, grid.NZ/2)]; v > 0 && v < full {
			partial++
		}
	}
	if partial != 0 {
		t.Fatalf("%d partial cells before smoothing, want a binary field to start from", partial)
	}

	grid.Smooth(1)

	for j := 0; j < grid.NY; j++ {
		if v := grid.counts[grid.at(grid.NX/2, j, grid.NZ/2)]; v > 0 && v < full {
			partial++
		}
	}
	if partial == 0 {
		t.Error("no partial cells after smoothing: every crossing is still at a cell midpoint")
	}
}

func TestSmoothingLeavesTheSurfaceWhereItWas(t *testing.T) {
	const radius = 600

	measure := func(passes int) float64 {
		grid := NewGrid(32, -640, 640)
		fill(grid, func(x, y, z float32) float32 {
			if math.Sqrt(float64(x*x+y*y+z*z)) <= radius {
				return 1
			}

			return 0
		})
		grid.Smooth(passes)

		mesh := SurfaceNets(grid, 0.5)
		if mesh.Vertices() == 0 {
			t.Fatalf("%d passes left no surface", passes)
		}

		sum := 0.0
		for v := 0; v < mesh.Vertices(); v++ {
			x := float64(mesh.Positions[v*3])
			y := float64(mesh.Positions[v*3+1])
			z := float64(mesh.Positions[v*3+2])
			sum += math.Sqrt(x*x + y*y + z*z)
		}

		return sum / float64(mesh.Vertices())
	}

	plain := measure(0)
	smoothed := measure(1)

	// A whole cell of drift would mean the blur is eroding rather than softening.
	if math.Abs(plain-smoothed) > 32 {
		t.Errorf("mean radius moved from %.1f to %.1f, want the surface to stay put", plain, smoothed)
	}
	t.Logf("mean radius %.1f m unsmoothed, %.1f m after one pass (true %d m)", plain, smoothed, radius)
}
