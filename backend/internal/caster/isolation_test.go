package caster

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// twoBooths is two booths with distinct tokens; a booth is identified by the credential presented.
var twoBooths = []config.Booth{
	{ID: "ada", IngestToken: "ada-push", ViewToken: "ada-view", OperatorToken: "ada-write"},
	{ID: "grace", IngestToken: "grace-push", ViewToken: "grace-view", OperatorToken: "grace-write"},
}

func twoBoothServer(t *testing.T) *Server {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: twoBooths}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))
}

// boothPath is the path of a booth's route.
func boothPath(booth, path string) string {
	return "/b/" + booth + path
}

// pushTo sends one payload to a booth's ingest with the given bearer token.
func pushTo(t *testing.T, server *Server, booth string, body []byte, bearer string) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(http.MethodPost, boothPath(booth, ingestPath), strings.NewReader(string(body)))
	if bearer != "" {
		request.Header.Set("Authorization", "Bearer "+bearer)
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	return recorder
}

// snapshotFor is a payload with one player and one event at the given sequence, so interleaved
// rosters or event streams are detectable.
func snapshotFor(player string, sequence int) []byte {
	return []byte(`{"v":6,"at":"2026-10-24T12:34:56.789Z",` +
		`"match":{"group":2,"generation":1,"state":"Running","elapsedMs":1,"phase":1,"phases":4,` +
		`"untilNextMs":1,"alive":1},` +
		`"zone":{"now":{"x":0,"y":0,"z":0,"r":1},"next":{"x":0,"y":0,"z":0,"r":1},"damagePerTick":1},` +
		`"standingsRevision":0,"standings":null,` +
		`"players":[{"name":"` + player + `","alive":true,"x":0,"y":0,"z":0,"depth":0,"biome":null,` +
		`"health":100,"oxygen":45,"food":90,"water":90,"vehicle":null,"held":null,"rifle":false,` +
		`"rifleCharge":0,"frozen":false,"insideZone":true,"distanceOutside":0,"inventory":[],` +
		`"equipment":[]}],` +
		`"events":[{"seq":` + strconv.Itoa(sequence) + `,"group":2,"generation":1,` +
		`"at":"2026-10-24T12:34:56.789Z","elapsedMs":1,"kind":"ZoneBite","player":"` + player + `",` +
		`"actor":null,"phase":1,"amount":1,"detail":null,"x":null,"y":null,"z":null,"biome":null}],` +
		`"pods":[]}`)
}

func TestTwoServersPushingUnderDifferentTokensAreStoredSeparately(t *testing.T) {
	server := twoBoothServer(t)

	// Grace's sequence is lower than Ada's on purpose: a shared watermark would silently drop it.
	if code := pushTo(t, server, "ada", snapshotFor("Ada", 9), "ada-push").Code; code != http.StatusAccepted {
		t.Fatalf("ada's push: status = %d, want 202", code)
	}
	if code := pushTo(t, server, "grace", snapshotFor("Grace", 2), "grace-push").Code; code != http.StatusAccepted {
		t.Fatalf("grace's push: status = %d, want 202", code)
	}

	ada, ok := server.Booth("ada").Store().Latest()
	if !ok {
		t.Fatal("ada's booth has no state after ada pushed")
	}
	grace, ok := server.Booth("grace").Store().Latest()
	if !ok {
		t.Fatal("grace's booth has no state after grace pushed")
	}

	if len(ada.Players) != 1 || ada.Players[0].Name != "Ada" {
		t.Errorf("ada's roster = %+v, want only Ada", ada.Players)
	}
	if len(grace.Players) != 1 || grace.Players[0].Name != "Grace" {
		t.Errorf("grace's roster = %+v, want only Grace", grace.Players)
	}

	if len(ada.Events) != 1 || named(ada.Events[0].Player) != "Ada" {
		t.Errorf("ada's events = %+v, want only her own", ada.Events)
	}
	if len(grace.Events) != 1 || named(grace.Events[0].Player) != "Grace" {
		t.Errorf("grace's events = %+v, want her own rather than none", grace.Events)
	}

	// The watermark is per booth, so grace's lower sequence is not judged against ada's.
	if got := server.Booth("grace").Store().LastSequence(); got != 2 {
		t.Errorf("grace's last sequence = %d, want 2 rather than ada's 9", got)
	}
}

func TestAnIdInThePayloadIsIgnored(t *testing.T) {
	server := twoBoothServer(t)

	claiming := []byte(strings.Replace(string(snapshotFor("Ada", 9)),
		`"v":6,`, `"v":6,"booth":"grace",`, 1))

	if code := pushTo(t, server, "ada", claiming, "ada-push").Code; code != http.StatusAccepted {
		t.Fatalf("status = %d, want 202 — an unknown key is not a refusal", code)
	}

	if _, ok := server.Booth("grace").Store().Latest(); ok {
		t.Error("a push that named grace in its body landed in grace")
	}

	ada, ok := server.Booth("ada").Store().Latest()
	if !ok || len(ada.Players) != 1 || ada.Players[0].Name != "Ada" {
		t.Errorf("ada's state = %+v %t, want the push that presented ada's token", ada, ok)
	}
}

func TestABoothNamedInAHeaderIsIgnored(t *testing.T) {
	server := twoBoothServer(t)

	request := httptest.NewRequest(http.MethodPost, boothPath("ada", ingestPath),
		strings.NewReader(string(snapshotFor("Ada", 9))))
	request.Header.Set("Authorization", "Bearer ada-push")

	// Plausible header names a booth id might be read from.
	for _, header := range []string{"X-Booth", "X-Booth-Id", "Booth"} {
		request.Header.Set(header, "grace")
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("status = %d, want 202 — an unknown header is not a refusal", recorder.Code)
	}

	if _, ok := server.Booth("grace").Store().Latest(); ok {
		t.Error("a push whose headers named grace landed in grace")
	}

	ada, ok := server.Booth("ada").Store().Latest()
	if !ok || len(ada.Players) != 1 || ada.Players[0].Name != "Ada" {
		t.Errorf("ada's state = %+v %t, want the push that presented ada's token", ada, ok)
	}
}

func TestABoothNamedInAHeaderIsIgnoredOnABarePath(t *testing.T) {
	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: append(oneBooth("tournament-push", "tournament-view", "tournament-write"), twoBooths...)}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))

	// A bare path takes the reserved-booth fallback, which no /b/{id} test exercises.
	request := httptest.NewRequest(http.MethodPost, ingestPath,
		strings.NewReader(string(snapshotFor("Casey", 9))))
	request.Header.Set("Authorization", "Bearer tournament-push")

	for _, header := range []string{"X-Booth", "X-Booth-Id", "Booth"} {
		request.Header.Set(header, "ada")
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("status = %d %q, want 202 — a bare path is the reserved booth and its token was presented",
			recorder.Code, recorder.Body.String())
	}

	if _, ok := server.Booth("ada").Store().Latest(); ok {
		t.Error("a bare-path push whose headers named ada landed in ada")
	}

	kept, ok := server.Booth(config.TournamentBooth).Store().Latest()
	if !ok || len(kept.Players) != 1 || kept.Players[0].Name != "Casey" {
		t.Errorf("the reserved booth's state = %+v %t, want the bare-path push", kept, ok)
	}
}

func TestOneBoothsIngestTokenCannotPushToAnother(t *testing.T) {
	server := twoBoothServer(t)

	// Refused like an unknown token, so a guesser learns nothing from the difference.
	wrong := pushTo(t, server, "grace", snapshotFor("Ada", 9), "ada-push")
	unknown := pushTo(t, server, "grace", snapshotFor("Ada", 9), "no-such-token")

	if wrong.Code != http.StatusUnauthorized {
		t.Errorf("ada's token on grace's path = %d, want 401", wrong.Code)
	}
	if wrong.Code != unknown.Code || wrong.Body.String() != unknown.Body.String() {
		t.Errorf("a wrong booth's token is distinguishable from an unknown one:\n %d %q\n %d %q",
			wrong.Code, wrong.Body.String(), unknown.Code, unknown.Body.String())
	}

	if _, ok := server.Booth("grace").Store().Latest(); ok {
		t.Error("a refused push reached grace's store")
	}
}

func TestAnUnknownBoothIsRefusedTheSameWayAndIsNotCreated(t *testing.T) {
	server := twoBoothServer(t)

	unknown := pushTo(t, server, "stranger", snapshotFor("Ada", 9), "ada-push")
	badToken := pushTo(t, server, "ada", snapshotFor("Ada", 9), "wrong")

	if unknown.Code != http.StatusUnauthorized {
		t.Errorf("an unknown booth = %d, want 401", unknown.Code)
	}
	if unknown.Body.String() != badToken.Body.String() {
		t.Errorf("an unknown booth is distinguishable from a bad token:\n %q\n %q",
			unknown.Body.String(), badToken.Body.String())
	}

	if server.Booth("stranger") != nil {
		t.Error("pushing at an unknown booth created one")
	}
}

func TestAnOperatorTokenCannotWriteToAnotherBooth(t *testing.T) {
	server := twoBoothServer(t)

	writes := []struct {
		name   string
		method string
		path   string
		body   string
	}{
		{"attribution", http.MethodPost, attributionsPath,
			`{"group":1,"generation":1,"victim":"Ada","by":"someone"}`},
		{"groups", http.MethodPut, groupsPath, `{"groups":[{"number":1,"name":"One","members":[]}]}`},
		{"command", http.MethodPost, commandsPath, `{"verb":"status","by":"someone"}`},
		{"people", http.MethodPut, peoplePath, `{"people":[]}`},
		{"override", http.MethodPost, overridesPath, `{"person":1,"role":"ghost","by":"someone"}`},
		{"rules", http.MethodPut, rulesPath, `{"yaml":"zone: {}","by":"someone"}`},
	}

	for _, write := range writes {
		t.Run(write.name, func(t *testing.T) {
			wrong := writeTo(t, server, "grace", write.method, write.path, write.body, "ada-write")
			unknown := writeTo(t, server, "grace", write.method, write.path, write.body, "no-such-token")

			if wrong.Code != http.StatusUnauthorized {
				t.Errorf("ada's operator token on grace's %s = %d, want 401", write.path, wrong.Code)
			}
			if wrong.Code != unknown.Code || wrong.Body.String() != unknown.Body.String() {
				t.Errorf("a wrong booth's operator token is distinguishable from an unknown one:\n %d %q\n %d %q",
					wrong.Code, wrong.Body.String(), unknown.Code, unknown.Body.String())
			}
		})
	}
}

func TestAnAuthorisedWriteLandsInTheBoothThatPresentedTheToken(t *testing.T) {
	records, rowsFor := perBooth()
	server := twoBoothServer(t).WithRecords(records)

	got := writeTo(t, server, "ada", http.MethodPost, attributionsPath,
		`{"group":1,"generation":1,"victim":"Rook","by":"ada"}`, "ada-write")
	if got.Code != http.StatusCreated {
		t.Fatalf("ada's own write = %d %q, want 201 — this test proves nothing unless it is accepted",
			got.Code, got.Body.String())
	}

	if rows := rowsFor("ada").rows; len(rows) != 1 || rows[0].Victim != "Rook" {
		t.Errorf("ada's attributions = %+v, want the one she wrote", rows)
	}

	// Neither the other booth nor the reserved booth a misrouted handler would fall back to.
	if rows := rowsFor("grace").rows; len(rows) != 0 {
		t.Errorf("grace's attributions = %+v, want none of ada's", rows)
	}
	if rows := rowsFor(config.TournamentBooth).rows; len(rows) != 0 {
		t.Errorf("the %q booth's attributions = %+v, want none of ada's", config.TournamentBooth, rows)
	}
}

func writeTo(t *testing.T, server *Server, booth, method, path, body, bearer string) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(method, boothPath(booth, path), strings.NewReader(body))
	if bearer != "" {
		request.Header.Set("Authorization", "Bearer "+bearer)
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	return recorder
}

func TestOneBoothsOperatorTokenCannotReadAnothersConsole(t *testing.T) {
	server := twoBoothServer(t)

	wrong := writeTo(t, server, "grace", http.MethodGet, consolePath, "", "ada-write")
	unknown := writeTo(t, server, "grace", http.MethodGet, consolePath, "", "no-such-token")

	if wrong.Code != http.StatusUnauthorized {
		t.Errorf("status = %d, want 401", wrong.Code)
	}
	if wrong.Body.String() != unknown.Body.String() {
		t.Errorf("refusals differ:\n %q\n %q", wrong.Body.String(), unknown.Body.String())
	}
}

func TestABracketIsThePathsBoothAndNotTheProcesss(t *testing.T) {
	server := twoBoothServer(t)

	if code := pushTo(t, server, "ada", examplePayload(t), "ada-push").Code; code != http.StatusAccepted {
		t.Fatalf("seeding ada: status = %d", code)
	}

	recorder := writeTo(t, server, "grace", http.MethodGet, bracketPath, "", "grace-view")
	if recorder.Code != http.StatusOK {
		t.Fatalf("grace's bracket: status = %d, want 200", recorder.Code)
	}

	var body struct {
		Standings *json.RawMessage `json:"standings"`
	}
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("bracket is not JSON: %v", err)
	}

	if body.Standings != nil {
		t.Errorf("grace's bracket carries a table: %s", *body.Standings)
	}
}

func TestFillingOneBoothsEventWindowDoesNotEvictAnothers(t *testing.T) {
	server := twoBoothServer(t)

	if code := pushTo(t, server, "grace", snapshotFor("Grace", 1), "grace-push").Code; code != http.StatusAccepted {
		t.Fatalf("grace's push: status = %d", code)
	}

	ada := server.Booth("ada").Store()
	for sequence := 1; sequence <= maxEvents+10; sequence++ {
		ada.Apply(snapshotWith(event(int64(sequence))))
	}

	if evicted := ada.Evicted(); evicted == 0 {
		t.Fatal("ada's window did not roll, so this test proves nothing")
	}

	if evicted := server.Booth("grace").Store().Evicted(); evicted != 0 {
		t.Errorf("grace's window evicted %d events while ada's filled", evicted)
	}
	if held := server.Booth("grace").Store().EventsAfter(0); len(held) != 1 {
		t.Errorf("grace holds %d events, want the one she pushed", len(held))
	}
}

func TestAViewTokenForOneBoothCannotReadAnothersLiveFeed(t *testing.T) {
	server := twoBoothServer(t)
	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	// Refused like an unknown token, or booth ids could be enumerated with one's own token.
	wrong := readLive(t, httpServer.URL, "grace", "ada-view")
	unknown := readLive(t, httpServer.URL, "grace", "no-such-token")

	if wrong.Code != http.StatusUnauthorized {
		t.Errorf("ada's view token on grace's feed = %d, want 401", wrong.Code)
	}
	if wrong.Code != unknown.Code || wrong.Body != unknown.Body {
		t.Errorf("a wrong booth's view token is distinguishable from an unknown one:\n %d %q\n %d %q",
			wrong.Code, wrong.Body, unknown.Code, unknown.Body)
	}
}

func TestAViewTokenForOneBoothCannotReadAnothersBracket(t *testing.T) {
	server := twoBoothServer(t)

	wrong := writeTo(t, server, "grace", http.MethodGet, bracketPath, "", "ada-view")
	unknown := writeTo(t, server, "grace", http.MethodGet, bracketPath, "", "no-such-token")

	if wrong.Code != http.StatusUnauthorized {
		t.Errorf("ada's view token on grace's bracket = %d, want 401", wrong.Code)
	}
	if wrong.Code != unknown.Code || wrong.Body.String() != unknown.Body.String() {
		t.Errorf("a wrong booth's view token is distinguishable from an unknown one:\n %d %q\n %d %q",
			wrong.Code, wrong.Body.String(), unknown.Code, unknown.Body.String())
	}
}

func TestABracketForAnUnknownBoothIsRefusedTheSameWay(t *testing.T) {
	server := twoBoothServer(t)

	stranger := writeTo(t, server, "stranger", http.MethodGet, bracketPath, "", "ada-view")
	badToken := writeTo(t, server, "ada", http.MethodGet, bracketPath, "", "no-such-token")

	if stranger.Code != http.StatusUnauthorized {
		t.Errorf("an unknown booth's bracket = %d, want 401", stranger.Code)
	}
	if stranger.Body.String() != badToken.Body.String() {
		t.Errorf("an unknown booth is distinguishable from a bad token:\n %q\n %q",
			stranger.Body.String(), badToken.Body.String())
	}
}

func TestALiveFeedForAnUnknownBoothIsRefusedTheSameWay(t *testing.T) {
	server := twoBoothServer(t)
	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	stranger := readLive(t, httpServer.URL, "stranger", "ada-view")
	badToken := readLive(t, httpServer.URL, "ada", "no-such-token")

	if stranger.Code != http.StatusUnauthorized {
		t.Errorf("an unknown booth's feed = %d, want 401", stranger.Code)
	}
	if stranger.Body != badToken.Body {
		t.Errorf("an unknown booth is distinguishable from a bad token:\n %q\n %q",
			stranger.Body, badToken.Body)
	}
}

// refusal is the answer to a plain GET on a WebSocket route; the token is checked before upgrade.
type refusal struct {
	Code int
	Body string
}

func readLive(t *testing.T, base, booth, token string) refusal {
	t.Helper()

	res, err := http.Get(base + boothPath(booth, livePath) + "?token=" + token)
	if err != nil {
		t.Fatalf("GET %s: %v", boothPath(booth, livePath), err)
	}
	defer func() { _ = res.Body.Close() }()

	body, err := io.ReadAll(res.Body)
	if err != nil {
		t.Fatalf("reading the refusal: %v", err)
	}

	return refusal{Code: res.StatusCode, Body: string(body)}
}

func TestWithOneBoothTheBarePathsAreStillTheBooth(t *testing.T) {
	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, viewToken, "")}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))

	if code := push(t, server, examplePayload(t), token).Code; code != http.StatusAccepted {
		t.Fatalf("POST %s: status = %d, want 202", ingestPath, code)
	}

	snapshot, ok := server.Store().Latest()
	if !ok || snapshot.Match == nil || snapshot.Match.Group != 2 {
		t.Fatalf("state after a push to the bare path = %+v %t", snapshot.Match, ok)
	}

	// The bare path and /b/tournament are the same booth.
	if _, ok := server.Booth(config.TournamentBooth).Store().Latest(); !ok {
		t.Error("the bare path and /b/tournament are two different booths")
	}
}

// named returns an event's player, or the empty string when the event names nobody.
func named(player *string) string {
	if player == nil {
		return ""
	}

	return *player
}

func TestOneBoothsModCannotReportAgainstAnothersConsole(t *testing.T) {
	server := twoBoothServer(t)

	wrong := writeTo(t, server, "grace", http.MethodPost, consolePath, oneReport, "ada-push")
	unknown := writeTo(t, server, "grace", http.MethodPost, consolePath, oneReport, "no-such-token")

	if wrong.Code != http.StatusUnauthorized {
		t.Errorf("ada's ingest token on grace's console = %d, want 401", wrong.Code)
	}
	if wrong.Body.String() != unknown.Body.String() {
		t.Errorf("refusals differ:\n %q\n %q", wrong.Body.String(), unknown.Body.String())
	}

	if report, seen := server.Booth("grace").console.Latest(); report != nil || !seen.IsZero() {
		t.Errorf("grace's console holds %+v from %v, which came from ada's mod", report, seen)
	}
}

func TestAModsReportLandsInTheBoothWhoseTokenItPresented(t *testing.T) {
	server := twoBoothServer(t)

	accepted := writeTo(t, server, "ada", http.MethodPost, consolePath, oneReport, "ada-push")
	if accepted.Code != http.StatusAccepted {
		t.Fatalf("status = %d, want 202: %s", accepted.Code, accepted.Body.String())
	}

	if _, seen := server.Booth("ada").console.Latest(); seen.IsZero() {
		t.Error("ada's console never saw her own mod's report")
	}

	if _, seen := server.Booth("grace").console.Latest(); !seen.IsZero() {
		t.Error("ada's report reached grace's console")
	}
}

func TestTheReservedBoothsReadersAreNilWhenItIsNotConfigured(t *testing.T) {
	server := twoBoothServer(t)

	if got := server.Booth(config.TournamentBooth); got != nil {
		t.Fatalf("Booth(%q) = %+v, want nil — this test proves nothing if it is configured",
			config.TournamentBooth, got)
	}

	if got := server.Store(); got != nil {
		t.Errorf("Store() = %+v, want nil when no reserved booth is configured", got)
	}
	if got := server.Hub(); got != nil {
		t.Errorf("Hub() = %+v, want nil when no reserved booth is configured", got)
	}
}
