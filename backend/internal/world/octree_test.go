package world_test

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/world"
)

// install is the Subnautica install to read, or skips the test when there is none.
func install(t *testing.T) string {
	t.Helper()

	path := os.Getenv("SUBNAUTICA_INSTALLATION_PATH")
	if path == "" {
		home, err := os.UserHomeDir()
		if err != nil {
			t.Skip("no home directory to look for an install in")
		}
		path = filepath.Join(home, ".local/share/Steam/steamapps/common/Subnautica")
	}

	if _, err := os.Stat(filepath.Join(path, world.CacheDir)); err != nil {
		t.Skip("no Subnautica install: this is a check-game test")
	}

	return path
}

type solidityFixture struct {
	Points []struct {
		X     int  `json:"x"`
		Y     int  `json:"y"`
		Z     int  `json:"z"`
		Solid bool `json:"solid"`
	} `json:"points"`
}

// `testdata/solidity.json` is written by `scripts/lms_terrain.py` and holds both readers to the same
// answers. Regenerate it after any change to either reader, sampling a seeded spread plus every
// world corner and batch seam.
func TestGoAgreesWithPythonAboutSolidity(t *testing.T) {
	root := install(t)

	raw, err := os.ReadFile(filepath.Clean("testdata/solidity.json"))
	if err != nil {
		t.Fatalf("reading the solidity fixture: %v", err)
	}

	var fixture solidityFixture
	if err := json.Unmarshal(raw, &fixture); err != nil {
		t.Fatalf("the solidity fixture does not parse: %v", err)
	}
	if len(fixture.Points) < 1000 {
		t.Fatalf("the fixture has %d points, want a real spread", len(fixture.Points))
	}

	terrain, err := world.Open(root)
	if err != nil {
		t.Fatalf("opening the world: %v", err)
	}

	disagreements := 0
	for _, point := range fixture.Points {
		got, err := terrain.IsSolid(point.X, point.Y, point.Z)
		if err != nil {
			t.Fatalf("(%d,%d,%d): %v", point.X, point.Y, point.Z, err)
		}
		if got != point.Solid {
			disagreements++
			if disagreements <= 10 {
				t.Errorf("(%d,%d,%d): Go says solid=%v, lms_terrain.py says %v",
					point.X, point.Y, point.Z, got, point.Solid)
			}
		}
	}

	if disagreements > 0 {
		t.Errorf("%d of %d points disagree", disagreements, len(fixture.Points))
	}
}

func TestEveryBatchParsesToTheExactByte(t *testing.T) {
	root := filepath.Join(install(t), world.CacheDir)

	entries, err := os.ReadDir(root)
	if err != nil {
		t.Fatalf("reading the cache: %v", err)
	}

	files, partial := 0, 0
	for _, entry := range entries {
		var bx, by, bz int
		if _, err := fmtSscan(entry.Name(), &bx, &by, &bz); err != nil {
			continue
		}

		batch, err := world.LoadBatch(root, bx, by, bz)
		if err != nil {
			t.Fatalf("%s: %v", entry.Name(), err)
		}
		if !batch.Present() {
			t.Fatalf("%s: read as absent", entry.Name())
		}
		files++

		if world.TreesAlong(world.WorldX, bx) != 5 || world.TreesAlong(world.WorldZ, bz) != 5 {
			partial++
		}
	}

	if files < 5000 {
		t.Fatalf("parsed %d batches, want the whole cache", files)
	}
	// 4096 m is 25.6 batches of 160 m, so the last batch on x and on z holds three trees, not five.
	if partial == 0 {
		t.Error("no partial batches seen: the edge-of-world case is not being exercised")
	}
	t.Logf("parsed %d batches, %d of them partial at the edge of the world", files, partial)
}

// fmtSscan parses the three batch indices from a filename and rejects anything else.
func fmtSscan(name string, bx, by, bz *int) (int, error) {
	return fmt.Sscanf(name, "compiled-batch-%d-%d-%d.optoctrees", bx, by, bz)
}

func TestTheFieldAgreesWithTheOctrees(t *testing.T) {
	root := install(t)

	terrain, err := world.Open(root)
	if err != nil {
		t.Fatalf("opening the world: %v", err)
	}

	// A shallow band covering the Safe Shallows, to keep the test to a few thousand batches.
	grid := world.NewGrid(8, -256, 160)
	if err := grid.Fill(terrain, nil); err != nil {
		t.Fatalf("filling the field: %v", err)
	}

	// A full cell must be rock at its centre and an empty one must not; partial cells prove nothing.
	checked, mismatched := 0, 0
	for j := 0; j < grid.NY; j += 3 {
		for k := 7; k < grid.NZ; k += 37 {
			for i := 11; i < grid.NX; i += 41 {
				fraction := grid.Fraction(i, j, k)
				if fraction > 0 && fraction < 1 {
					continue
				}

				x, y, z := grid.Centre(i, j, k)
				solid, err := terrain.IsSolid(int(x), int(y), int(z))
				if err != nil {
					t.Fatalf("probing (%v,%v,%v): %v", x, y, z, err)
				}
				checked++
				if solid != (fraction == 1) {
					mismatched++
					if mismatched <= 5 {
						t.Errorf("cell (%d,%d,%d) at (%.0f,%.0f,%.0f) has fraction %v but IsSolid says %v",
							i, j, k, x, y, z, fraction, solid)
					}
				}
			}
		}
	}

	if checked < 500 {
		t.Fatalf("only %d uniform cells checked, want a real sample", checked)
	}
	t.Logf("%d uniform cells agree with the octrees, %d do not", checked, mismatched)
}

// The world's centre is the Safe Shallows under lifepod 5, where the seabed is about 20 m down.
func TestTheFieldPutsTheSafeShallowsWhereTheyAre(t *testing.T) {
	terrain, err := world.Open(install(t))
	if err != nil {
		t.Fatalf("opening the world: %v", err)
	}

	grid := world.NewGrid(8, -256, 160)
	if err := grid.Fill(terrain, nil); err != nil {
		t.Fatalf("filling the field: %v", err)
	}

	centreI, centreK := grid.NX/2, grid.NZ/2
	surface := 0
	found := false
	for j := grid.NY - 1; j >= 0; j-- {
		if grid.Fraction(centreI, j, centreK) >= 0.5 {
			_, y, _ := grid.Centre(centreI, j, centreK)
			surface = int(y)
			found = true

			break
		}
	}

	if !found {
		t.Fatal("no seabed at the world's centre, which should be the Safe Shallows")
	}
	// The heightmap gives -20 m at the same place.
	if surface < -48 || surface > 8 {
		t.Errorf("the seabed at the world's centre is %d m, want about -20 m (the Safe Shallows)", surface)
	}
	t.Logf("the seabed at the world's centre is %d m", surface)
}
