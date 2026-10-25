package tournament

import (
	"sort"
	"strings"
)

// The roles an override can set. `RoleDerived` takes an override back, letting the rule decide.
const (
	RoleGhost      = "ghost"
	RoleContestant = "contestant"
	RoleDerived    = "derived"
)

// Person is somebody on the roster, recorded before they join.
type Person struct {
	// ID is stable across renames, so correcting Name keeps the person in their group.
	ID int64 `json:"id"`

	// Name is the Nitrox name they join with: the only handle the mod has.
	Name string `json:"name"`

	// Label is the display name.
	Label string `json:"label"`

	Notes string `json:"notes"`
}

// Seat is one person in a group.
type Seat struct {
	Person int64  `json:"person"`
	Name   string `json:"name"`
	Label  string `json:"label"`
}

// Override is one exception to the derived role rule, and who wrote it.
type Override struct {
	ID     int64  `json:"id"`
	Person int64  `json:"person"`
	Name   string `json:"name"`
	Role   string `json:"role"`
	Note   string `json:"note"`
	By     string `json:"by"`
	At     string `json:"at"`
}

// Presence is what the mod reports about one connected player.
type Presence struct {
	Name    string
	InMatch bool
}

// Roles is the intended role of every connected player.
type Roles struct {
	// Ghosts is who should be out of the match: everyone connected outside the playing group, adjusted
	// by overrides.
	Ghosts []string

	// Contestants is everyone connected who should be in the match.
	Contestants []string

	// Unbound is everyone connected with no roster entry. They are left alone, never ghosted or
	// unghosted, because only a human can tell a roster typo from an unexpected player.
	Unbound []string
}

// Intended is who should be a ghost while one group is playing: its members are contestants and
// everybody else connected is a ghost, then overrides apply. Names compare case-insensitively,
// because a roster name typed by hand may differ in case from the one the game client reports.
func Intended(connected []Presence, seats []Seat, people []Person, overrides []Override) Roles {
	roles := Roles{Ghosts: []string{}, Contestants: []string{}, Unbound: []string{}}

	known := map[string]Person{}
	for _, person := range people {
		known[fold(person.Name)] = person
	}

	playing := map[string]bool{}
	for _, seat := range seats {
		playing[fold(seat.Name)] = true
	}

	// Rows arrive in write order, so the last override per person wins.
	decided := map[int64]string{}
	for _, override := range overrides {
		decided[override.Person] = override.Role
	}

	for _, present := range connected {
		person, bound := known[fold(present.Name)]
		if !bound {
			roles.Unbound = append(roles.Unbound, present.Name)
			continue
		}

		ghost := !playing[fold(present.Name)]

		switch decided[person.ID] {
		case RoleGhost:
			ghost = true
		case RoleContestant:
			ghost = false
		}

		if ghost {
			roles.Ghosts = append(roles.Ghosts, present.Name)
		} else {
			roles.Contestants = append(roles.Contestants, present.Name)
		}
	}

	// Sorted for a deterministic result.
	sort.Strings(roles.Ghosts)
	sort.Strings(roles.Contestants)
	sort.Strings(roles.Unbound)

	return roles
}

// Reconcile turns the gap between intended roles and what the mod reports into `ghost <name>` and
// `unghost <name>` commands, using the mod's spelling of each name. Ghosts come first, so a
// transition never briefly has extra players in the match.
func Reconcile(intended Roles, reported []Presence) []string {
	ghosts := map[string]bool{}
	for _, name := range intended.Ghosts {
		ghosts[fold(name)] = true
	}

	contestants := map[string]bool{}
	for _, name := range intended.Contestants {
		contestants[fold(name)] = true
	}

	takeOut := []string{}
	putBack := []string{}

	for _, present := range reported {
		switch {
		case present.InMatch && ghosts[fold(present.Name)]:
			takeOut = append(takeOut, "ghost "+present.Name)
		case !present.InMatch && contestants[fold(present.Name)]:
			putBack = append(putBack, "unghost "+present.Name)
		}
	}

	sort.Strings(takeOut)
	sort.Strings(putBack)

	return append(takeOut, putBack...)
}

// fold normalizes a name for comparing a hand-typed name with one the game client reports.
func fold(name string) string {
	return strings.ToLower(strings.TrimSpace(name))
}
