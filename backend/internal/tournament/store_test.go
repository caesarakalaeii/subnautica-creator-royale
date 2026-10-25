package tournament

import (
	"context"
	"os"
	"slices"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// scratch returns a migrated store against a real Postgres, scoped to the tournament booth. Skipped
// unless `LMS_TEST_DATABASE_URL` is set:
//
//	docker run --rm -e POSTGRES_PASSWORD=lms -p 5433:5432 -d postgres:17
//	LMS_TEST_DATABASE_URL='postgres://postgres:lms@127.0.0.1:5433/postgres?sslmode=disable' \
//	  go test ./internal/tournament/
func scratch(t *testing.T) *Store {
	t.Helper()

	dsn := os.Getenv("LMS_TEST_DATABASE_URL")
	if dsn == "" {
		t.Skip("no LMS_TEST_DATABASE_URL, so there is no database to migrate against")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	t.Cleanup(cancel)

	store, err := Open(ctx, dsn)
	if err != nil {
		t.Fatalf("open: %v", err)
	}

	t.Cleanup(func() { _ = store.Close() })

	// Dropped rather than truncated so every test also runs the migrations.
	if _, err := store.db.ExecContext(ctx,
		`DROP TABLE IF EXISTS overrides, group_members, people, groups, final_series, attributions, adjustments, commands, configs, booths, schema_migrations`); err != nil {
		t.Fatalf("clear the scratch database: %v", err)
	}

	if err := store.Migrate(ctx); err != nil {
		t.Fatalf("migrate: %v", err)
	}

	// An unscoped store matches no rows, so scope to the reserved booth.
	return store.For(config.TournamentBooth)
}

func TestTheMigrationsApplyToAnEmptyDatabase(t *testing.T) {
	store := scratch(t)

	applied, err := store.Applied(context.Background())
	if err != nil {
		t.Fatalf("applied: %v", err)
	}

	// Against the embedded set, so a migration that was added but never applied fails here.
	names, err := migrationNames()
	if err != nil {
		t.Fatalf("migration names: %v", err)
	}

	if len(applied) != len(names) {
		t.Fatalf("applied = %v, want every migration: %v", applied, names)
	}
}

func TestMigratingTwiceChangesNothing(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.Migrate(ctx); err != nil {
		t.Fatalf("second migrate: %v", err)
	}

	applied, err := store.Applied(ctx)
	if err != nil {
		t.Fatalf("applied: %v", err)
	}

	names, err := migrationNames()
	if err != nil {
		t.Fatalf("migration names: %v", err)
	}

	if len(applied) != len(names) {
		t.Errorf("applied = %v, want every migration once each: %v", applied, names)
	}
}

func TestAnAttributionSurvivesBeingWrittenAndReadBack(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, err := store.Add(ctx, Attribution{
		Group: 1, Generation: 1, Victim: "Rook", Actor: "Vela", By: "ops",
	})
	if err != nil {
		t.Fatalf("add: %v", err)
	}

	if written.ID == 0 {
		t.Error("the row came back with no id, and the id is what decides which row is last")
	}

	if written.At.IsZero() {
		t.Error("the row came back with no timestamp")
	}

	rows, err := store.Attributions(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(rows) != 1 {
		t.Fatalf("rows = %d, want 1", len(rows))
	}

	if rows[0].Victim != "Rook" || rows[0].Actor != "Vela" || rows[0].By != "ops" {
		t.Errorf("row = %+v, want Vela credited with Rook by ops", rows[0])
	}
}

func TestAKillTakenBackReadsBackAsNobody(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if _, err := store.Add(ctx, Attribution{
		Group: 1, Generation: 1, Victim: "Rook", By: "ops",
	}); err != nil {
		t.Fatalf("add: %v", err)
	}

	rows, err := store.Attributions(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if rows[0].Actor != "" {
		t.Errorf("actor = %q, want nobody", rows[0].Actor)
	}
}

func TestAttributionsComeBackInTheOrderTheyWereWritten(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	for _, actor := range []string{"Ada", "Bea", "Cy"} {
		if _, err := store.Add(ctx, Attribution{
			Group: 1, Generation: 1, Victim: "Rook", Actor: actor, By: "ops",
		}); err != nil {
			t.Fatalf("add %s: %v", actor, err)
		}
	}

	rows, err := store.Attributions(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(rows) != 3 {
		t.Fatalf("rows = %d, want 3 — nothing is ever edited", len(rows))
	}

	if rows[0].Actor != "Ada" || rows[2].Actor != "Cy" {
		t.Errorf("order = %s, %s, %s; want Ada, Bea, Cy",
			rows[0].Actor, rows[1].Actor, rows[2].Actor)
	}

	// The fold depends on this order.
	if credits := Credits(rows); credits["Cy"] != 1 || credits["Ada"] != 0 {
		t.Errorf("credits = %v, want the last row to hold the kill", credits)
	}
}

func TestTheBracketsShapeSurvivesBeingWrittenAndReadBack(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	want := []Group{
		{Number: 1, Name: "Group 1", Members: []string{"Vela", "Rook", "Tess"}},
		{Number: 2, Name: "Group 2", Members: []string{"Ada", "Bea"}},
	}

	if err := store.SetGroups(ctx, want, nil); err != nil {
		t.Fatalf("write: %v", err)
	}

	got, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(got) != 2 {
		t.Fatalf("groups = %d, want 2", len(got))
	}

	// Members keep the order they were written in, not alphabetical.
	if len(got[0].Members) != 3 || got[0].Members[0] != "Vela" || got[0].Members[2] != "Tess" {
		t.Errorf("group 1 = %v, want Vela, Rook, Tess in that order", got[0].Members)
	}
}

func TestTheFinalsSeriesSurviveBeingWrittenAndReadBack(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	final := []FinalSeries{{Series: "winners semi 2", Seats: 2}, {Series: "losers final", Seats: 1}}
	if err := store.SetGroups(ctx, []Group{
		{Number: 18, Name: "Winners semi 2 · game 1", Series: "winners semi 2"},
		{Number: 23, Name: "Losers bracket · game 5", Series: "losers final"},
	}, final); err != nil {
		t.Fatalf("write: %v", err)
	}

	groups, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("read the groups: %v", err)
	}

	if len(groups) != 2 || groups[0].Series != "winners semi 2" || groups[1].Series != "losers final" {
		t.Errorf("groups = %+v, want each carrying its series", groups)
	}

	got, err := store.Final(ctx)
	if err != nil {
		t.Fatalf("read the final: %v", err)
	}

	if !slices.Equal(got, final) {
		t.Errorf("final = %+v, want %+v in the order written", got, final)
	}

	// Writing the bracket without a final removes the old spec.
	if err := store.SetGroups(ctx, groups, nil); err != nil {
		t.Fatalf("rewrite: %v", err)
	}

	if got, _ := store.Final(ctx); len(got) != 0 {
		t.Errorf("final = %+v, want none after a rewrite that wrote none", got)
	}
}

func TestWritingTheBracketAgainReplacesIt(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetGroups(ctx, []Group{
		{Number: 1, Name: "Group 1", Members: []string{"Vella"}},
	}, nil); err != nil {
		t.Fatalf("write: %v", err)
	}

	if err := store.SetGroups(ctx, []Group{
		{Number: 1, Name: "Group 1", Members: []string{"Vela"}},
	}, nil); err != nil {
		t.Fatalf("rewrite: %v", err)
	}

	got, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(got) != 1 || len(got[0].Members) != 1 || got[0].Members[0] != "Vela" {
		t.Errorf("groups = %+v, want the corrected spelling and nothing else", got)
	}
}

func TestAGroupWithNobodyInItStillReadsBack(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetGroups(ctx, []Group{{Number: 6, Name: "Final"}}, nil); err != nil {
		t.Fatalf("write: %v", err)
	}

	got, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(got) != 1 || got[0].Name != "Final" || len(got[0].Members) != 0 {
		t.Errorf("groups = %+v, want one empty Final", got)
	}
}

func TestAnAdjustmentSurvivesBeingWrittenAndReadBackInItsOwnBooth(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, err := store.AddAdjustment(ctx, Adjustment{
		Group: 5, Name: "Rook", Points: 1, Reason: "killed by a bug", By: "ops",
	})
	if err != nil {
		t.Fatalf("add: %v", err)
	}

	if written.ID == 0 || written.At.IsZero() {
		t.Errorf("row = %+v, want an id and a timestamp", written)
	}

	rows, err := store.Adjustments(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(rows) != 1 || rows[0].Name != "Rook" || rows[0].Points != 1 || rows[0].Group != 5 ||
		rows[0].Reason != "killed by a bug" || rows[0].By != "ops" {
		t.Fatalf("rows = %+v, want the one adjustment as written", rows)
	}

	if other, err := store.For("ada").Adjustments(ctx); err != nil || len(other) != 0 {
		t.Errorf("another booth reads %+v (%v), want nothing", other, err)
	}
}
