package tournament

import (
	"context"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
	"time"
)

// aCreatedBooth is one row's worth, an hour old and expiring in a day.
func aCreatedBooth(id string, expires time.Time) CreatedBooth {
	return CreatedBooth{
		ID:           id,
		IngestHash:   id + "-ingest-hash",
		ViewHash:     id + "-view-hash",
		OperatorHash: id + "-operator-hash",
		CreatedAt:    expires.Add(-24 * time.Hour),
		ExpiresAt:    expires,
	}
}

func TestACreatedBoothIsReadBackWithItsHashesAndItsExpiry(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	expires := time.Now().Add(24 * time.Hour).UTC().Truncate(time.Millisecond)
	if err := store.CreateBooth(ctx, aCreatedBooth("ada", expires)); err != nil {
		t.Fatalf("create: %v", err)
	}

	held, err := store.CreatedBooths(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(held) != 1 {
		t.Fatalf("read %d booths, want the one that was created: %+v", len(held), held)
	}

	if held[0].ID != "ada" || held[0].IngestHash != "ada-ingest-hash" ||
		held[0].ViewHash != "ada-view-hash" || held[0].OperatorHash != "ada-operator-hash" {
		t.Errorf("read %+v, want the hashes it was given", held[0])
	}

	if !held[0].ExpiresAt.Equal(expires) {
		t.Errorf("expires at %s, want %s", held[0].ExpiresAt, expires)
	}
}

func TestDroppingABoothTakesItsRowsInEveryTableWithIt(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	ada := store.For("ada")
	if err := store.CreateBooth(ctx, aCreatedBooth("ada", time.Now())); err != nil {
		t.Fatalf("create: %v", err)
	}

	fillEveryTable(t, ada)

	if err := store.DropBooth(ctx, "ada"); err != nil {
		t.Fatalf("drop: %v", err)
	}

	for table, count := range rowsPerTable(t, store, "ada") {
		if count != 0 {
			t.Errorf("%s still holds %d of ada's rows", table, count)
		}
	}

	held, err := store.CreatedBooths(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(held) != 0 {
		t.Errorf("the booths table still holds %+v", held)
	}
}

func TestDroppingABoothTwiceIsNotAnError(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.CreateBooth(ctx, aCreatedBooth("ada", time.Now())); err != nil {
		t.Fatalf("create: %v", err)
	}

	if err := store.DropBooth(ctx, "ada"); err != nil {
		t.Fatalf("first drop: %v", err)
	}

	if err := store.DropBooth(ctx, "ada"); err != nil {
		t.Errorf("second drop: %v, want a booth that is already gone to be no work rather than an error", err)
	}
}

func TestDroppingOneBoothLeavesAnothersRowsAlone(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	for _, id := range []string{"ada", "grace"} {
		if err := store.CreateBooth(ctx, aCreatedBooth(id, time.Now())); err != nil {
			t.Fatalf("create %s: %v", id, err)
		}

		fillEveryTable(t, store.For(id))
	}

	if err := store.DropBooth(ctx, "ada"); err != nil {
		t.Fatalf("drop: %v", err)
	}

	for table, count := range rowsPerTable(t, store, "grace") {
		if count == 0 {
			t.Errorf("%s lost grace's rows when ada was dropped", table)
		}
	}
}

// fillEveryTable puts at least one row in each of the tables a booth owns.
func fillEveryTable(t *testing.T, booth *Store) {
	t.Helper()

	ctx := context.Background()

	// SetGroups writes groups, group_members and people in one call.
	if err := booth.SetGroups(ctx, []Group{
		{Number: 1, Name: "the first group", Members: []string{"Ada"}, Series: "semi"},
	}, []FinalSeries{{Series: "semi", Seats: 1}}); err != nil {
		t.Fatalf("groups: %v", err)
	}

	if _, err := booth.AddAdjustment(ctx, Adjustment{
		Group: 1, Name: "Ada", Points: 1, Reason: "a bug", By: "somebody",
	}); err != nil {
		t.Fatalf("adjustment: %v", err)
	}

	if _, err := booth.Add(ctx, Attribution{
		Group: 1, Generation: 1, Victim: "Ada", Actor: "Rook", By: "somebody",
	}); err != nil {
		t.Fatalf("attribution: %v", err)
	}

	if _, err := booth.Queue(ctx, "status", "somebody"); err != nil {
		t.Fatalf("command: %v", err)
	}

	if _, err := booth.WriteConfig(ctx, "zone: {}", "somebody", ""); err != nil {
		t.Fatalf("config: %v", err)
	}

	people, err := booth.People(ctx)
	if err != nil || len(people) == 0 {
		t.Fatalf("people = %+v, %v", people, err)
	}

	if _, err := booth.Override(ctx, people[0].ID, "ghost", "", "somebody"); err != nil {
		t.Fatalf("override: %v", err)
	}
}

// rowsPerTable counts one booth's rows per table, so a failure names the table that was missed.
func rowsPerTable(t *testing.T, store *Store, booth string) map[string]int {
	t.Helper()

	counts := map[string]int{}

	for _, table := range boothTables {
		var count int

		// Table names cannot be placeholders; these come from `boothTables`, not from input.
		if err := store.db.QueryRowContext(context.Background(),
			`SELECT count(*) FROM `+table+` WHERE booth_id = $1`, booth).Scan(&count); err != nil {
			t.Fatalf("count %s: %v", table, err)
		}

		counts[table] = count
	}

	return counts
}

// A source scan that runs without a database. When this fails, fix DropBooth, not the test.
func TestDropBoothNamesEveryTableABoothOwns(t *testing.T) {
	t.Parallel()

	body, err := os.ReadFile(filepath.Clean("created.go"))
	if err != nil {
		t.Fatalf("read created.go: %v", err)
	}

	statements := strings.Join(statementsIn(string(body)), "\n")

	for _, table := range boothTables {
		if !regexp.MustCompile(`DELETE\s+FROM\s+` + table + `\b`).MatchString(statements) {
			t.Errorf("DropBooth deletes nothing from %s, so an expired booth's rows stay there", table)
		}
	}
}

// Holds scratch's hand-written drop list to the tables the migrations create, so no rows leak
// between tests.
func TestTheScratchDatabaseDropsEveryTableTheMigrationsCreate(t *testing.T) {
	t.Parallel()

	body, err := os.ReadFile(filepath.Clean("store_test.go"))
	if err != nil {
		t.Fatalf("read store_test.go: %v", err)
	}

	dropped := strings.Join(statementsIn(string(body)), "\n")

	for _, table := range migratedTables(t) {
		if !regexp.MustCompile(`\b` + table + `\b`).MatchString(dropped) {
			t.Errorf("scratch does not drop %s, so its rows survive into the next test", table)
		}
	}
}

// migratedTables is every table the embedded migrations create.
func migratedTables(t *testing.T) []string {
	t.Helper()

	names, err := migrationNames()
	if err != nil {
		t.Fatalf("migration names: %v", err)
	}

	created := regexp.MustCompile(`CREATE TABLE IF NOT EXISTS (\w+)`)
	tables := []string{}

	for _, name := range names {
		body, err := migrations.ReadFile("migrations/" + name)
		if err != nil {
			t.Fatalf("read %s: %v", name, err)
		}

		for _, match := range created.FindAllStringSubmatch(string(body), -1) {
			tables = append(tables, match[1])
		}
	}

	if len(tables) == 0 {
		t.Fatal("no CREATE TABLE found in the migrations, so this test proves nothing")
	}

	return tables
}
