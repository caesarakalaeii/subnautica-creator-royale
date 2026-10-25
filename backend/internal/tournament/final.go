package tournament

import (
	"sort"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// FinalSeries is one row of the final's spec: how many seats one series sends. Rows seat in the
// order written.
type FinalSeries struct {
	Series string `json:"series"`
	Seats  int    `json:"seats"`
}

// FinalSeat is one seat of the final and the series that filled it.
type FinalSeat struct {
	Name   string `json:"name"`
	Series string `json:"series"`
}

// standing is one player's run through one series.
type standing struct {
	name   string
	points int
	wins   int
	best   int
}

// SeatFinal returns who each series in the final's spec seats, in spec order. A series is the
// finished games whose group carries its name, summed from the per-game results the mod pushes, plus
// attributed kills and adjustments in those games. Within a series players rank by points, wins,
// best result, then name. A player already seated by an earlier series is skipped.
func SeatFinal(pushed telemetry.Standings, groups []Group, final []FinalSeries, rows []Attribution,
	adjustments []Adjustment) []FinalSeat {
	seriesOf := map[int]string{}
	for _, group := range groups {
		if group.Series != "" {
			seriesOf[group.Number] = group.Series
		}
	}

	type played struct{ group, generation int }

	seats := []FinalSeat{}
	seated := map[string]bool{}

	for _, spec := range final {
		finished := map[played]bool{}
		finishedGroups := map[int]bool{}
		byName := map[string]*standing{}
		// Keyed like the table, so a kill credited in another case counts for the player; the games
		// come first, so the name shown is the one the game reported.
		entry := func(name string) *standing {
			key := fold(name)
			if byName[key] == nil {
				byName[key] = &standing{name: name}
			}

			return byName[key]
		}

		for _, game := range pushed.Games {
			if seriesOf[game.Group] != spec.Series {
				continue
			}

			finished[played{game.Group, game.Generation}] = true
			finishedGroups[game.Group] = true

			for _, line := range game.Entries {
				row := entry(line.Name)
				row.points += line.Points

				if line.Placement == 1 {
					row.wins++
				}

				if row.best == 0 || line.Placement < row.best {
					row.best = line.Placement
				}
			}
		}

		var counted []Attribution
		for _, row := range rows {
			if finished[played{row.Group, row.Generation}] {
				counted = append(counted, row)
			}
		}

		for name, kills := range Credits(counted) {
			entry(name).points += kills * pushed.PointsPerKill
		}

		// Adjustments count per game, not per attempt, so a replayed game keeps them.
		for _, row := range adjustments {
			if finishedGroups[row.Group] {
				entry(row.Name).points += row.Points
			}
		}

		ranked := make([]*standing, 0, len(byName))
		for _, row := range byName {
			ranked = append(ranked, row)
		}

		sort.Slice(ranked, func(i, j int) bool { return ahead(ranked[i], ranked[j]) })

		taken := 0
		for _, row := range ranked {
			if taken == spec.Seats {
				break
			}

			key := fold(row.name)
			if seated[key] {
				continue
			}

			seated[key] = true
			seats = append(seats, FinalSeat{Name: row.name, Series: spec.Series})
			taken++
		}
	}

	return seats
}

func ahead(left, right *standing) bool {
	if left.points != right.points {
		return left.points > right.points
	}

	if left.wins != right.wins {
		return left.wins > right.wins
	}

	// Zero is a credited killer with no line of their own in the series, which ranks them last.
	if left.best != right.best {
		return right.best == 0 || (left.best != 0 && left.best < right.best)
	}

	return left.name < right.name
}
