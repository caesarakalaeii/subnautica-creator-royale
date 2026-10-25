package tournament

import (
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// pushed builds a mod table that scores only outlasting, one point per player, with no kills.
func pushed(lines ...telemetry.StandingsLine) telemetry.Standings {
	return telemetry.Standings{Matches: 2, PointsPerKill: 2, Entries: lines}
}

func line(place int, name string, points, wins, best, outlasted int) telemetry.StandingsLine {
	return telemetry.StandingsLine{
		Place: place, Name: name, Points: points, Played: 2,
		Wins: wins, BestPlacement: best, Kills: 0, Outlasted: outlasted,
	}
}

func attributed(group, generation int, victim, actor string) Attribution {
	return Attribution{Group: group, Generation: generation, Victim: victim, Actor: actor}
}

func TestTheLastRowForAVictimIsTheOneThatCounts(t *testing.T) {
	credits := Credits([]Attribution{
		attributed(1, 1, "Bea", "Ada"),
		attributed(1, 1, "Bea", "Cy"),
	})

	if credits["Ada"] != 0 {
		t.Errorf("Ada = %d kill(s), want 0 — the second row moved the kill", credits["Ada"])
	}

	if credits["Cy"] != 1 {
		t.Errorf("Cy = %d kill(s), want 1", credits["Cy"])
	}
}

func TestAKillCanBeTakenBackByNamingNobody(t *testing.T) {
	credits := Credits([]Attribution{
		attributed(1, 1, "Bea", "Ada"),
		attributed(1, 1, "Bea", ""),
	})

	if len(credits) != 0 {
		t.Errorf("credits = %v, want nobody credited", credits)
	}
}

func TestTheSameVictimInTwoMatchesIsTwoKills(t *testing.T) {
	credits := Credits([]Attribution{
		attributed(1, 1, "Bea", "Ada"),
		attributed(4, 1, "Bea", "Ada"),
	})

	if credits["Ada"] != 2 {
		t.Errorf("Ada = %d kill(s), want 2 — different matches", credits["Ada"])
	}
}

func TestAnAbandonedAttemptsAttributionsAreDropped(t *testing.T) {
	credits := Credits([]Attribution{
		attributed(2, 1, "Bea", "Ada"),
		attributed(2, 2, "Bea", "Cy"),
	})

	if credits["Ada"] != 0 {
		t.Errorf("Ada = %d kill(s), want 0 — generation 1 was replayed", credits["Ada"])
	}

	if credits["Cy"] != 1 {
		t.Errorf("Cy = %d kill(s), want 1", credits["Cy"])
	}
}

func TestTheFoldAddsKillPointsAndLeavesOutlastingAlone(t *testing.T) {
	table := Fold(pushed(line(1, "Vela", 5, 1, 1, 5)), map[string]int{"Vela": 2}, nil)

	if len(table.Entries) != 1 {
		t.Fatalf("entries = %d, want 1", len(table.Entries))
	}

	row := table.Entries[0]
	if row.Points != 9 {
		t.Errorf("points = %d, want 9 — five outlasted and two kills at two", row.Points)
	}

	if row.Outlasted != 5 {
		t.Errorf("outlasted = %d, want the mod's 5", row.Outlasted)
	}

	if row.Attributed != 2 || row.Kills != 0 {
		t.Errorf("kills = %d off the log and %d attributed; want 0 and 2",
			row.Kills, row.Attributed)
	}
}

func TestTheFoldPaysWhatTheTableSaysAKillPays(t *testing.T) {
	table := pushed(line(1, "Vela", 5, 1, 1, 5))
	table.PointsPerKill = 3

	if got := Fold(table, map[string]int{"Vela": 2}, nil).Entries[0].Points; got != 11 {
		t.Errorf("points = %d, want 11 — two kills at the table's 3", got)
	}
}

func TestKillPointsReorderTheTableAndNothingElseIsResorted(t *testing.T) {
	table := Fold(pushed(
		line(1, "Vela", 6, 1, 1, 6),
		line(2, "Rook", 5, 0, 2, 5),
		line(3, "Tess", 5, 0, 3, 5),
	), map[string]int{"Tess": 2}, nil)

	names := []string{}
	for _, row := range table.Entries {
		names = append(names, row.Name)
	}

	if len(names) != 3 || names[0] != "Tess" || names[1] != "Vela" || names[2] != "Rook" {
		t.Errorf("order = %v, want Tess (9), Vela (6), Rook (5)", names)
	}
}

func TestPlayersTheTableCannotSeparateShareAPlace(t *testing.T) {
	table := Fold(pushed(
		line(1, "Ada", 5, 1, 1, 5),
		line(2, "Bea", 5, 1, 1, 5),
		line(3, "Cy", 4, 0, 2, 4),
	), nil, nil)

	places := []int{}
	for _, row := range table.Entries {
		places = append(places, row.Place)
	}

	if len(places) != 3 || places[0] != 1 || places[1] != 1 || places[2] != 3 {
		t.Errorf("places = %v, want 1, 1, 3", places)
	}
}

func TestEqualPointsWithADifferentRecordIsNotATie(t *testing.T) {
	table := Fold(pushed(
		line(1, "Ada", 5, 1, 1, 5),
		line(2, "Bea", 5, 0, 2, 5),
	), nil, nil)

	if table.Entries[1].Place != 2 {
		t.Errorf("second place = %d, want 2 — one win against none is a difference",
			table.Entries[1].Place)
	}
}

func TestAKillerWhoHasNotFinishedAMatchStillAppears(t *testing.T) {
	table := Fold(pushed(line(1, "Vela", 5, 1, 1, 5)), map[string]int{"Newcomer": 1}, nil)

	if len(table.Entries) != 2 {
		t.Fatalf("entries = %d, want 2", len(table.Entries))
	}

	row := table.Entries[1]
	if row.Name != "Newcomer" || row.Points != 2 || row.Played != 0 {
		t.Errorf("row = %+v, want Newcomer on 2 points from no finished match", row)
	}
}

func TestTheFoldDoesNotChangeHowManyMatchesTheTableCounts(t *testing.T) {
	if got := Fold(pushed(), map[string]int{"Ada": 1}, nil).Matches; got != 2 {
		t.Errorf("matches = %d, want the mod's 2", got)
	}
}

func TestNobodyIsPaidForKillingThemselves(t *testing.T) {
	credits := Credits([]Attribution{attributed(1, 1, "Tess", "Tess")})

	if len(credits) != 0 {
		t.Errorf("credits = %v, want nobody credited", credits)
	}
}

func TestAnAdjustmentMovesThePointsAndSaysSo(t *testing.T) {
	table := Fold(pushed(line(1, "Vela", 5, 1, 1, 5)), nil, Adjusted([]Adjustment{
		{Group: 5, Name: "Vela", Points: 1},
		{Group: 6, Name: "Vela", Points: 2},
	}))

	row := table.Entries[0]
	if row.Points != 8 || row.Adjusted != 3 {
		t.Errorf("row = %d points with %d adjusted, want 8 with 3", row.Points, row.Adjusted)
	}

	if row.Outlasted != 5 || row.Attributed != 0 {
		t.Errorf("row = %+v, want the mod's columns untouched", row)
	}
}

func TestAnAdjustmentIsTakenBackByItsOpposite(t *testing.T) {
	table := Fold(pushed(line(1, "Vela", 5, 1, 1, 5)), nil, Adjusted([]Adjustment{
		{Group: 5, Name: "Vela", Points: 1},
		{Group: 5, Name: "Vela", Points: -1},
	}))

	if row := table.Entries[0]; row.Points != 5 || row.Adjusted != 0 {
		t.Errorf("row = %d points with %d adjusted, want 5 with 0", row.Points, row.Adjusted)
	}
}

func TestAnAdjustmentCanReorderTheTable(t *testing.T) {
	table := Fold(pushed(line(1, "Vela", 5, 1, 1, 5), line(2, "Rook", 4, 0, 2, 4)), nil,
		Adjusted([]Adjustment{{Group: 1, Name: "Rook", Points: 2}}))

	if table.Entries[0].Name != "Rook" || table.Entries[0].Points != 6 {
		t.Errorf("leader = %+v, want Rook on 6", table.Entries[0])
	}
}
