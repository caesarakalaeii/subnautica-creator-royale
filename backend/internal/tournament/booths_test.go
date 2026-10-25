package tournament

import (
	"context"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// twoScopes returns stores for two booths on one database. Skipped without LMS_TEST_DATABASE_URL.
func twoScopes(t *testing.T) (ada, grace *Store) {
	t.Helper()

	tournament := scratch(t)

	return tournament.For("ada"), tournament.For("grace")
}

// Runs without a database, so a For that ignored its argument is caught everywhere.
func TestForNarrowsToTheBoothItIsAsked(t *testing.T) {
	t.Parallel()

	unscoped := &Store{}

	ada := unscoped.For("ada")
	if ada.booth != "ada" {
		t.Errorf("For(%q).booth = %q, want the booth it was asked for", "ada", ada.booth)
	}

	// For must return a copy, or narrowing a second booth would move the first.
	grace := unscoped.For("grace")
	if grace == ada {
		t.Fatal("For returned the same *Store twice, so narrowing one booth moves another's")
	}
	if ada.booth != "ada" {
		t.Errorf("after narrowing grace, ada.booth = %q, want it untouched", ada.booth)
	}
	if unscoped.booth != "" {
		t.Errorf("For mutated its receiver: booth = %q, want it left unscoped", unscoped.booth)
	}
}

func TestOneBoothsAttributionsAreNotAnothers(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	if _, err := ada.Add(ctx, Attribution{
		Group: 1, Generation: 1, Victim: "Rook", Actor: "Vela", By: "ada",
	}); err != nil {
		t.Fatalf("ada's attribution: %v", err)
	}

	rows, err := grace.Attributions(ctx)
	if err != nil {
		t.Fatalf("grace's attributions: %v", err)
	}

	if len(rows) != 0 {
		t.Errorf("grace can read %d of ada's attributions: %+v", len(rows), rows)
	}

	mine, err := ada.Attributions(ctx)
	if err != nil {
		t.Fatalf("ada's attributions: %v", err)
	}

	if len(mine) != 1 {
		t.Errorf("ada reads %d of her own rows, want 1", len(mine))
	}
}

func TestTwoBoothsCanBothHaveAGroupOneWithTheSameNameInIt(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	for _, booth := range []*Store{ada, grace} {
		if err := booth.SetGroups(ctx, []Group{
			{Number: 1, Name: "The first group", Members: []string{"Ada", "Rook"}},
		}, nil); err != nil {
			t.Fatalf("writing group 1: %v", err)
		}
	}

	for name, booth := range map[string]*Store{"ada": ada, "grace": grace} {
		groups, err := booth.Groups(ctx)
		if err != nil {
			t.Fatalf("%s's groups: %v", name, err)
		}

		if len(groups) != 1 || len(groups[0].Members) != 2 {
			t.Errorf("%s reads %+v, want one group of two", name, groups)
		}
	}
}

func TestWritingOneBoothsRosterLeavesAnothersAlone(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	if err := grace.SetPeople(ctx, []Person{{Name: "Grace", Label: "Grace"}}); err != nil {
		t.Fatalf("grace's roster: %v", err)
	}

	if err := ada.SetPeople(ctx, []Person{{Name: "Ada", Label: "Ada"}}); err != nil {
		t.Fatalf("ada's roster: %v", err)
	}

	people, err := grace.People(ctx)
	if err != nil {
		t.Fatalf("grace's people: %v", err)
	}

	if len(people) != 1 || people[0].Name != "Grace" {
		t.Errorf("grace's roster after ada wrote hers = %+v, want just Grace", people)
	}
}

func TestACommandIsOnlyOwedToItsOwnBooth(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	if _, err := ada.Queue(ctx, "round force", "ada"); err != nil {
		t.Fatalf("queue: %v", err)
	}

	owed, err := grace.Owed(ctx, 0)
	if err != nil {
		t.Fatalf("grace's owed: %v", err)
	}

	if len(owed) != 0 {
		t.Errorf("grace is owed %+v, which is ada's command", owed)
	}

	mine, err := ada.Owed(ctx, 0)
	if err != nil {
		t.Fatalf("ada's owed: %v", err)
	}

	if len(mine) != 1 || mine[0].Verb != "round force" {
		t.Errorf("ada is owed %+v, want her own command", mine)
	}
}

func TestWritingOneBoothsRulesDoesNotSupersedeAnothers(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	if _, err := grace.WriteConfig(ctx, "zone: {}", "grace", ""); err != nil {
		t.Fatalf("grace's config: %v", err)
	}

	if _, err := ada.WriteConfig(ctx, "zone: {}", "ada", ""); err != nil {
		t.Fatalf("ada's config: %v", err)
	}

	owed, has, err := grace.ConfigOwed(ctx, 0)
	if err != nil {
		t.Fatalf("grace's owed config: %v", err)
	}

	if !has || owed.By != "grace" {
		t.Errorf("grace is owed %+v (has = %t), want her own revision still pending", owed, has)
	}
}

// Ids are unique across the table, so only the booth predicate stops a cross-booth read.
func TestARevisionCannotBeReadByAnotherBoothsId(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	written, err := ada.WriteConfig(ctx, "zone: {}", "ada", "hers")
	if err != nil {
		t.Fatalf("ada's config: %v", err)
	}

	if _, has, err := grace.ConfigAt(ctx, written.Revision); err != nil || has {
		t.Errorf("grace read ada's revision %d (has = %t, err = %v)", written.Revision, has, err)
	}

	if _, has, err := ada.ConfigAt(ctx, written.Revision); err != nil || !has {
		t.Errorf("ada cannot read her own revision %d (has = %t, err = %v)", written.Revision, has, err)
	}
}

func TestAnOverrideCannotNameSomebodyInAnotherBooth(t *testing.T) {
	ada, grace := twoScopes(t)
	ctx := context.Background()

	if err := ada.SetPeople(ctx, []Person{{Name: "Ada", Label: "Ada"}}); err != nil {
		t.Fatalf("ada's roster: %v", err)
	}

	people, err := ada.People(ctx)
	if err != nil || len(people) != 1 {
		t.Fatalf("ada's people = %+v, %v", people, err)
	}

	if _, err := grace.Override(ctx, people[0].ID, "ghost", "", "grace"); err == nil {
		t.Error("grace wrote an override against somebody in ada's booth")
	}

	overrides, err := grace.Overrides(ctx)
	if err != nil {
		t.Fatalf("grace's overrides: %v", err)
	}

	if len(overrides) != 0 {
		t.Errorf("grace's overrides = %+v, want none", overrides)
	}
}

func TestARowWrittenWithoutABoothBelongsToTheTournament(t *testing.T) {
	tournament := scratch(t)
	ctx := context.Background()

	// An INSERT that does not name a booth.
	if _, err := tournament.db.ExecContext(ctx, `
		INSERT INTO attributions (group_number, generation, victim, written_by)
		VALUES (1, 1, 'Rook', 'somebody')`); err != nil {
		t.Fatalf("write a row the old way: %v", err)
	}

	rows, err := tournament.Attributions(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	if len(rows) != 1 {
		t.Fatalf("the tournament booth reads %d rows, want the one written without a booth", len(rows))
	}

	other, err := tournament.For("ada").Attributions(ctx)
	if err != nil {
		t.Fatalf("ada's attributions: %v", err)
	}

	if len(other) != 0 {
		t.Errorf("a booth that is not %q reads %+v", config.TournamentBooth, other)
	}
}
