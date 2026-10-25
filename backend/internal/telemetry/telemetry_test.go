package telemetry

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// examplePath is the canonical payload. The C# encoder is tested against the same file.
const examplePath = "../../../docs/telemetry/example.json"

func readExample(t *testing.T) []byte {
	t.Helper()

	payload, err := os.ReadFile(filepath.Clean(examplePath))
	if err != nil {
		t.Fatalf("reading the contract's example payload: %v", err)
	}

	return payload
}

func TestTheCommittedExampleParses(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("the payload the mod writes did not parse: %v", err)
	}

	if snapshot.Version != Version {
		t.Errorf("version = %d, want %d", snapshot.Version, Version)
	}

	wantAt := time.Date(2026, 10, 24, 12, 34, 56, 789_000_000, time.UTC)
	if !snapshot.At.Equal(wantAt) {
		t.Errorf("at = %s, want %s", snapshot.At, wantAt)
	}
}

func TestTheMatchArrivesWithItsCountdown(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if snapshot.Match == nil {
		t.Fatal("match is nil, want a running match")
	}

	if snapshot.Match.Group != 2 || snapshot.Match.State != "Running" {
		t.Errorf("match = group %d %q, want group 2 Running", snapshot.Match.Group, snapshot.Match.State)
	}

	if got := snapshot.Match.Elapsed(); got != 150*time.Second {
		t.Errorf("elapsed = %s, want 2m30s", got)
	}

	until, ok := snapshot.Match.UntilNext()
	if !ok || until != 30*time.Second {
		t.Errorf("untilNext = %s (%t), want 30s true", until, ok)
	}
}

func TestBothSpheresArriveForTheMap(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if snapshot.Zone == nil {
		t.Fatal("zone is nil, want two spheres")
	}

	if snapshot.Zone.Now.Radius != 1400 {
		t.Errorf("now.r = %v, want 1400", snapshot.Zone.Now.Radius)
	}

	if snapshot.Zone.Next == nil {
		t.Fatal("next is nil, want the sphere the wall is closing to")
	}

	if snapshot.Zone.Next.Radius != 900 || snapshot.Zone.Next.X != 500 {
		t.Errorf("next = %+v, want centre x 500 radius 900", *snapshot.Zone.Next)
	}
}

// A quote and a backslash are what a hand-written encoder most often gets wrong.
func TestAwkwardNamesSurviveTheRoundTrip(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if len(snapshot.Players) != 2 {
		t.Fatalf("players = %d, want 2", len(snapshot.Players))
	}

	vela := snapshot.Players[0]
	if vela.Name != "Vela" || vela.Depth != 42.1 {
		t.Errorf("vela = %q at depth %v, want Vela at 42.1", vela.Name, vela.Depth)
	}

	if len(vela.Inventory) != 2 || vela.Inventory[1].Item != `Quartz "big"` {
		t.Errorf("inventory = %+v, want a quoted item name intact", vela.Inventory)
	}

	rook := snapshot.Players[1]
	if rook.Name != `Ro\ok` {
		t.Errorf("name = %q, want a backslash intact", rook.Name)
	}

	if rook.Alive || rook.InsideZone || rook.DistanceOutside != 240.25 {
		t.Errorf("rook = %+v, want out, outside, 240.25 m past the wall", rook)
	}
}

func TestAbsentFieldsArriveAsNilRatherThanZero(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	rook := snapshot.Players[1]
	if rook.Biome != nil || rook.Vehicle != nil || rook.Held != nil {
		t.Errorf("rook = %+v, want nil biome, vehicle and held item", rook)
	}

	if rook.Inventory == nil || len(rook.Inventory) != 0 {
		t.Errorf("inventory = %v, want an empty list rather than nil", rook.Inventory)
	}

	if rook.Equipment == nil || len(rook.Equipment) != 0 {
		t.Errorf("equipment = %v, want an empty list rather than nil", rook.Equipment)
	}
}

func TestWhatAFighterIsWearingAndCarryingArrives(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	vela := snapshot.Players[0]

	if len(vela.Equipment) != 2 {
		t.Fatalf("equipment = %+v, want two filled slots", vela.Equipment)
	}

	// Worn equipment differs from the same item carried.
	if vela.Equipment[0].Slot != "Body" || vela.Equipment[0].Item != "ReinforcedDiveSuit" {
		t.Errorf("equipment[0] = %+v, want Body: ReinforcedDiveSuit", vela.Equipment[0])
	}

	if vela.Equipment[1].Slot != "Tank" {
		t.Errorf("equipment[1] = %+v, want the tank slot", vela.Equipment[1])
	}

	if !vela.Rifle || vela.RifleCharge != 0.5 || vela.Frozen {
		t.Errorf("rifle = %t at %v, frozen %t; want a rifle half charged and nobody holding her",
			vela.Rifle, vela.RifleCharge, vela.Frozen)
	}

	rook := snapshot.Players[1]
	if rook.Rifle || rook.RifleCharge != 0 || rook.Frozen {
		t.Errorf("rook = %+v, want no rifle, no charge and not frozen", rook)
	}
}

func TestTheEventFeedKeepsTheLogsOwnSequenceAndWords(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if len(snapshot.Events) != 3 {
		t.Fatalf("events = %d, want 3", len(snapshot.Events))
	}

	phase := snapshot.Events[0]
	if phase.Kind != "PhaseStarted" || phase.Sequence != 41 {
		t.Errorf("first event = %s #%d, want PhaseStarted #41", phase.Kind, phase.Sequence)
	}

	// Its own match identity, so an event kept across a reset is filed correctly.
	if phase.Group != 2 || phase.Generation != 1 {
		t.Errorf("first event = group %d.%d, want 2.1", phase.Group, phase.Generation)
	}

	// A phase change has no position.
	if phase.X != nil || phase.Y != nil || phase.Z != nil {
		t.Errorf("phase event has a position %v,%v,%v, want none", phase.X, phase.Y, phase.Z)
	}

	kill := snapshot.Events[2]
	if kill.Kind != "PlayerEliminated" || kill.Actor == nil || *kill.Actor != "Vela" {
		t.Errorf("last event = %+v, want an elimination attributed to Vela", kill)
	}

	if kill.X == nil || *kill.X != -5 {
		t.Errorf("elimination has no place, want one for the map's pin")
	}

	if got := kill.Elapsed(); got != 140*time.Second {
		t.Errorf("elapsed = %s, want 2m20s", got)
	}
}

func TestAPayloadFromAnotherBuildIsRefusedRatherThanHalfRead(t *testing.T) {
	_, err := Parse([]byte(`{"v":999,"at":"2026-10-24T12:00:00.000Z","match":null,"zone":null,"players":[],"events":[]}`))
	if err == nil {
		t.Fatal("a version this service does not speak was accepted")
	}
}

func TestRubbishIsAnErrorRatherThanAPanic(t *testing.T) {
	if _, err := Parse([]byte("not json")); err == nil {
		t.Fatal("invalid JSON was accepted")
	}
}

// Go marshals a nil slice as null and a time with nanoseconds; the contract forbids both.
func TestASnapshotBuiltHereIsSentInTheContractsShape(t *testing.T) {
	built := Snapshot{
		Version: Version,
		At:      time.Date(2026, 10, 24, 12, 0, 0, 123456789, time.UTC),
		Players: []Player{{Name: "Vela"}},
	}

	payload, err := json.Marshal(ForSending(built))
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	text := string(payload)

	for _, forbidden := range []string{
		`"players":null`, `"events":null`, `"inventory":null`, `"equipment":null`, `"pods":null`,
	} {
		if strings.Contains(text, forbidden) {
			t.Errorf("payload contains %s; the contract says those are always arrays:\n%s", forbidden, text)
		}
	}

	if !strings.Contains(text, `"at":"2026-10-24T12:00:00.123Z"`) {
		t.Errorf("timestamp is not milliseconds with a Z:\n%s", text)
	}
}

// The empty snapshot is what every client receives first, on connect.
func TestAnEmptySnapshotIsStillAllArrays(t *testing.T) {
	payload, err := json.Marshal(ForSending(Snapshot{Version: Version}))
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	if !strings.Contains(string(payload), `"players":[]`) ||
		!strings.Contains(string(payload), `"events":[]`) ||
		!strings.Contains(string(payload), `"pods":[]`) {
		t.Errorf("empty snapshot = %s, want empty arrays rather than nulls", payload)
	}
}

func TestTheTournamentTableArrivesInItsOwnOrder(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if snapshot.Standings == nil {
		t.Fatal("standings is nil, want the table the example carries")
	}

	if snapshot.StandingsRevision != 2 {
		t.Errorf("standingsRevision = %d, want 2", snapshot.StandingsRevision)
	}

	if snapshot.Standings.Matches != 2 {
		t.Errorf("matches = %d, want 2 finished", snapshot.Standings.Matches)
	}

	if len(snapshot.Standings.Entries) != 2 {
		t.Fatalf("entries = %d, want 2", len(snapshot.Standings.Entries))
	}

	// Leader first, in the mod's order; never re-sorted here.
	leader := snapshot.Standings.Entries[0]
	if leader.Place != 1 {
		t.Errorf("leader's place = %d, want 1", leader.Place)
	}

	if leader.Name != "Vela" || leader.Points != 9 {
		t.Errorf("leader = %q on %d, want Vela on 9", leader.Name, leader.Points)
	}

	if leader.Wins != 1 || leader.BestPlacement != 1 || leader.Played != 2 {
		t.Errorf("leader = %d win(s), best %d, %d played; want 1, 1, 2",
			leader.Wins, leader.BestPlacement, leader.Played)
	}
}

func TestEveryRowsColumnsAddUpToItsPoints(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	for _, line := range snapshot.Standings.Entries {
		if want := snapshot.Standings.PointsPerKill*line.Kills + line.Outlasted; line.Points != want {
			t.Errorf("%s: points = %d, but %d kills and %d outlasted make %d",
				line.Name, line.Points, line.Kills, line.Outlasted, want)
		}
	}
}

func TestTheTableSaysWhatAKillPays(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if snapshot.Standings.PointsPerKill != 2 {
		t.Errorf("pointsPerKill = %d, want the 2 the example carries",
			snapshot.Standings.PointsPerKill)
	}
}

func TestEachFinishedGameArrivesBesideTheTable(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	games := snapshot.Standings.Games
	if len(games) != 2 || games[0].Group != 1 || games[1].Group != 2 {
		t.Fatalf("games = %+v, want groups 1 and 2 in the order they were folded", games)
	}

	sums := map[string]int{}
	for _, game := range games {
		for _, line := range game.Entries {
			sums[line.Name] += line.Points
		}
	}

	for _, line := range snapshot.Standings.Entries {
		if sums[line.Name] != line.Points {
			t.Errorf("%s: games add to %d, table says %d", line.Name, sums[line.Name], line.Points)
		}
	}
}

// A nil Standings means no table; a non-nil one must keep array rows.
func TestATableWithNoRowsIsStillATable(t *testing.T) {
	payload, err := json.Marshal(ForSending(Snapshot{
		Version:   Version,
		Standings: &Standings{Matches: 1},
	}))
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	if !strings.Contains(string(payload), `"entries":[]`) || !strings.Contains(string(payload), `"games":[]`) {
		t.Errorf("payload = %s, want empty entries and games arrays rather than null", payload)
	}
}

func TestNoTableStaysNull(t *testing.T) {
	payload, err := json.Marshal(ForSending(Snapshot{Version: Version}))
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	if !strings.Contains(string(payload), `"standings":null`) {
		t.Errorf("payload = %s, want a null standings block", payload)
	}

	if !strings.Contains(string(payload), `"standingsRevision":0`) {
		t.Errorf("payload = %s, want revision zero present rather than absent", payload)
	}
}

func TestTheLifepodsArriveAsLandmarks(t *testing.T) {
	snapshot, err := Parse(readExample(t))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}

	if len(snapshot.Pods) != 5 {
		t.Fatalf("pods = %d, want a group of five", len(snapshot.Pods))
	}

	if snapshot.Pods[0].Z != 600 || snapshot.Pods[0].Assigned != 1 {
		t.Errorf("pods[0] = %+v, want the first spot on the circle with somebody in it", snapshot.Pods[0])
	}

	// A derelict: no players assigned. Still a landmark.
	last := snapshot.Pods[4]
	if last.Assigned != 0 || last.X != -100.5 {
		t.Errorf("pods[4] = %+v, want a derelict off the circle", last)
	}
}
