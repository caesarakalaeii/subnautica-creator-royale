package caster

import (
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// TestABoothIsCreatedFedByReplayAndDrawnByItsDashboard uses no fakes in the path: a real HTTP
// client, the `cmd/replay` binary pushing over a socket, and a real WebSocket feed.
func TestABoothIsCreatedFedByReplayAndDrawnByItsDashboard(t *testing.T) {
	replay := buildReplay(t)

	registry := newFakeRegistry()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth("tournament-push", "tournament-view", "tournament-write")}
	service := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(registry)

	booth := httptest.NewServer(service.Handler())
	t.Cleanup(booth.Close)

	made := createOver(t, booth.URL)

	// The dashboard opens before the match starts, so its first message is empty.
	feed := "ws" + strings.TrimPrefix(booth.URL, "http") +
		strings.TrimSuffix(made.Dashboard, "/") + livePath + "?token=" + made.View

	connection, ctx := dial(t, feed)

	first := readSnapshot(t, ctx, connection)
	if len(first.Players) != 0 {
		t.Fatalf("a booth that has never been pushed to draws %+v", first.Players)
	}

	// `cmd/replay` takes the push token from the environment, as the game server does.
	pushed := exec.Command(replay, "-interval", "10ms",
		filepath.Join("..", "..", "..", "docs", "telemetry", "example.json"),
		booth.URL+made.IngestURL)
	pushed.Env = append(os.Environ(), "LMS_INGEST_TOKEN="+made.Ingest)

	if output, err := pushed.CombinedOutput(); err != nil {
		t.Fatalf("replay against the booth it was told about: %v\n%s", err, output)
	}

	// Checked before reading the feed, whose failure mode is a read that never returns.
	if _, tookIt := service.Booth(made.ID).Store().Latest(); !tookIt {
		t.Fatalf("replay pushed to %s and the booth holds nothing", booth.URL+made.IngestURL)
	}

	// Same connection as before the match: this is the push arriving live, not a replay on reload.
	drawn := readSnapshot(t, ctx, connection)
	if len(drawn.Players) == 0 {
		t.Fatal("the dashboard drew no players after the match was replayed into the booth")
	}

	// The reserved tournament booth saw none of it.
	if snapshot, pushedTo := service.Booth(config.TournamentBooth).Store().Latest(); pushedTo {
		t.Errorf("the tournament booth holds %+v after a stranger's replay", snapshot.Players)
	}
}

// buildReplay compiles `cmd/replay` and returns the path to the binary.
func buildReplay(t *testing.T) string {
	t.Helper()

	binary := filepath.Join(t.TempDir(), "replay")

	build := exec.Command("go", "build", "-o", binary, "./cmd/replay")
	build.Dir = filepath.Join("..", "..")

	if output, err := build.CombinedOutput(); err != nil {
		t.Fatalf("build cmd/replay: %v\n%s", err, output)
	}

	return binary
}

// createOver asks a running service for a booth over HTTP, the way the creation page does.
func createOver(t *testing.T, base string) createdBooth {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)

	request, err := http.NewRequestWithContext(ctx, http.MethodPost, base+boothsPath, nil)
	if err != nil {
		t.Fatalf("build the creation request: %v", err)
	}

	response, err := http.DefaultClient.Do(request)
	if err != nil {
		t.Fatalf("POST %s: %v", boothsPath, err)
	}
	defer func() { _ = response.Body.Close() }()

	if response.StatusCode != http.StatusCreated {
		body, _ := io.ReadAll(io.LimitReader(response.Body, 1<<12))
		t.Fatalf("POST %s: status = %d: %s", boothsPath, response.StatusCode, body)
	}

	var made createdBooth
	if err := json.NewDecoder(response.Body).Decode(&made); err != nil {
		t.Fatalf("the creation reply is not JSON: %v", err)
	}

	return made
}
