package tournament

import (
	"context"
	"testing"
)

func TestWritingABracketWritesDownEverybodyInIt(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetGroups(ctx, []Group{
		{Number: 1, Name: "Group A", Members: []string{"Ada", "Bea"}},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	roster, err := store.People(ctx)
	if err != nil {
		t.Fatalf("people: %v", err)
	}

	if len(roster) != 2 {
		t.Fatalf("a name typed into a group becomes a person: %+v", roster)
	}

	if roster[0].Label != roster[0].Name {
		t.Errorf("their label starts as their name: %+v", roster[0])
	}
}

func TestCorrectingSomebodysNameKeepsThemInTheirGroup(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetGroups(ctx, []Group{
		{Number: 2, Name: "Group B", Members: []string{"AdaPlaz", "Bea"}},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	roster, _ := store.People(ctx)

	var ada Person
	for _, person := range roster {
		if person.Name == "AdaPlaz" {
			ada = person
		}
	}

	if ada.ID == 0 {
		t.Fatalf("could not find the misspelt person: %+v", roster)
	}

	// She joined as AdaPlays, so the operator binds the unknown joiner to her.
	ada.Name = "AdaPlays"
	ada.Label = "Ada"

	corrected := []Person{ada}
	for _, person := range roster {
		if person.ID != ada.ID {
			corrected = append(corrected, person)
		}
	}

	if err := store.SetPeople(ctx, corrected); err != nil {
		t.Fatalf("set people: %v", err)
	}

	groups, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("groups: %v", err)
	}

	if len(groups) != 1 || len(groups[0].Members) != 2 {
		t.Fatalf("the group still has both of them: %+v", groups)
	}

	found := false
	for _, member := range groups[0].Members {
		if member == "AdaPlays" {
			found = true
		}
	}

	if !found {
		t.Fatalf("the group draws the corrected name: %+v", groups[0])
	}
}

func TestSomebodyLeftOffTheRosterIsRemoved(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetPeople(ctx, []Person{
		{Name: "Ada", Label: "Ada"}, {Name: "Bea", Label: "Bea"},
	}); err != nil {
		t.Fatalf("set people: %v", err)
	}

	roster, _ := store.People(ctx)
	if err := store.SetPeople(ctx, roster[:1]); err != nil {
		t.Fatalf("set people again: %v", err)
	}

	left, _ := store.People(ctx)
	if len(left) != 1 {
		t.Fatalf("people = %+v, want the one that was kept", left)
	}
}

func TestEditingSomebodyWhoIsNoLongerThereIsRefusedRatherThanInserted(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	err := store.SetPeople(ctx, []Person{{ID: 9999, Name: "Ada", Label: "Ada"}})

	if err == nil {
		t.Fatalf("editing a person who is not there has to be refused")
	}

	roster, _ := store.People(ctx)
	if len(roster) != 0 {
		t.Fatalf("and nothing is written: %+v", roster)
	}
}

func TestAnEmptyRosterIsALegitimateThingToWrite(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetPeople(ctx, []Person{{Name: "Ada", Label: "Ada"}}); err != nil {
		t.Fatalf("set people: %v", err)
	}

	if err := store.SetPeople(ctx, nil); err != nil {
		t.Fatalf("clearing the roster: %v", err)
	}

	roster, _ := store.People(ctx)
	if len(roster) != 0 {
		t.Fatalf("people = %+v, want none", roster)
	}
}

func TestAnOverrideIsAnAppendAndTheLastOneWins(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetPeople(ctx, []Person{{Name: "Caster", Label: "Caster"}}); err != nil {
		t.Fatalf("set people: %v", err)
	}

	roster, _ := store.People(ctx)
	caster := roster[0].ID

	first, err := store.Override(ctx, caster, RoleContestant, "wants to play group 3", "ops")
	if err != nil {
		t.Fatalf("override: %v", err)
	}

	// The name comes back with the row.
	if first.Name != "Caster" {
		t.Fatalf("the written row names who it is about: %+v", first)
	}

	if _, err := store.Override(ctx, caster, RoleDerived, "changed their mind", "ops"); err != nil {
		t.Fatalf("override again: %v", err)
	}

	overrides, err := store.Overrides(ctx)
	if err != nil {
		t.Fatalf("overrides: %v", err)
	}

	if len(overrides) != 2 {
		t.Fatalf("both rows stay, because the argument stays in the table: %+v", overrides)
	}

	if overrides[1].Role != RoleDerived {
		t.Fatalf("oldest first, so the later decision is last: %+v", overrides)
	}

	if overrides[0].Name != "Caster" || overrides[0].By != "ops" {
		t.Errorf("who it is about and who said so both survive: %+v", overrides[0])
	}
}

func TestAnOverrideGoesWhenThePersonDoes(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	if err := store.SetPeople(ctx, []Person{{Name: "Caster", Label: "Caster"}}); err != nil {
		t.Fatalf("set people: %v", err)
	}

	roster, _ := store.People(ctx)
	if _, err := store.Override(ctx, roster[0].ID, RoleGhost, "", "ops"); err != nil {
		t.Fatalf("override: %v", err)
	}

	if err := store.SetPeople(ctx, nil); err != nil {
		t.Fatalf("clear the roster: %v", err)
	}

	overrides, _ := store.Overrides(ctx)
	if len(overrides) != 0 {
		t.Fatalf("overrides = %+v, want none", overrides)
	}
}

func TestOutstandingIsWhatTheReconcilerMustNotAskForTwice(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	queued, _ := store.Queue(ctx, "ghost Caster", "reconciler")
	ran, _ := store.Queue(ctx, "unghost Ada", "reconciler")

	if err := store.MarkRan(ctx, ran.ID, "Ada is in the match again.", ran.QueuedAt); err != nil {
		t.Fatalf("mark ran: %v", err)
	}

	verbs, err := store.Outstanding(ctx)
	if err != nil {
		t.Fatalf("outstanding: %v", err)
	}

	if len(verbs) != 1 || verbs[0] != "ghost Caster" {
		t.Fatalf("outstanding = %v, want only the one nobody has answered for (queued %d)",
			verbs, queued.ID)
	}
}

func TestTheMigrationCarriesAnExistingBracketOntoPeople(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	// Rewind the schema to what 0002 left, with a group written the old way. 0006 is unrecorded too,
	// because it made the groups key composite and migrations replay in name order.
	if _, err := store.db.ExecContext(ctx, `
		DROP TABLE overrides, group_members, people;
		ALTER TABLE groups DROP CONSTRAINT groups_pkey;
		ALTER TABLE groups DROP COLUMN booth_id;
		ALTER TABLE groups ADD PRIMARY KEY (number);
		CREATE TABLE group_members (
			group_number INTEGER NOT NULL REFERENCES groups (number) ON DELETE CASCADE,
			player TEXT NOT NULL,
			seat INTEGER NOT NULL,
			PRIMARY KEY (group_number, player));
		INSERT INTO groups (number, name) VALUES (1, 'Group A');
		INSERT INTO group_members (group_number, player, seat) VALUES (1, 'Ada', 0), (1, 'Bea', 1);
		DELETE FROM schema_migrations WHERE name IN ('0004_people.sql', '0006_booths.sql')`); err != nil {
		t.Fatalf("rebuild the old schema: %v", err)
	}

	if err := store.Migrate(ctx); err != nil {
		t.Fatalf("migrate: %v", err)
	}

	groups, err := store.Groups(ctx)
	if err != nil {
		t.Fatalf("groups: %v", err)
	}

	if len(groups) != 1 || len(groups[0].Members) != 2 {
		t.Fatalf("the bracket survived with its membership: %+v", groups)
	}

	roster, _ := store.People(ctx)
	if len(roster) != 2 {
		t.Fatalf("and both of them are people now: %+v", roster)
	}
}

func TestWithdrawingTakesOnlyWhatNobodyHasCollected(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	queued, _ := store.Queue(ctx, "ghost Caster", "reconciler")
	sent, _ := store.Queue(ctx, "ghost Rook", "reconciler")
	mine, _ := store.Queue(ctx, "start", "ops")

	if err := store.MarkSent(ctx, []int64{sent.ID}, sent.QueuedAt); err != nil {
		t.Fatalf("mark sent: %v", err)
	}

	withdrawn, err := store.Withdraw(ctx, "reconciler", []string{"unghost Ada"})
	if err != nil {
		t.Fatalf("withdraw: %v", err)
	}

	if withdrawn != 1 {
		t.Fatalf("withdrew %d, want just the queued one the rule no longer wants", withdrawn)
	}

	states := map[int64]string{}
	rows, _ := store.Commands(ctx, 10)
	for _, row := range rows {
		states[row.ID] = row.State
	}

	if states[queued.ID] != StateExpired {
		t.Errorf("the stale one is given up on: %v", states[queued.ID])
	}

	if states[sent.ID] != StateSent {
		t.Errorf("one the mod may be running is left alone: %v", states[sent.ID])
	}

	if states[mine.ID] != StateQueued {
		t.Errorf("somebody's own click is not the rule's to withdraw: %v", states[mine.ID])
	}
}

func TestWithdrawingKeepsAVerbWithAwkwardCharactersInIt(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	awkward := `ghost O"Rourke`
	if _, err := store.Queue(ctx, awkward, "reconciler"); err != nil {
		t.Fatalf("queue: %v", err)
	}

	withdrawn, err := store.Withdraw(ctx, "reconciler", []string{awkward})
	if err != nil {
		t.Fatalf("withdraw: %v", err)
	}

	if withdrawn != 0 {
		t.Fatalf("withdrew %d, want none: it is still wanted", withdrawn)
	}
}
