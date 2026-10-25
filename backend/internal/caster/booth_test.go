package caster

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"slices"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

const operatorToken = "the-operators-secret"

// fakeRecords is an in-memory store of record for testing the routes without a database.
type fakeRecords struct {
	rows        []tournament.Attribution
	adjustments []tournament.Adjustment
	groups      []tournament.Group
	final       []tournament.FinalSeries

	// The command queue, in write order.
	commands []tournament.Command
	expired  int

	// The roster and its overrides.
	people    []tournament.Person
	overrides []tournament.Override

	// The tournament's rules, newest last.
	configs []tournament.Config

	// broken makes every call fail, as if the database had gone away.
	broken bool

	next int64
}

var errBroken = errors.New("the database has gone away")

// everyBooth hands the same rows to every booth, as a single-booth deployment does. Use perBooth
// to observe which booth a handler asked for.
func everyBooth(records Records) RecordsFor {
	return func(string) Records { return records }
}

// perBooth gives each booth its own rows, so a test can check writes land in the right booth.
func perBooth() (RecordsFor, func(booth string) *fakeRecords) {
	rows := map[string]*fakeRecords{}

	rowsFor := func(booth string) *fakeRecords {
		if _, ok := rows[booth]; !ok {
			rows[booth] = &fakeRecords{}
		}

		return rows[booth]
	}

	return func(booth string) Records { return rowsFor(booth) }, rowsFor
}

func (f *fakeRecords) Add(_ context.Context, row tournament.Attribution) (tournament.Attribution, error) {
	if f.broken {
		return tournament.Attribution{}, errBroken
	}

	f.next++
	row.ID = f.next
	row.At = time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)
	f.rows = append(f.rows, row)

	return row, nil
}

func (f *fakeRecords) Attributions(context.Context) ([]tournament.Attribution, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.rows, nil
}

func (f *fakeRecords) AddAdjustment(_ context.Context, row tournament.Adjustment) (tournament.Adjustment, error) {
	if f.broken {
		return tournament.Adjustment{}, errBroken
	}

	f.next++
	row.ID = f.next
	row.At = time.Date(2026, 10, 25, 12, 0, 0, 0, time.UTC)
	f.adjustments = append(f.adjustments, row)

	return row, nil
}

func (f *fakeRecords) Adjustments(context.Context) ([]tournament.Adjustment, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.adjustments, nil
}

func (f *fakeRecords) Groups(context.Context) ([]tournament.Group, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.groups, nil
}

func (f *fakeRecords) Final(context.Context) ([]tournament.FinalSeries, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.final, nil
}

func (f *fakeRecords) SetGroups(_ context.Context, groups []tournament.Group, final []tournament.FinalSeries) error {
	if f.broken {
		return errBroken
	}

	f.groups = groups
	f.final = final

	return nil
}

func (f *fakeRecords) Queue(_ context.Context, verb, by string) (tournament.Command, error) {
	if f.broken {
		return tournament.Command{}, errBroken
	}

	f.next++
	row := tournament.Command{
		ID: f.next, Verb: verb, By: by, State: tournament.StateQueued,
		// Stamped now, as the database's now() does; a fixed date would expire once past the TTL.
		QueuedAt: time.Now().UTC(),
	}
	f.commands = append(f.commands, row)

	return row, nil
}

func (f *fakeRecords) Expire(_ context.Context, before time.Time) (int, error) {
	if f.broken {
		return 0, errBroken
	}

	gone := 0
	for i := range f.commands {
		row := &f.commands[i]
		if (row.State == tournament.StateQueued || row.State == tournament.StateSent) &&
			row.QueuedAt.Before(before) {
			row.State = tournament.StateExpired
			gone++
		}
	}

	f.expired += gone

	return gone, nil
}

func (f *fakeRecords) Owed(_ context.Context, above int64) ([]tournament.Command, error) {
	if f.broken {
		return nil, errBroken
	}

	owed := []tournament.Command{}
	for _, row := range f.commands {
		if row.ID <= above {
			continue
		}

		if row.State != tournament.StateQueued && row.State != tournament.StateSent {
			continue
		}

		owed = append(owed, row)
		if len(owed) == tournament.MaxCommandsPerReply {
			break
		}
	}

	return owed, nil
}

func (f *fakeRecords) MarkSent(_ context.Context, ids []int64, at time.Time) error {
	if f.broken {
		return errBroken
	}

	for _, id := range ids {
		for i := range f.commands {
			row := &f.commands[i]
			if row.ID != id || row.State != tournament.StateQueued {
				continue
			}

			row.State = tournament.StateSent
			if row.SentAt == nil {
				stamp := at
				row.SentAt = &stamp
			}
		}
	}

	return nil
}

func (f *fakeRecords) MarkRan(_ context.Context, id int64, answer string, at time.Time) error {
	if f.broken {
		return errBroken
	}

	for i := range f.commands {
		row := &f.commands[i]
		if row.ID != id || row.State == tournament.StateRan {
			continue
		}

		row.State = tournament.StateRan
		row.Answer = answer
		stamp := at
		row.RanAt = &stamp
	}

	return nil
}

func (f *fakeRecords) Outstanding(context.Context) ([]string, error) {
	if f.broken {
		return nil, errBroken
	}

	verbs := []string{}
	for _, row := range f.commands {
		if row.State == tournament.StateQueued || row.State == tournament.StateSent {
			verbs = append(verbs, row.Verb)
		}
	}

	return verbs, nil
}

func (f *fakeRecords) Withdraw(_ context.Context, by string, keep []string) (int, error) {
	if f.broken {
		return 0, errBroken
	}

	wanted := map[string]bool{}
	for _, verb := range keep {
		wanted[verb] = true
	}

	withdrawn := 0
	for i := range f.commands {
		row := &f.commands[i]
		if row.State == tournament.StateQueued && row.By == by && !wanted[row.Verb] {
			row.State = tournament.StateExpired
			withdrawn++
		}
	}

	return withdrawn, nil
}

func (f *fakeRecords) People(context.Context) ([]tournament.Person, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.people, nil
}

func (f *fakeRecords) SetPeople(_ context.Context, people []tournament.Person) error {
	if f.broken {
		return errBroken
	}

	kept := make([]tournament.Person, 0, len(people))
	for _, person := range people {
		if person.ID == 0 {
			f.next++
			person.ID = f.next
		}

		kept = append(kept, person)
	}

	f.people = kept

	return nil
}

func (f *fakeRecords) Overrides(context.Context) ([]tournament.Override, error) {
	if f.broken {
		return nil, errBroken
	}

	return f.overrides, nil
}

func (f *fakeRecords) Override(_ context.Context, person int64, role, note, by string) (tournament.Override, error) {
	if f.broken {
		return tournament.Override{}, errBroken
	}

	f.next++
	written := tournament.Override{
		ID: f.next, Person: person, Role: role, Note: note, By: by,
		At: "2026-10-24T12:00:00.000Z",
	}

	for _, one := range f.people {
		if one.ID == person {
			written.Name = one.Name
		}
	}

	f.overrides = append(f.overrides, written)

	return written, nil
}

func (f *fakeRecords) WriteConfig(_ context.Context, yaml, by, note string) (tournament.Config, error) {
	if f.broken {
		return tournament.Config{}, errBroken
	}

	// Supersede pending configs, like the real store.
	for i := range f.configs {
		switch f.configs[i].State {
		case tournament.ConfigQueued, tournament.ConfigSent, tournament.ConfigWaiting:
			f.configs[i].State = tournament.ConfigSuperseded
		}
	}

	f.next++
	row := tournament.Config{
		Revision: f.next, Yaml: yaml, By: by, Note: note,
		State: tournament.ConfigQueued, Problems: []string{},
		At: time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC),
	}
	f.configs = append(f.configs, row)

	return row, nil
}

func (f *fakeRecords) NewestConfig(context.Context) (tournament.Config, bool, error) {
	if f.broken {
		return tournament.Config{}, false, errBroken
	}

	if len(f.configs) == 0 {
		return tournament.Config{}, false, nil
	}

	return f.configs[len(f.configs)-1], true, nil
}

func (f *fakeRecords) ConfigOwed(_ context.Context, running int64) (tournament.Config, bool, error) {
	if f.broken {
		return tournament.Config{}, false, errBroken
	}

	if len(f.configs) == 0 {
		return tournament.Config{}, false, nil
	}

	// Only the newest row counts, whatever its state, so a refused edit never falls back to an
	// older pending one.
	row := f.configs[len(f.configs)-1]
	if row.Revision <= running {
		return tournament.Config{}, false, nil
	}

	switch row.State {
	case tournament.ConfigQueued, tournament.ConfigSent, tournament.ConfigWaiting:
		return row, true, nil
	default:
		return tournament.Config{}, false, nil
	}
}

func (f *fakeRecords) ConfigSentTo(_ context.Context, revision int64, at time.Time) error {
	if f.broken {
		return errBroken
	}

	for i := range f.configs {
		row := &f.configs[i]
		if row.Revision == revision && row.State == tournament.ConfigQueued {
			row.State = tournament.ConfigSent
			if row.SentAt == nil {
				stamp := at
				row.SentAt = &stamp
			}
		}
	}

	return nil
}

func (f *fakeRecords) ConfigSettled(_ context.Context, revision int64, adopted, final bool,
	answer string, problems []string, at time.Time) error {
	if f.broken {
		return errBroken
	}

	state := tournament.ConfigWaiting
	switch {
	case adopted:
		state = tournament.ConfigAdopted
	case final:
		state = tournament.ConfigRefused
	}

	for i := range f.configs {
		row := &f.configs[i]
		if row.Revision != revision || row.State == tournament.ConfigAdopted {
			continue
		}

		row.State = state
		row.Answer = answer
		row.Problems = problems
		stamp := at
		row.SettledAt = &stamp
	}

	return nil
}

func (f *fakeRecords) Configs(_ context.Context, limit int) ([]tournament.Config, error) {
	if f.broken {
		return nil, errBroken
	}

	newest := []tournament.Config{}
	for i := len(f.configs) - 1; i >= 0 && len(newest) < limit; i-- {
		row := f.configs[i]
		row.Yaml = ""
		newest = append(newest, row)
	}

	return newest, nil
}

// ConfigAt returns a revision with its document, unlike Configs, which omits it.
func (f *fakeRecords) ConfigAt(_ context.Context, revision int64) (tournament.Config, bool, error) {
	if f.broken {
		return tournament.Config{}, false, errBroken
	}

	for _, row := range f.configs {
		if row.Revision == revision {
			return row, true, nil
		}
	}

	return tournament.Config{}, false, nil
}

func (f *fakeRecords) Commands(_ context.Context, limit int) ([]tournament.Command, error) {
	if f.broken {
		return nil, errBroken
	}

	newest := []tournament.Command{}
	for i := len(f.commands) - 1; i >= 0 && len(newest) < limit; i-- {
		newest = append(newest, f.commands[i])
	}

	return newest, nil
}

func boothServer(t *testing.T, records Records) *Server {
	t.Helper()

	cfg := config.Config{
		Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths:     oneBooth(token, "watching", operatorToken),
		IngestRate: 20, FinalSeats: 5, CommandTTL: 2 * time.Minute,
	}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithRecords(everyBooth(records))
}

func ask(t *testing.T, server *Server, method, path, body, bearer string) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(method, path, strings.NewReader(body))
	if bearer != "" {
		request.Header.Set("Authorization", "Bearer "+bearer)
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	return recorder
}

func reason(t *testing.T, recorder *httptest.ResponseRecorder) string {
	t.Helper()

	var body struct {
		Error string `json:"error"`
	}

	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("reply is not JSON: %v (%q)", err, recorder.Body.String())
	}

	return body.Error
}

func readBracket(t *testing.T, recorder *httptest.ResponseRecorder) bracketResponse {
	t.Helper()

	var body bracketResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("bracket is not JSON: %v (%q)", err, recorder.Body.String())
	}

	return body
}

// pushedTable is a snapshot with standings for two finished matches and no logged kills, as the
// mod pushes it.
func pushedTable() telemetry.Snapshot {
	return telemetry.Snapshot{
		Version:           telemetry.Version,
		StandingsRevision: 1,
		Standings: &telemetry.Standings{
			Matches: 2, PointsPerKill: 2,
			Entries: []telemetry.StandingsLine{
				{Place: 1, Name: "Vela", Points: 6, Played: 2, Wins: 1, BestPlacement: 1, Outlasted: 6},
				{Place: 2, Name: "Rook", Points: 4, Played: 2, BestPlacement: 2, Outlasted: 4},
			},
		},
	}
}

const oneAttribution = `{"group":1,"generation":1,"victim":"Rook","actor":"Vela","by":"ops"}`

func TestAnAttributionIsRefusedWithoutTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, presented := range []string{"", token, "watching", "wrong"} {
		recorder := ask(t, server, http.MethodPost, attributionsPath, oneAttribution, presented)

		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestAnAttributionIsWrittenWithTheOperatorToken(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	recorder := ask(t, boothServer(t, records), http.MethodPost, attributionsPath,
		oneAttribution, operatorToken)

	if recorder.Code != http.StatusCreated {
		t.Fatalf("status = %d (%s), want 201", recorder.Code, recorder.Body.String())
	}

	if len(records.rows) != 1 {
		t.Fatalf("rows = %d, want 1", len(records.rows))
	}

	row := records.rows[0]
	if row.Group != 1 || row.Generation != 1 || row.Victim != "Rook" || row.Actor != "Vela" {
		t.Errorf("row = %+v, want group 1 generation 1, Vela credited with Rook", row)
	}

	if row.By != "ops" {
		t.Errorf("by = %q, want the operator's own label", row.By)
	}
}

func TestAnAttributionNamesItsOwnMatch(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	body := `{"group":1,"generation":2,"victim":"Rook","actor":"Vela","by":"ops"}`

	if recorder := ask(t, boothServer(t, records), http.MethodPost, attributionsPath,
		body, operatorToken); recorder.Code != http.StatusCreated {
		t.Fatalf("status = %d (%s), want 201", recorder.Code, recorder.Body.String())
	}

	if records.rows[0].Group != 1 || records.rows[0].Generation != 2 {
		t.Errorf("row = group %d generation %d, want 1 and 2 as sent",
			records.rows[0].Group, records.rows[0].Generation)
	}
}

func TestNamingNobodyTakesAKillBack(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	body := `{"group":1,"generation":1,"victim":"Rook","actor":null,"by":"ops"}`

	if recorder := ask(t, boothServer(t, records), http.MethodPost, attributionsPath,
		body, operatorToken); recorder.Code != http.StatusCreated {
		t.Fatalf("status = %d (%s), want 201", recorder.Code, recorder.Body.String())
	}

	if records.rows[0].Actor != "" {
		t.Errorf("actor = %q, want nobody", records.rows[0].Actor)
	}
}

func TestABadAttributionIsRefusedWithAReason(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	cases := map[string]struct {
		body  string
		names string
	}{
		"no group":      {`{"generation":1,"victim":"Rook","by":"c"}`, "group"},
		"no generation": {`{"group":1,"victim":"Rook","by":"c"}`, "generation"},
		"no victim":     {`{"group":1,"generation":1,"by":"c"}`, "victim"},
		"nobody saying": {`{"group":1,"generation":1,"victim":"Rook"}`, "by"},
		"killing themselves": {
			`{"group":1,"generation":1,"victim":"Rook","actor":"Rook","by":"c"}`, "same player"},
		"not json": {`{`, "JSON"},
	}

	for name, given := range cases {
		recorder := ask(t, server, http.MethodPost, attributionsPath, given.body, operatorToken)

		if recorder.Code != http.StatusBadRequest {
			t.Errorf("%s: status = %d, want 400", name, recorder.Code)
			continue
		}

		if !strings.Contains(reason(t, recorder), given.names) {
			t.Errorf("%s: reason = %q, want it to mention %q", name, reason(t, recorder), given.names)
		}
	}
}

func TestAnAttributionIsRefusedWhenThereIsNoDatabase(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, nil), http.MethodPost, attributionsPath,
		oneAttribution, operatorToken)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503", recorder.Code)
	}

	if !strings.Contains(reason(t, recorder), "no database") {
		t.Errorf("reason = %q, want it to say there is no database", reason(t, recorder))
	}
}

func TestAnAttributionIsRefusedWithAReasonWhenTheDatabaseIsUnreachable(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, &fakeRecords{broken: true}), http.MethodPost,
		attributionsPath, oneAttribution, operatorToken)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503", recorder.Code)
	}

	if !strings.Contains(reason(t, recorder), "not kept") {
		t.Errorf("reason = %q, want it to say the attribution was not kept", reason(t, recorder))
	}
}

func TestTheBracketIsServedBehindTheViewToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	if recorder := ask(t, server, http.MethodGet, bracketPath, "", ""); recorder.Code != http.StatusUnauthorized {
		t.Errorf("the bracket was served with no token: %d", recorder.Code)
	}

	recorder := ask(t, server, http.MethodGet, bracketPath, "", "watching")
	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}
}

func TestTheBracketSaysWhenNoViewTokenIsConfigured(t *testing.T) {
	t.Parallel()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, "", operatorToken), IngestRate: 20, FinalSeats: 5}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).
		WithRecords(everyBooth(&fakeRecords{}))

	recorder := ask(t, server, http.MethodGet, bracketPath, "", "anything")

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503", recorder.Code)
	}

	if got := reason(t, recorder); !strings.Contains(got, "view token") ||
		!strings.Contains(got, "not configured") {
		t.Errorf("reason = %q, want it to name the view token as unconfigured", got)
	}
}

func TestTheBracketCarriesNoPositionsAndNoLiveMatch(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	snapshot := pushedTable()
	snapshot.Match = &telemetry.Match{Group: 3, Generation: 1, State: "Running", Alive: 4}
	snapshot.Players = []telemetry.Player{{Name: "Vela", Alive: true, X: 123.456, Y: -42, Z: 88.9}}
	server.Store().Apply(snapshot)

	body := ask(t, server, http.MethodGet, bracketPath, "", "watching").Body.String()

	for _, forbidden := range []string{"123.456", "88.9", "players", "alive", "insideZone", "Running"} {
		if strings.Contains(body, forbidden) {
			t.Errorf("bracket carries %q:\n%s", forbidden, body)
		}
	}
}

func TestTheBracketFoldsAttributionsOntoThePushedTable(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	server.Store().Apply(pushedTable())

	if recorder := ask(t, server, http.MethodPost, attributionsPath, oneAttribution,
		operatorToken); recorder.Code != http.StatusCreated {
		t.Fatalf("the attribution was refused: %s", recorder.Body.String())
	}

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))
	if bracket.Standings == nil {
		t.Fatal("standings is nil, want the folded table")
	}

	if bracket.Attributions != 1 {
		t.Errorf("attributions = %d, want 1", bracket.Attributions)
	}

	leader := bracket.Standings.Entries[0]
	if leader.Name != "Vela" || leader.Points != 8 || leader.Attributed != 1 {
		t.Errorf("leader = %+v, want Vela on 8 with one attributed kill", leader)
	}

	if leader.Outlasted != 6 {
		t.Errorf("outlasted = %d, want the mod's 6 untouched", leader.Outlasted)
	}
}

func TestTheBracketServesWithoutATableBeforeTheModHasPushed(t *testing.T) {
	t.Parallel()

	bracket := readBracket(t, ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "watching"))

	if bracket.Standings != nil {
		t.Errorf("standings = %+v, want nil before anything is pushed", bracket.Standings)
	}

	if bracket.Records != recordsOK {
		t.Errorf("records = %q, want %q", bracket.Records, recordsOK)
	}
}

func TestTheBracketSaysWhenTheDatabaseCouldNotBeRead(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{broken: true})
	server.Store().Apply(pushedTable())

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))

	if bracket.Records != recordsUnreachable {
		t.Errorf("records = %q, want %q", bracket.Records, recordsUnreachable)
	}

	// Degraded, not failed: the mod's table is still served without the kill columns.
	if bracket.Standings == nil || bracket.Standings.Entries[0].Points != 6 {
		t.Errorf("standings = %+v, want the mod's table unfolded", bracket.Standings)
	}
}

func TestABoothWithNoDatabaseSaysSoOnTheBracket(t *testing.T) {
	t.Parallel()

	bracket := readBracket(t, ask(t, boothServer(t, nil), http.MethodGet, bracketPath, "", "watching"))

	if bracket.Records != recordsNone {
		t.Errorf("records = %q, want %q", bracket.Records, recordsNone)
	}
}

func TestTheFinalsSeatsAreComputedFromTheTable(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	server.Store().Apply(pushedTable())

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))

	if bracket.Final.Seats != 5 {
		t.Errorf("seats = %d, want the configured 5", bracket.Final.Seats)
	}

	// Two players in the table, five seats: seats fill as the tournament is played.
	if len(bracket.Final.Seated) != 2 || bracket.Final.Seated[0] != "Vela" {
		t.Errorf("final = %v, want the top of the table", bracket.Final.Seated)
	}
}

func TestTheBracketsShapeIsWrittenBehindTheOperatorToken(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	body := `{"groups":[{"number":1,"name":"Group 1","members":["Vela","Rook"]}]}`

	if recorder := ask(t, server, http.MethodPut, groupsPath, body, "watching"); recorder.Code != http.StatusUnauthorized {
		t.Errorf("the view token wrote the bracket: %d", recorder.Code)
	}

	if recorder := ask(t, server, http.MethodPut, groupsPath, body, operatorToken); recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))
	if len(bracket.Groups) != 1 || len(bracket.Groups[0].Members) != 2 {
		t.Fatalf("groups = %+v, want one group of two", bracket.Groups)
	}

	// The operator's order is kept, not sorted.
	if bracket.Groups[0].Members[0] != "Vela" {
		t.Errorf("members = %v, want the order they were written in", bracket.Groups[0].Members)
	}
}

func TestABadBracketIsRefusedWithAReason(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	cases := map[string]string{
		"no number":     `{"groups":[{"name":"Group 1","members":[]}]}`,
		"no name":       `{"groups":[{"number":1,"members":[]}]}`,
		"shared number": `{"groups":[{"number":1,"name":"a"},{"number":1,"name":"b"}]}`,
		"same player twice": `{"groups":[{"number":1,"name":"Group 1",` +
			`"members":["Vela","Vela"]}]}`,
		"final names a series no group carries": `{"groups":[{"number":14,"name":"Semi","series":"winners semi 1"}],` +
			`"final":[{"series":"winners semi 2","seats":2}]}`,
		"final names a series twice": `{"groups":[{"number":14,"name":"Semi","series":"winners semi 1"}],` +
			`"final":[{"series":"winners semi 1","seats":1},{"series":"winners semi 1","seats":1}]}`,
		"a series that sends nobody": `{"groups":[{"number":14,"name":"Semi","series":"winners semi 1"}],` +
			`"final":[{"series":"winners semi 1","seats":0}]}`,
	}

	for name, body := range cases {
		if recorder := ask(t, server, http.MethodPut, groupsPath, body, operatorToken); recorder.Code != http.StatusBadRequest {
			t.Errorf("%s: status = %d, want 400", name, recorder.Code)
		}
	}
}

func TestAFinalSpecSeatsTheTopOfEachSeriesRatherThanTheTable(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	body := `{"groups":[` +
		`{"number":1,"name":"Group 1 · game 1","members":["Vela","Rook"]},` +
		`{"number":14,"name":"Winners semi 1 · game 1","series":"winners semi 1","members":["Vela","Rook"]},` +
		`{"number":23,"name":"Losers bracket · game 5","series":"losers final","members":["Tess"]}],` +
		`"final":[{"series":"winners semi 1","seats":1},{"series":"losers final","seats":1}]}`

	if recorder := ask(t, server, http.MethodPut, groupsPath, body, operatorToken); recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}

	// Vela leads the whole table on game 1, but Rook won the semi, which is what seats.
	snapshot := pushedTable()
	snapshot.Standings.Games = []telemetry.GameResult{
		{Group: 1, Generation: 1, Entries: []telemetry.GameLine{{Name: "Vela", Points: 6, Placement: 1}}},
		{Group: 14, Generation: 1, Entries: []telemetry.GameLine{
			{Name: "Rook", Points: 4, Placement: 1}, {Name: "Vela", Points: 0, Placement: 2},
		}},
		{Group: 23, Generation: 1, Entries: []telemetry.GameLine{{Name: "Tess", Points: 1, Placement: 1}}},
	}
	server.Store().Apply(snapshot)

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))

	if bracket.Final.Seats != 2 {
		t.Errorf("seats = %d, want the spec's 2 rather than LMS_FINAL_SEATS", bracket.Final.Seats)
	}

	if !slices.Equal(bracket.Final.Seated, []string{"Rook", "Tess"}) {
		t.Fatalf("final = %v, want Rook from the semi and Tess from the losers final", bracket.Final.Seated)
	}

	if len(bracket.Final.From) != 2 || bracket.Final.From[1].Series != "losers final" {
		t.Errorf("from = %+v, want each seat with the series that filled it", bracket.Final.From)
	}
}

func TestTheBoothsRoutesAnswerAWrongVerb(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	cases := map[string]string{
		attributionsPath: http.MethodGet,
		adjustmentsPath:  http.MethodGet,
		groupsPath:       http.MethodPost,
		bracketPath:      http.MethodDelete,
		rulesPath + "/1": http.MethodDelete,
	}

	for path, method := range cases {
		recorder := ask(t, server, method, path, "", operatorToken)

		if recorder.Code != http.StatusMethodNotAllowed {
			t.Errorf("%s %s: status = %d, want 405", method, path, recorder.Code)
		}

		if recorder.Header().Get("Allow") == "" {
			t.Errorf("%s %s: no Allow header", method, path)
		}

		// JSON envelope, since the console reads `error` off the body.
		if got := recorder.Header().Get("Content-Type"); !strings.HasPrefix(got, "application/json") {
			t.Errorf("%s %s: content-type = %q, want this service's own refusal", method, path, got)
		}
	}
}

func TestWritingIsOffRatherThanOpenWithNoOperatorToken(t *testing.T) {
	t.Parallel()

	// A view token but no operator token: reads work, writes are off.
	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth("", "watching", ""), IngestRate: 20, FinalSeats: 5}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).
		WithRecords(everyBooth(&fakeRecords{}))

	recorder := ask(t, server, http.MethodPost, attributionsPath, oneAttribution, "anything")
	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503", recorder.Code)
	}

	if !strings.Contains(reason(t, recorder), "not configured") {
		t.Errorf("reason = %q, want it to say writing is not configured", reason(t, recorder))
	}

	// The read side is unaffected.
	if recorder := ask(t, server, http.MethodGet, bracketPath, "", "watching"); recorder.Code != http.StatusOK {
		t.Errorf("the bracket needs the operator token: %d", recorder.Code)
	}
}

func TestWritesAreRateLimitedAfterTheTokenIsChecked(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	// The bucket is writeRate a second with two seconds of burst, so this is well past it.
	limited := false
	for i := 0; i < int(writeRate*burstWindow.Seconds())+5; i++ {
		if ask(t, server, http.MethodPost, attributionsPath, oneAttribution,
			operatorToken).Code == http.StatusTooManyRequests {
			limited = true
			break
		}
	}

	if !limited {
		t.Fatal("the write bucket never refused anything")
	}

	// A wrong token still gets 401, not 429, so a guesser learns nothing from the bucket. The
	// bucket sits after the token check so a caller without the token cannot spend it.
	if recorder := ask(t, server, http.MethodPost, attributionsPath, oneAttribution,
		"wrong"); recorder.Code != http.StatusUnauthorized {
		t.Errorf("a wrong token got %d after the bucket emptied, want 401", recorder.Code)
	}
}

func TestADatabaseThatHasGoneAwayDoesNotStopThePushOrTheFeed(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{broken: true})

	payload, err := json.Marshal(telemetry.ForSending(pushedTable()))
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	recorder := ask(t, server, http.MethodPost, ingestPath, string(payload), token)
	if recorder.Code != http.StatusAccepted {
		t.Fatalf("push = %d (%s), want 202 with a broken database",
			recorder.Code, recorder.Body.String())
	}

	if _, pushed := server.Store().Latest(); !pushed {
		t.Error("the push did not land while the database was broken")
	}
}

func TestEveryWritePathRefusesTooLargeAndUnparseableBodiesTheSameWay(t *testing.T) {
	t.Parallel()

	writes := []struct {
		method, path string
		limit        int64
	}{
		{http.MethodPost, attributionsPath, maxWriteBytes},
		{http.MethodPost, adjustmentsPath, maxWriteBytes},
		{http.MethodPut, groupsPath, maxWriteBytes},
		{http.MethodPost, commandsPath, maxWriteBytes},
		{http.MethodPut, peoplePath, maxWriteBytes},
		{http.MethodPost, overridesPath, maxWriteBytes},
		{http.MethodPut, rulesPath, maxRulesBytes},
	}

	for _, write := range writes {
		t.Run(write.path, func(t *testing.T) {
			t.Parallel()

			// One server per route: the oversize probe spends the write bucket, so a second
			// request on the same server would get 429.
			server := boothServer(t, &fakeRecords{})

			oversized := ask(t, server, write.method, write.path,
				strings.Repeat("x", int(write.limit)+1), operatorToken)
			if oversized.Code != http.StatusRequestEntityTooLarge {
				t.Errorf("one past the limit: status = %d, want 413", oversized.Code)
			}
			if body := reason(t, oversized); body != "that is larger than this service accepts" {
				t.Errorf("one past the limit: error = %q, want the shared sentence", body)
			}

			unparseable := ask(t, server, write.method, write.path, "not json", operatorToken)
			if unparseable.Code != http.StatusBadRequest {
				t.Errorf("unparseable body: status = %d, want 400", unparseable.Code)
			}
			if body := reason(t, unparseable); !strings.HasPrefix(body, "that is not the JSON this path takes: ") {
				t.Errorf("unparseable body: error = %q, want the shared sentence and the parser's reason", body)
			}
		})
	}
}

func TestARulesDocumentIsAllowedToBeFourTimesAnOperatorsWrite(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	// Valid JSON over maxWriteBytes, padded inside the YAML string like a long rules document.
	body, err := json.Marshal(rulesRequest{
		Yaml: strings.Repeat("x", maxWriteBytes+4096), By: "ops",
	})
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	recorder := ask(t, server, http.MethodPut, rulesPath, string(body), operatorToken)
	if recorder.Code == http.StatusRequestEntityTooLarge {
		t.Fatalf("a rules document of %d bytes was refused as oversized; maxWriteBytes may have "+
			"replaced maxRulesBytes on this route", len(body))
	}
}

const oneAdjustment = `{"group":5,"player":"Rook","points":1,"reason":"killed by a bug","by":"ops"}`

func TestAnAdjustmentIsRefusedWithoutTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, presented := range []string{"", token, "watching", "wrong"} {
		if recorder := ask(t, server, http.MethodPost, adjustmentsPath, oneAdjustment,
			presented); recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestAnAdjustmentIsWrittenWithTheOperatorToken(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	recorder := ask(t, boothServer(t, records), http.MethodPost, adjustmentsPath, oneAdjustment,
		operatorToken)

	if recorder.Code != http.StatusCreated {
		t.Fatalf("status = %d (%s), want 201", recorder.Code, recorder.Body.String())
	}

	want := tournament.Adjustment{ID: 1, Group: 5, Name: "Rook", Points: 1, Reason: "killed by a bug",
		By: "ops", At: time.Date(2026, 10, 25, 12, 0, 0, 0, time.UTC)}
	if len(records.adjustments) != 1 || records.adjustments[0] != want {
		t.Errorf("adjustments = %+v, want %+v", records.adjustments, want)
	}
}

func TestABadAdjustmentIsRefusedWithAReason(t *testing.T) {
	t.Parallel()

	cases := map[string]string{
		"no game":   `{"group":0,"player":"Rook","points":1,"reason":"r","by":"ops"}`,
		"no player": `{"group":5,"player":" ","points":1,"reason":"r","by":"ops"}`,
		"no points": `{"group":5,"player":"Rook","points":0,"reason":"r","by":"ops"}`,
		"no reason": `{"group":5,"player":"Rook","points":1,"reason":"","by":"ops"}`,
		"nobody":    `{"group":5,"player":"Rook","points":1,"reason":"r","by":""}`,
		"too many":  `{"group":5,"player":"Rook","points":101,"reason":"r","by":"ops"}`,
		"too few":   `{"group":5,"player":"Rook","points":-101,"reason":"r","by":"ops"}`,
	}

	for name, body := range cases {
		t.Run(name, func(t *testing.T) {
			t.Parallel()

			records := &fakeRecords{}
			recorder := ask(t, boothServer(t, records), http.MethodPost, adjustmentsPath, body,
				operatorToken)

			if recorder.Code != http.StatusBadRequest || !strings.Contains(recorder.Body.String(), "error") {
				t.Errorf("status = %d (%s), want 400 with a reason", recorder.Code, recorder.Body.String())
			}

			if len(records.adjustments) != 0 {
				t.Errorf("a refused adjustment was kept: %+v", records.adjustments)
			}
		})
	}
}

func TestTheBracketFoldsAdjustmentsOntoThePushedTable(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	server.Store().Apply(pushedTable())

	if recorder := ask(t, server, http.MethodPost, adjustmentsPath, oneAdjustment,
		operatorToken); recorder.Code != http.StatusCreated {
		t.Fatalf("the adjustment was refused: %s", recorder.Body.String())
	}

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", "watching"))
	if bracket.Standings == nil {
		t.Fatal("standings is nil, want the folded table")
	}

	rook := bracket.Standings.Entries[1]
	if rook.Name != "Rook" || rook.Points != 5 || rook.Adjusted != 1 {
		t.Errorf("Rook = %+v, want 4 + 1 adjusted", rook)
	}
}
