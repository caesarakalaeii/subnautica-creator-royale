// Package telemetry is the backend's side of the payload the mod pushes. The mod writes it; nothing
// here writes it back.
//
// docs/telemetry/example.json is the canonical payload; both the C# encoder and this package are
// tested against it.
package telemetry

import (
	"encoding/json"
	"fmt"
	"time"
)

// Version is the contract version this package understands. The mod sends it as the first field of
// every push so a mismatch is caught immediately.
const Version = 6

// Snapshot is one push: the full live state plus the events since the last push. Events carry the
// audit log's sequence numbers, so a client can tell a gap from a quiet minute.
type Snapshot struct {
	Version int       `json:"v"`
	At      time.Time `json:"at"`

	// Match is nil when no tournament is running, which differs from a tournament in the lobby.
	Match *Match `json:"match"`

	// Zone is nil before a match starts.
	Zone *Zone `json:"zone"`

	// StandingsRevision is the version of the table this push refers to, counting from zero. The same
	// revision as the one held means keep it; a new revision with nil Standings means there is no
	// table now.
	StandingsRevision int `json:"standingsRevision"`

	// Standings is nil on most pushes: the table is sent when it changes and on a slow heartbeat. Store
	// keeps the last one, so clients always get it.
	Standings *Standings `json:"standings"`

	Players []Player `json:"players"`
	Events  []Event  `json:"events"`

	// Pods is every escape pod in the world: the arena's fixed landmarks. Always an array, empty
	// before a round has laid a group out.
	Pods []Pod `json:"pods"`
}

// Pod is one escape pod: its position and how many players are assigned to it. The game reports no
// owner names, so the map cannot reveal who started where.
type Pod struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`

	// Assigned is how many players are assigned to it, connected or not. Zero is a derelict pod.
	Assigned int `json:"assigned"`
}

// Standings is the tournament table across finished matches, folded by the mod from its audit log.
type Standings struct {
	// Matches is how many matches the table was folded from, not how many any one player has played.
	Matches int `json:"matches"`

	// PointsPerKill is what a kill pays. Sent with the table so the backend can fold in operator
	// attributions using the mod's value rather than a duplicated constant.
	PointsPerKill int `json:"pointsPerKill"`

	// Entries is the table, leader first, ordered by the mod (points, wins, best result, name). Never
	// re-sorted here.
	Entries []StandingsLine `json:"entries"`

	// Games is one entry per finished match, in finish order, with every player's result, so series
	// standings can be computed per game.
	Games []GameResult `json:"games"`
}

// GameResult is one finished match's final ledger.
type GameResult struct {
	Group      int        `json:"group"`
	Generation int        `json:"generation"`
	Entries    []GameLine `json:"entries"`
}

// GameLine is one player's result in one match.
type GameLine struct {
	Name      string `json:"name"`
	Points    int    `json:"points"`
	Placement int    `json:"placement"`
	Kills     int    `json:"kills"`
	Outlasted int    `json:"outlasted"`
}

// StandingsLine is one player's tournament. The components travel beside the total so the points
// can be explained and checked.
type StandingsLine struct {
	// Place is one for the leader; tied players share a place. Sent rather than taken from the row
	// index, which breaks ties by name.
	Place int `json:"place"`

	Name   string `json:"name"`
	Points int    `json:"points"`

	// Played is how many matches this player was in, which is at most Standings.Matches.
	Played int `json:"played"`

	// Wins is first places, shared ones included: the first tie-break after points.
	Wins int `json:"wins"`

	// BestPlacement is their best single result, the tie-break after wins.
	BestPlacement int `json:"best"`

	Kills     int `json:"kills"`
	Outlasted int `json:"outlasted"`
}

// Match is the state of the one match in progress; matches run one at a time.
type Match struct {
	Group      int    `json:"group"`
	Generation int    `json:"generation"`
	State      string `json:"state"`
	ElapsedMs  int64  `json:"elapsedMs"`
	Phase      int    `json:"phase"`
	Phases     int    `json:"phases"`

	// UntilNextMs is nil when nothing is due: the lobby, a pause, or after the last phase. The server
	// owns the clock, so clients do not compute it.
	UntilNextMs *int64 `json:"untilNextMs"`

	Alive int `json:"alive"`
}

// Elapsed is how far into the match, as a duration.
func (m Match) Elapsed() time.Duration {
	return time.Duration(m.ElapsedMs) * time.Millisecond
}

// UntilNext is the countdown and whether there is one.
func (m Match) UntilNext() (time.Duration, bool) {
	if m.UntilNextMs == nil {
		return 0, false
	}

	return time.Duration(*m.UntilNextMs) * time.Millisecond, true
}

// Zone is where the wall is and where it is closing to.
type Zone struct {
	Now Sphere `json:"now"`

	// Next is nil in the last phase, which closes onto its own centre.
	Next *Sphere `json:"next"`

	// DamagePerTick is per tick of the configured interval, not per second.
	DamagePerTick float64 `json:"damagePerTick"`
}

// Sphere is a centre and a radius, in metres, in the game's own coordinates.
type Sphere struct {
	X      float64 `json:"x"`
	Y      float64 `json:"y"`
	Z      float64 `json:"z"`
	Radius float64 `json:"r"`
}

// Player is one row of the dashboard: everything shown about a player right now, as reported by
// their own client.
type Player struct {
	Name  string `json:"name"`
	Alive bool   `json:"alive"`

	X     float64 `json:"x"`
	Y     float64 `json:"y"`
	Z     float64 `json:"z"`
	Depth float64 `json:"depth"`

	// Biome is nil where nothing names the place.
	Biome *string `json:"biome"`

	Health float64 `json:"health"`
	Oxygen float64 `json:"oxygen"`
	Food   float64 `json:"food"`
	Water  float64 `json:"water"`

	// Vehicle and Held are nil rather than empty: no vehicle is not a vehicle called "".
	Vehicle *string `json:"vehicle"`
	Held    *string `json:"held"`

	// Rifle is whether they carry a stasis rifle. Sent by the mod so the backend need not know the
	// game's item names.
	Rifle bool `json:"rifle"`

	// RifleCharge is how far into a charge the trigger is, from zero to one; zero when not holding it.
	RifleCharge float64 `json:"rifleCharge"`

	// Frozen is whether a stasis field is holding them. Who froze them is not knowable from the
	// reporting client.
	Frozen bool `json:"frozen"`

	InsideZone bool `json:"insideZone"`

	// DistanceOutside is metres past the wall, zero inside it.
	DistanceOutside float64 `json:"distanceOutside"`

	Inventory []InventoryLine `json:"inventory"`

	// Equipment is what they are wearing, by slot. Empty slots are omitted.
	Equipment []EquipmentLine `json:"equipment"`
}

// InventoryLine is one stack: an item and how many of it.
type InventoryLine struct {
	Item  string `json:"item"`
	Count int    `json:"count"`
}

// EquipmentLine is one filled slot: where it is worn and what it is. Worn equipment differs from
// the same item carried in the inventory.
type EquipmentLine struct {
	// Slot is the game's own name for it: Head, Body, Tank.
	Slot string `json:"slot"`
	Item string `json:"item"`
}

// Event is one line of the feed, projected from an audit record. Kind is the audit log's own word.
type Event struct {
	Sequence int64 `json:"seq"`

	// Group and Generation name the match this event belongs to. A push can straddle a reset, so
	// this is not inferred from the snapshot.
	Group      int `json:"group"`
	Generation int `json:"generation"`

	At        time.Time `json:"at"`
	ElapsedMs int64     `json:"elapsedMs"`
	Kind      string    `json:"kind"`

	Player *string `json:"player"`

	// Actor is the other player: who fired the ring, who gets the kill.
	Actor  *string  `json:"actor"`
	Phase  *int     `json:"phase"`
	Amount *float64 `json:"amount"`
	Detail *string  `json:"detail"`

	// X, Y and Z are nil for anything with no place; (0, 0, 0) is a real spot in the Safe Shallows.
	X *float64 `json:"x"`
	Y *float64 `json:"y"`
	Z *float64 `json:"z"`

	Biome *string `json:"biome"`
}

// Elapsed is match time, which is what a timeline is drawn against.
func (e Event) Elapsed() time.Duration {
	return time.Duration(e.ElapsedMs) * time.Millisecond
}

// ForSending normalizes a snapshot built here so it meets the contract: nil slices become empty
// arrays (players, events, pods, inventory, equipment, standings rows and games), and the time is
// UTC truncated to milliseconds.
func ForSending(snapshot Snapshot) Snapshot {
	snapshot.At = snapshot.At.UTC().Truncate(time.Millisecond)

	if snapshot.Players == nil {
		snapshot.Players = []Player{}
	}

	if snapshot.Events == nil {
		snapshot.Events = []Event{}
	}

	if snapshot.Pods == nil {
		snapshot.Pods = []Pod{}
	}

	for i := range snapshot.Players {
		if snapshot.Players[i].Inventory == nil {
			snapshot.Players[i].Inventory = []InventoryLine{}
		}

		if snapshot.Players[i].Equipment == nil {
			snapshot.Players[i].Equipment = []EquipmentLine{}
		}
	}

	// A nil Standings means no table, but a non-nil one must have array rows.
	if snapshot.Standings != nil && (snapshot.Standings.Entries == nil || snapshot.Standings.Games == nil) {
		table := *snapshot.Standings
		if table.Entries == nil {
			table.Entries = []StandingsLine{}
		}

		// The same for games.
		if table.Games == nil {
			table.Games = []GameResult{}
		}

		snapshot.Standings = &table
	}

	return snapshot
}

// Parse reads one pushed payload and refuses any version other than Version, so a newer mod's
// fields are never silently dropped.
func Parse(payload []byte) (Snapshot, error) {
	var snapshot Snapshot

	if err := json.Unmarshal(payload, &snapshot); err != nil {
		return Snapshot{}, fmt.Errorf("telemetry: %w", err)
	}

	if snapshot.Version != Version {
		return Snapshot{}, fmt.Errorf("telemetry: payload is version %d, this service speaks %d",
			snapshot.Version, Version)
	}

	return snapshot, nil
}
