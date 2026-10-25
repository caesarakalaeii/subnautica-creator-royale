package caster

import (
	"fmt"
	"net/http"
	"strconv"
	"strings"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// peoplePath is the roster of people, assigned to groups before anybody joins.
const peoplePath = "/people"

// overridesPath is the exceptions to the derived rule.
const overridesPath = "/overrides"

// reconciler is the `by` recorded for commands the booth queues itself rather than an operator.
const reconciler = "reconciler"

// peopleRequest is the roster, written whole. A row without an id is a new person; a row with one
// is an edit (including a rename), so group membership survives it.
type peopleRequest struct {
	People []tournament.Person `json:"people"`

	// Removing is the operator's confirmation of how many people this write removes. A pointer
	// because absent is not zero: a page that omits it cannot confirm any removal.
	Removing *int `json:"removing"`
}

// setPeople writes the roster.
func (s *Server) setPeople(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, peoplePath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so the roster cannot be kept")

		return
	}

	asked, ok := decodeJSON[peopleRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	people, reason := peopleFrom(asked)
	if reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	// Read before write. The console renders an empty roster when it could not read one, so a
	// write that cannot see what it would replace could wipe every group and override.
	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	held, err := records.People(ctx)
	if err != nil {
		s.logger.Error("could not read the roster before writing it", "error", err.Error())
		writeError(w, http.StatusServiceUnavailable,
			"the roster could not be read, so it was not written — try again once the booth can "+
				"see the database: "+err.Error())

		return
	}

	// 409, not 400: the request is well formed; the server's state is what refuses it.
	gone := removals(held, people)

	switch {
	case len(gone) > 0 && len(gone) == len(held):
		// Removing everyone looks like an outage redraw or a paste without ids, so no `removing`
		// count can confirm it.
		writeError(w, http.StatusConflict, fmt.Sprintf(
			"that takes all %d people off the roster, and their groups and overrides with them, so "+
				"it was refused: check whether the booth could read the database when the page last "+
				"drew, then remove them one at a time or clear the table outside the browser",
			len(held)))

		return
	case len(gone) > 0 && (asked.Removing == nil || *asked.Removing != len(gone)):
		// The count is a confirmation, not a lock; concurrent edits are caught by the store refusing
		// ids that no longer exist.
		writeError(w, http.StatusConflict, fmt.Sprintf(
			"that removes %d from the roster (%s), and their groups and overrides with them — send "+
				"removing: %d with the same write to confirm it", len(gone), listed(gone), len(gone)))

		return
	}

	if err := records.SetPeople(r.Context(), people); err != nil {
		s.logger.Error("could not write the roster", "error", err.Error())
		writeError(w, http.StatusServiceUnavailable,
			"the roster was refused and not kept: "+err.Error())

		return
	}

	s.logger.Info("roster written", "booth", booth.ID(), "people", len(people),
		"removed", len(gone))

	replyJSON(w, http.StatusOK, map[string]any{"written": true, "people": len(people)})
}

func peopleFrom(asked peopleRequest) ([]tournament.Person, string) {
	people := make([]tournament.Person, 0, len(asked.People))
	seen := map[string]bool{}

	for _, person := range asked.People {
		name := strings.TrimSpace(person.Name)
		if name == "" {
			return nil, "everybody needs the Nitrox name they will join with, because that is the " +
				"only handle the mod has on them"
		}

		// Duplicates (case-insensitive, like the schema's index) are refused rather than merged.
		folded := strings.ToLower(name)
		if seen[folded] {
			return nil, "the same Nitrox name is on the roster twice: " + name
		}
		seen[folded] = true

		label := strings.TrimSpace(person.Label)
		if label == "" {
			// The label is display-only, so defaulting it to the name is safe.
			label = name
		}

		people = append(people, tournament.Person{
			ID: person.ID, Name: name, Label: label, Notes: strings.TrimSpace(person.Notes),
		})
	}

	return people, ""
}

// removals is who is on the roster now and not in the write, by label. It mirrors the store's
// `DELETE FROM people WHERE NOT (id = ANY($1))`: a row without an id keeps nobody, so re-pasting
// the same names without ids removes them all.
func removals(held, asked []tournament.Person) []string {
	kept := make(map[int64]bool, len(asked))

	for _, person := range asked {
		if person.ID != 0 {
			kept[person.ID] = true
		}
	}

	gone := []string{}

	for _, person := range held {
		if !kept[person.ID] {
			gone = append(gone, person.Label)
		}
	}

	return gone
}

// listed names them for the refusal, and stops before the sentence becomes a roster.
func listed(names []string) string {
	if len(names) <= namesInARefusal {
		return strings.Join(names, ", ")
	}

	return strings.Join(names[:namesInARefusal], ", ") +
		fmt.Sprintf(" and %d more", len(names)-namesInARefusal)
}

// namesInARefusal is how many people a refusal spells out before it starts counting instead.
const namesInARefusal = 10

// overrideRequest is one exception, and who asked for it.
type overrideRequest struct {
	Person int64  `json:"person"`
	Role   string `json:"role"`
	Note   string `json:"note"`
	By     string `json:"by"`
}

// overrule writes one exception to the derived rule.
func (s *Server) overrule(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, overridesPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so an override cannot be kept")

		return
	}

	asked, ok := decodeJSON[overrideRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	if reason := overrideReason(asked); reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	written, err := records.Override(r.Context(), asked.Person, asked.Role,
		strings.TrimSpace(asked.Note), strings.TrimSpace(asked.By))
	if err != nil {
		s.logger.Error("could not write an override", "error", err.Error(),
			"person", asked.Person, "role", asked.Role)
		writeError(w, http.StatusServiceUnavailable, "the override was refused and not kept")

		return
	}

	s.logger.Info("override", "id", written.ID, "person", written.Person,
		"name", written.Name, "role", written.Role, "by", written.By)

	replyJSON(w, http.StatusCreated, written)
}

func overrideReason(asked overrideRequest) string {
	if asked.Person < 1 {
		return "person has to be the id of somebody on the roster"
	}

	switch asked.Role {
	case tournament.RoleGhost, tournament.RoleContestant, tournament.RoleDerived:
	default:
		return "role has to be one of ghost, contestant, or derived to hand them back to the rule"
	}

	if strings.TrimSpace(asked.By) == "" {
		return "by has to name whoever is saying so, for reading afterwards"
	}

	return ""
}

// intent is the booth's view of who should be what, shown on the console beside what the mod
// reports so a pending reconcile is visible.
type intent struct {
	// Group is the group the mod says is being played, which is what the rule is computed against.
	Group int `json:"group"`

	// Ghosts, Contestants and Unbound are the derived sets over the people actually connected.
	Ghosts      []string `json:"ghosts"`
	Contestants []string `json:"contestants"`
	Unbound     []string `json:"unbound"`

	// Pending is the difference the booth would ask for right now: the verbs it has queued or is
	// about to. Empty when the mod already agrees.
	Pending []string `json:"pending"`

	// Reconciling says whether the booth is driving this at all, and Why says what stopped it.
	Reconciling bool   `json:"reconciling"`
	Why         string `json:"why"`
}

// reconcile compares what the booth intends with what the mod reports, and queues the difference
// as ordinary commands. Both sides re-converge after a restart. A database error skips the
// reconcile but never fails the report.
func (s *Server) reconcile(booth *Booth, r *http.Request, report consoleReport) intent {
	decided := intent{Ghosts: []string{}, Contestants: []string{}, Unbound: []string{}, Pending: []string{}}

	records := s.recordsFor(booth)
	if records == nil {
		decided.Why = "there is no database on this service, so there is no roster to reconcile against"
		return decided
	}

	if !report.AcceptsCommands {
		// Queueing for a server that refuses commands would only fill the table.
		decided.Why = "that server's telemetry.acceptsCommands is false, so it runs nothing from here"
		return decided
	}

	snapshot, pushed := booth.store.Latest()
	if !pushed || snapshot.Match == nil {
		decided.Why = "no match has been pushed, so the booth does not know which group is playing"
		return decided
	}

	decided.Group = snapshot.Match.Group

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	groups, err := records.Groups(ctx)
	if err != nil {
		s.logger.Warn("could not read the bracket to reconcile against", "error", err.Error())
		decided.Why = "the bracket could not be read"

		return decided
	}

	seats := seatsOf(groups, decided.Group)
	if len(seats) == 0 {
		// An empty group is an unfilled bracket; applying the rule would make everyone a ghost.
		decided.Why = "group " + strconv.Itoa(decided.Group) + " has nobody assigned to it, so the rule is not applied"

		return decided
	}

	people, err := records.People(ctx)
	if err != nil {
		s.logger.Warn("could not read the roster to reconcile against", "error", err.Error())
		decided.Why = "the roster could not be read"

		return decided
	}

	overrides, err := records.Overrides(ctx)
	if err != nil {
		s.logger.Warn("could not read the overrides", "error", err.Error())
		decided.Why = "the overrides could not be read"

		return decided
	}

	connected := make([]tournament.Presence, 0, len(report.Connected))
	for _, present := range report.Connected {
		connected = append(connected,
			tournament.Presence{Name: present.Name, InMatch: present.InMatch})
	}

	roles := tournament.Intended(connected, seats, people, overrides)
	decided.Ghosts = roles.Ghosts
	decided.Contestants = roles.Contestants
	decided.Unbound = roles.Unbound
	decided.Reconciling = true

	wanted := tournament.Reconcile(roles, connected)

	// Withdraw stale reconciles first: the intent moves when the bracket or group changes, and an
	// old queued verb would briefly misassign the current group. Only uncollected ones are
	// withdrawn, since a command already handed out may be running.
	if withdrawn, err := records.Withdraw(ctx, reconciler, wanted); err != nil {
		s.logger.Warn("could not withdraw a stale reconcile", "error", err.Error())
	} else if withdrawn > 0 {
		s.logger.Info("withdrew reconciles the rule no longer wants", "commands", withdrawn)
	}

	if len(wanted) == 0 {
		return decided
	}

	outstanding, err := records.Outstanding(ctx)
	if err != nil {
		s.logger.Warn("could not read what is already queued", "error", err.Error())
		decided.Why = "what is already queued could not be read, so nothing was added"

		return decided
	}

	already := map[string]bool{}
	for _, verb := range outstanding {
		already[verb] = true
	}

	for _, verb := range wanted {
		decided.Pending = append(decided.Pending, verb)

		if already[verb] {
			// Already queued and not yet applied; do not queue a duplicate.
			continue
		}

		if _, err := records.Queue(ctx, verb, reconciler); err != nil {
			s.logger.Warn("could not queue a reconcile", "error", err.Error(), "verb", verb)
			break
		}

		s.logger.Info("reconciling", "verb", verb, "group", decided.Group)
	}

	return decided
}

// seatsOf is who is assigned to one group.
func seatsOf(groups []tournament.Group, number int) []tournament.Seat {
	for _, group := range groups {
		if group.Number == number {
			return group.Seats
		}
	}

	return nil
}
