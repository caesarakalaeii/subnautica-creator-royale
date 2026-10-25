package tournament

import (
	"reflect"
	"testing"
)

func people(names ...string) []Person {
	held := make([]Person, 0, len(names))
	for i, name := range names {
		held = append(held, Person{ID: int64(i + 1), Name: name, Label: name})
	}

	return held
}

func seats(names ...string) []Seat {
	held := make([]Seat, 0, len(names))
	for i, name := range names {
		held = append(held, Seat{Person: int64(i + 1), Name: name, Label: name})
	}

	return held
}

func connected(names ...string) []Presence {
	held := make([]Presence, 0, len(names))
	for _, name := range names {
		held = append(held, Presence{Name: name, InMatch: true})
	}

	return held
}

func TestEverybodyConnectedWhoIsNotInThePlayingGroupIsAGhost(t *testing.T) {
	roles := Intended(
		connected("Ada", "Bea", "Cy", "Caster"),
		seats("Ada", "Bea"),
		people("Ada", "Bea", "Cy", "Caster"),
		nil)

	if !reflect.DeepEqual(roles.Contestants, []string{"Ada", "Bea"}) {
		t.Fatalf("contestants = %v, want the playing group", roles.Contestants)
	}

	if !reflect.DeepEqual(roles.Ghosts, []string{"Caster", "Cy"}) {
		t.Fatalf("ghosts = %v, want everybody else", roles.Ghosts)
	}
}

func TestAGroupMemberWhoHasNotJoinedIsNotOnEitherList(t *testing.T) {
	roles := Intended(connected("Ada"), seats("Ada", "Bea"), people("Ada", "Bea"), nil)

	if len(roles.Contestants) != 1 || roles.Contestants[0] != "Ada" {
		t.Fatalf("contestants = %v, want just the one who is here", roles.Contestants)
	}

	if len(roles.Ghosts) != 0 {
		t.Fatalf("ghosts = %v, want nobody", roles.Ghosts)
	}
}

func TestAJoinerNobodyWroteDownIsSurfacedRatherThanSwallowed(t *testing.T) {
	roles := Intended(connected("Ada", "WhoIsThis"), seats("Ada"), people("Ada"), nil)

	if !reflect.DeepEqual(roles.Unbound, []string{"WhoIsThis"}) {
		t.Fatalf("unbound = %v, want the name nobody wrote down", roles.Unbound)
	}

	// Neither ghosted nor unghosted: only a human can tell a roster typo from an unexpected player.
	for _, name := range append(roles.Ghosts, roles.Contestants...) {
		if name == "WhoIsThis" {
			t.Fatalf("an unbound joiner must be neither ghosted nor un-ghosted: %+v", roles)
		}
	}
}

func TestANameIsMatchedWithoutCase(t *testing.T) {
	roles := Intended(
		[]Presence{{Name: "adaplays", InMatch: true}},
		seats("AdaPlays"), people("AdaPlays"), nil)

	if !reflect.DeepEqual(roles.Contestants, []string{"adaplays"}) {
		t.Fatalf("contestants = %v, ghosts = %v: the same person either way",
			roles.Contestants, roles.Ghosts)
	}
}

func TestAnOverrideBeatsTheDerivedRuleInBothDirections(t *testing.T) {
	roster := people("Ada", "Caster")

	// A caster who wants to play this group, and a creator who is ill.
	roles := Intended(
		connected("Ada", "Caster"),
		seats("Ada"),
		roster,
		[]Override{
			{Person: roster[0].ID, Role: RoleGhost},
			{Person: roster[1].ID, Role: RoleContestant},
		})

	if !reflect.DeepEqual(roles.Ghosts, []string{"Ada"}) {
		t.Fatalf("ghosts = %v, want the one who was overridden out", roles.Ghosts)
	}

	if !reflect.DeepEqual(roles.Contestants, []string{"Caster"}) {
		t.Fatalf("contestants = %v, want the one who was overridden in", roles.Contestants)
	}
}

func TestTheLastOverrideForAPersonIsTheOneThatCounts(t *testing.T) {
	roster := people("Ada")

	roles := Intended(connected("Ada"), seats("Ada"), roster, []Override{
		{Person: roster[0].ID, Role: RoleGhost},
		{Person: roster[0].ID, Role: RoleDerived},
	})

	if !reflect.DeepEqual(roles.Contestants, []string{"Ada"}) {
		t.Fatalf("a `derived` row hands the person back to the rule: %+v", roles)
	}
}

func TestNothingIsPlayingMeansEverybodyConnectedIsAGhost(t *testing.T) {
	// Callers therefore refuse to reconcile against an empty group, since the bracket may be unwritten.
	roles := Intended(connected("Ada", "Bea"), nil, people("Ada", "Bea"), nil)

	if len(roles.Ghosts) != 2 {
		t.Fatalf("ghosts = %v, want everybody", roles.Ghosts)
	}
}

func TestTheDifferenceIsTheVerbsTheModAlreadyHas(t *testing.T) {
	intended := Roles{Ghosts: []string{"Caster"}, Contestants: []string{"Ada"}}

	verbs := Reconcile(intended, []Presence{
		{Name: "Ada", InMatch: false},
		{Name: "Caster", InMatch: true},
	})

	// Ghosts first, so nobody extra is briefly in the match.
	if !reflect.DeepEqual(verbs, []string{"ghost Caster", "unghost Ada"}) {
		t.Fatalf("verbs = %v", verbs)
	}
}

func TestAModThatAlreadyAgreesIsAskedForNothing(t *testing.T) {
	intended := Roles{Ghosts: []string{"Caster"}, Contestants: []string{"Ada"}}

	verbs := Reconcile(intended, []Presence{
		{Name: "Ada", InMatch: true},
		{Name: "Caster", InMatch: false},
	})

	if len(verbs) != 0 {
		t.Fatalf("verbs = %v, want none: the difference is empty", verbs)
	}
}

func TestTheVerbCarriesTheModsOwnSpelling(t *testing.T) {
	intended := Roles{Ghosts: []string{"AdaPlays"}}

	verbs := Reconcile(intended, []Presence{{Name: "adaplays", InMatch: true}})

	if !reflect.DeepEqual(verbs, []string{"ghost adaplays"}) {
		t.Fatalf("verbs = %v, want the name as the mod spelled it", verbs)
	}
}

func TestSomebodyTheBoothHasNoOpinionAboutIsLeftAlone(t *testing.T) {
	verbs := Reconcile(Roles{}, []Presence{
		{Name: "WhoIsThis", InMatch: true},
		{Name: "AndThis", InMatch: false},
	})

	if len(verbs) != 0 {
		t.Fatalf("verbs = %v, want none: an unbound joiner is a question for a human", verbs)
	}
}
