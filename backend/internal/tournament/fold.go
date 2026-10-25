// Package tournament holds the backend's durable state: kill attributions, adjustments, the bracket,
// the roster, commands and configs, plus the fold that combines them with the table the mod pushes.
//
// fold.go is pure arithmetic; store.go is the Postgres side. The live path uses neither, so a
// database outage only affects the bracket page and writes.
package tournament

import (
	"maps"
	"slices"
	"sort"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// Attribution is one operator statement about one death: who killed this player in this match.
// Rows are never edited; a correction is another row and the last one wins.
type Attribution struct {
	// ID is the append order, which decides which row wins. Zero until written.
	ID int64

	// Group and Generation name the match explicitly rather than taking whatever group is running
	// now, so a kill can be attributed after its match has ended.
	Group      int
	Generation int

	Victim string

	// Actor is the killer, or empty to say it was nobody's, which is how a kill is taken back.
	Actor string

	At time.Time

	// By is a label the caller supplies; the operator token is shared, so it is not an identity.
	By string
}

// Adjustment is points an operator gives or takes against one game, with a reason, for cases the
// scoring cannot express. Append-only: taking one back is writing its opposite.
type Adjustment struct {
	ID int64

	// Group is the game it is for, which decides the series the points count toward.
	Group int

	Name   string
	Points int
	Reason string
	At     time.Time
	By     string
}

// Adjusted sums the adjustments per player.
func Adjusted(rows []Adjustment) map[string]int {
	sums := map[string]int{}
	for _, row := range rows {
		sums[row.Name] += row.Points
	}

	return sums
}

// Line is one row of the bracket's table: the mod's row with the operators' kills folded in. Kills
// is what the mod read off the deaths; Attributed is what operators credited afterwards.
type Line struct {
	Place         int    `json:"place"`
	Name          string `json:"name"`
	Points        int    `json:"points"`
	Played        int    `json:"played"`
	Wins          int    `json:"wins"`
	BestPlacement int    `json:"best"`
	Kills         int    `json:"kills"`
	Attributed    int    `json:"attributed"`
	Outlasted     int    `json:"outlasted"`

	// Adjusted is the operators' adjustment, already included in Points.
	Adjusted int `json:"adjusted"`
}

// Table is the bracket's standings: the mod's table with attributions and adjustments folded in.
type Table struct {
	// Matches is the mod's count; attributions do not add to it.
	Matches int `json:"matches"`

	PointsPerKill int `json:"pointsPerKill"`

	Entries []Line `json:"entries"`
}

// Credits counts, per player, how many deaths the attributions credit them with. The last row for a
// victim wins, and only the highest generation of each group counts, matching the mod's rule that a
// discarded attempt is void.
func Credits(rows []Attribution) map[string]int {
	// rows must be in write order (by id), which is what "last wins" means.
	current := map[int]int{}
	for _, row := range rows {
		if row.Generation > current[row.Group] {
			current[row.Group] = row.Generation
		}
	}

	type death struct {
		group      int
		generation int
		victim     string
	}

	killer := map[death]string{}
	for _, row := range rows {
		if row.Generation != current[row.Group] {
			continue
		}

		killer[death{row.Group, row.Generation, row.Victim}] = row.Actor
	}

	credits := map[string]int{}
	for death, actor := range killer {
		if actor == "" {
			continue
		}

		// Self-kills never score, matching how the overlays treat a self-attribution.
		if actor == death.victim {
			continue
		}

		credits[actor]++
	}

	return credits
}

// Fold adds the kill credits and adjustments to the table the mod pushed. Every other column is the
// mod's, verbatim. The sort compares points only and is stable, so the mod's tie-break order (wins,
// best result, name) is preserved.
func Fold(pushed telemetry.Standings, credits, adjusted map[string]int) Table {
	table := Table{
		Matches:       pushed.Matches,
		PointsPerKill: pushed.PointsPerKill,
		Entries:       make([]Line, 0, len(pushed.Entries)+len(credits)),
	}

	// Keyed the way the roster compares names, so a credit typed as "DSpear" lands on the "Dspear"
	// the game reported.
	shown := map[string]string{}
	kills := keyed(credits, shown)
	points := keyed(adjusted, shown)

	seen := map[string]bool{}
	for _, row := range pushed.Entries {
		key := fold(row.Name)
		seen[key] = true
		attributed := kills[key]

		table.Entries = append(table.Entries, Line{
			Name:          row.Name,
			Points:        row.Points + attributed*pushed.PointsPerKill + points[key],
			Played:        row.Played,
			Wins:          row.Wins,
			BestPlacement: row.BestPlacement,
			Kills:         row.Kills,
			Attributed:    attributed,
			Outlasted:     row.Outlasted,
			Adjusted:      points[key],
		})
	}

	// Players credited or adjusted who are not in the mod's table yet, since it only counts finished
	// matches.
	newcomers := make([]string, 0, len(shown))
	for key := range shown {
		if !seen[key] {
			newcomers = append(newcomers, key)
		}
	}

	// Sorted for a deterministic order.
	sort.Slice(newcomers, func(i, j int) bool { return shown[newcomers[i]] < shown[newcomers[j]] })
	for _, key := range newcomers {
		table.Entries = append(table.Entries, Line{
			Name:       shown[key],
			Points:     kills[key]*pushed.PointsPerKill + points[key],
			Attributed: kills[key],
			Adjusted:   points[key],
		})
	}

	sort.SliceStable(table.Entries, func(i, j int) bool {
		return table.Entries[i].Points > table.Entries[j].Points
	})

	place(table.Entries)

	return table
}

// keyed sums named counts under the roster's key for each name, and remembers in shown the first
// spelling of each in name order, so a player nobody pushed yet reads the same on every request.
func keyed(named map[string]int, shown map[string]string) map[string]int {
	sums := make(map[string]int, len(named))
	for _, name := range slices.Sorted(maps.Keys(named)) {
		key := fold(name)
		sums[key] += named[name]

		if _, ok := shown[key]; !ok {
			shown[key] = name
		}
	}

	return sums
}

// place numbers the table, sharing a place between rows tied on points, wins and best result, as the
// mod does. The name only orders tied rows; it never separates places.
func place(entries []Line) {
	number := 1

	for i := range entries {
		if i > 0 && !tied(entries[i-1], entries[i]) {
			number = i + 1
		}

		entries[i].Place = number
	}
}

func tied(left, right Line) bool {
	return left.Points == right.Points &&
		left.Wins == right.Wins &&
		left.BestPlacement == right.BestPlacement
}
