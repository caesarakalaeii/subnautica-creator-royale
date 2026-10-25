package tournament

import (
	"slices"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// game is one finished game's results, winner first.
func game(group int, lines ...telemetry.GameLine) telemetry.GameResult {
	return telemetry.GameResult{Group: group, Generation: 1, Entries: lines}
}

func scored(name string, points, placement int) telemetry.GameLine {
	return telemetry.GameLine{Name: name, Points: points, Placement: placement}
}

// eventGroups is a sample bracket: winners semi 1 is games 14-16, winners semi 2 is 18-20, and
// losers game 23 sends its winner.
func eventGroups() []Group {
	groups := []Group{{Number: 13, Name: "Losers bracket · game 1"}}
	for _, number := range []int{14, 15, 16} {
		groups = append(groups, Group{Number: number, Name: "Winners semi 1", Series: "winners semi 1"})
	}

	for _, number := range []int{18, 19, 20} {
		groups = append(groups, Group{Number: number, Name: "Winners semi 2", Series: "winners semi 2"})
	}

	return append(groups, Group{Number: 23, Name: "Losers bracket · game 5", Series: "losers final"})
}

func eventFinal() []FinalSeries {
	return []FinalSeries{
		{Series: "winners semi 1", Seats: 2},
		{Series: "winners semi 2", Seats: 2},
		{Series: "losers final", Seats: 1},
	}
}

func names(seats []FinalSeat) []string {
	seated := make([]string, 0, len(seats))
	for _, seat := range seats {
		seated = append(seated, seat.Name)
	}

	return seated
}

// Five seats: the top two of each winners semi and the losers winner. A game in no series moves nobody.
func TestTheFinalSeatsTheTopOfEachSeriesThatFeedsIt(t *testing.T) {
	standings := telemetry.Standings{PointsPerKill: 2, Games: []telemetry.GameResult{
		game(13, scored("Zed", 50, 1)),
		game(14, scored("Ada", 3, 1), scored("Bea", 2, 2), scored("Cy", 1, 3), scored("Dov", 0, 4)),
		game(15, scored("Cy", 3, 1), scored("Bea", 2, 2), scored("Ada", 1, 3), scored("Dov", 0, 4)),
		game(16, scored("Cy", 3, 1), scored("Dov", 2, 2), scored("Ada", 1, 3), scored("Bea", 0, 4)),
		game(18, scored("Eli", 3, 1), scored("Fay", 2, 2), scored("Gus", 1, 3), scored("Hal", 0, 4)),
		game(19, scored("Eli", 3, 1), scored("Gus", 2, 2), scored("Fay", 1, 3), scored("Hal", 0, 4)),
		game(20, scored("Hal", 3, 1), scored("Gus", 2, 2), scored("Eli", 1, 3), scored("Fay", 0, 4)),
		game(23, scored("Ivo", 2, 1), scored("Zed", 0, 2)),
	}}

	seats := SeatFinal(standings, eventGroups(), eventFinal(), nil, nil)

	if got, want := names(seats), []string{"Cy", "Ada", "Eli", "Gus", "Ivo"}; !slices.Equal(got, want) {
		t.Fatalf("final = %v, want %v", got, want)
	}

	if seats[4].Series != "losers final" {
		t.Errorf("Ivo was seated by %q, want the losers final", seats[4].Series)
	}
}

func TestATieAtTheCutGoesToTheMoreWinsThenTheName(t *testing.T) {
	standings := telemetry.Standings{Games: []telemetry.GameResult{
		game(14, scored("Bea", 3, 1), scored("Ada", 2, 2), scored("Cy", 2, 2)),
		game(15, scored("Ada", 1, 1), scored("Cy", 1, 1), scored("Bea", 0, 3)),
	}}

	final := []FinalSeries{{Series: "winners semi 1", Seats: 2}}
	seats := SeatFinal(standings, eventGroups(), final, nil, nil)

	// All three have three points, one win and a best of first; the name settles it.
	if got, want := names(seats), []string{"Ada", "Bea"}; !slices.Equal(got, want) {
		t.Errorf("final = %v, want %v", got, want)
	}
}

func TestAPlayerSeatedByOneSeriesIsNotSeatedTwice(t *testing.T) {
	standings := telemetry.Standings{Games: []telemetry.GameResult{
		game(14, scored("Ada", 3, 1), scored("Bea", 2, 2)),
		game(23, scored("Ada", 2, 1), scored("Cy", 1, 2)),
	}}

	final := []FinalSeries{{Series: "winners semi 1", Seats: 1}, {Series: "losers final", Seats: 1}}

	if got, want := names(SeatFinal(standings, eventGroups(), final, nil, nil)), []string{"Ada", "Cy"}; !slices.Equal(got, want) {
		t.Errorf("final = %v, want %v", got, want)
	}
}

func TestAnAttributedKillInASeriesGameCanMoveASeat(t *testing.T) {
	standings := telemetry.Standings{PointsPerKill: 2, Games: []telemetry.GameResult{
		game(14, scored("Ada", 3, 1), scored("Bea", 2, 2), scored("Cy", 1, 3)),
	}}

	final := []FinalSeries{{Series: "winners semi 1", Seats: 1}}
	rows := []Attribution{
		attributed(14, 1, "Cy", "Bea"),
		// A kill in a game outside the series pays nothing here.
		attributed(13, 1, "Zed", "Cy"),
	}

	if got := names(SeatFinal(standings, eventGroups(), final, rows, nil)); !slices.Equal(got, []string{"Bea"}) {
		t.Errorf("final = %v, want Bea on 2 + 2 ahead of Ada on 3", got)
	}
}

func TestASeriesNotYetPlayedSeatsNobody(t *testing.T) {
	standings := telemetry.Standings{Games: []telemetry.GameResult{
		game(14, scored("Ada", 3, 1), scored("Bea", 2, 2)),
	}}

	seats := SeatFinal(standings, eventGroups(), eventFinal(), nil, nil)

	if got := names(seats); !slices.Equal(got, []string{"Ada", "Bea"}) {
		t.Errorf("final = %v, want only winners semi 1's two", got)
	}
}

func TestAnAdjustmentInASeriesGameCanMoveASeat(t *testing.T) {
	standings := telemetry.Standings{PointsPerKill: 2, Games: []telemetry.GameResult{
		game(14, scored("Ada", 3, 1), scored("Bea", 2, 2), scored("Cy", 1, 3)),
	}}

	final := []FinalSeries{{Series: "winners semi 1", Seats: 1}}
	adjustments := []Adjustment{
		{Group: 14, Name: "Bea", Points: 2},
		{Group: 5, Name: "Cy", Points: 9},
	}

	if got := names(SeatFinal(standings, eventGroups(), final, nil, adjustments)); !slices.Equal(got, []string{"Bea"}) {
		t.Errorf("final = %v, want Bea on 2 + 2 ahead of Ada on 3", got)
	}
}
