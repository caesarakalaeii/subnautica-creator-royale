package caster

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// publishedServer is a one-booth server whose reserved booth is published.
func publishedServer(t *testing.T, records Records) *Server {
	t.Helper()

	booths := oneBooth(token, "watching", operatorToken)
	booths[0].Published = true

	cfg := config.Config{
		Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths:     booths,
		IngestRate: 20, FinalSeats: 5, CommandTTL: 2 * time.Minute,
	}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithRecords(everyBooth(records))
}

func TestThePublishedBracketAnswersWithoutAViewToken(t *testing.T) {
	t.Parallel()

	recorder := ask(t, publishedServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "")
	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}
}

func TestThePublishedBracketCarriesNoPositionsAndNoLiveMatch(t *testing.T) {
	t.Parallel()

	server := publishedServer(t, &fakeRecords{})

	snapshot := pushedTable()
	snapshot.Match = &telemetry.Match{Group: 3, Generation: 1, State: "Running", Alive: 4}
	snapshot.Players = []telemetry.Player{{Name: "Vela", Alive: true, X: 123.456, Y: -42, Z: 88.9}}
	server.Store().Apply(snapshot)

	body := ask(t, server, http.MethodGet, bracketPath, "", "").Body.String()

	for _, forbidden := range []string{"123.456", "88.9", "players", "alive", "insideZone", "Running"} {
		if strings.Contains(body, forbidden) {
			t.Errorf("bracket carries %q:\n%s", forbidden, body)
		}
	}
}

func TestThePublishedBracketServesTheSameDocumentItsTokenDoes(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := publishedServer(t, records)
	server.Store().Apply(pushedTable())

	bracket := readBracket(t, ask(t, server, http.MethodGet, bracketPath, "", ""))

	if bracket.Standings == nil || bracket.Standings.Entries[0].Name != "Vela" {
		t.Errorf("standings = %+v, want the push unnamed", bracket.Standings)
	}

	if bracket.Final.Seats != 5 {
		t.Errorf("seats = %d, want the configured 5", bracket.Final.Seats)
	}

	if bracket.Records != recordsOK {
		t.Errorf("records = %q, want %q", bracket.Records, recordsOK)
	}
}

func TestThePublishedReplySaysItIsPublic(t *testing.T) {
	t.Parallel()

	var reply map[string]any
	if err := json.Unmarshal(
		ask(t, publishedServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "").Body.Bytes(),
		&reply,
	); err != nil {
		t.Fatalf("bracket is not JSON: %v", err)
	}

	if reply["public"] != true {
		t.Errorf("public = %v (%v), want true and present", reply["public"], reply)
	}
}

func TestTheUnpublishedReplyDoesNotMentionPublicAtAll(t *testing.T) {
	t.Parallel()

	var reply map[string]any
	if err := json.Unmarshal(
		ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "watching").Body.Bytes(),
		&reply,
	); err != nil {
		t.Fatalf("bracket is not JSON: %v", err)
	}

	if _, present := reply["public"]; present {
		t.Errorf("public = %v, want the field absent on an unpublished booth", reply["public"])
	}
}

func TestAPublishedBoothStillServesATokenThatIsPresented(t *testing.T) {
	t.Parallel()

	recorder := ask(t, publishedServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "watching")
	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}
}

func TestAPublishedBoothServesWithoutAViewTokenConfigured(t *testing.T) {
	t.Parallel()

	booths := oneBooth(token, "", operatorToken)
	booths[0].Published = true

	cfg := config.Config{
		Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths:     booths,
		IngestRate: 20, FinalSeats: 5, CommandTTL: 2 * time.Minute,
	}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).
		WithRecords(everyBooth(&fakeRecords{}))

	recorder := ask(t, server, http.MethodGet, bracketPath, "", "")
	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}
}

func TestTheUnpublishedBracketKeepsItsRefusal(t *testing.T) {
	t.Parallel()

	// Byte-identical refusals, so a caller cannot tell which booths could be published.
	t.Run("the body is exactly what it always was", func(t *testing.T) {
		t.Parallel()

		recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "")

		if recorder.Code != http.StatusUnauthorized {
			t.Fatalf("status = %d, want 401", recorder.Code)
		}

		if body := recorder.Body.String(); body != "{\"error\":\"bad or missing view token\"}\n" {
			t.Errorf("body = %q, want the one sentence unchanged", body)
		}
	})

	t.Run("a token an unpublished booth does hold is refused the same way", func(t *testing.T) {
		t.Parallel()

		recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "wrong")
		if recorder.Code != http.StatusUnauthorized {
			t.Fatalf("status = %d, want 401", recorder.Code)
		}

		if body := recorder.Body.String(); body != "{\"error\":\"bad or missing view token\"}\n" {
			t.Errorf("body = %q, want the one sentence unchanged", body)
		}
	})

	t.Run("a token an unpublished booth does not hold is refused the same way", func(t *testing.T) {
		t.Parallel()

		recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "Ada-view")
		if recorder.Code != http.StatusUnauthorized {
			t.Fatalf("status = %d, want 401", recorder.Code)
		}

		if body := recorder.Body.String(); body != "{\"error\":\"bad or missing view token\"}\n" {
			t.Errorf("body = %q, want the one sentence unchanged", body)
		}
	})
}

// TestAStaleTokenDoesNotLockAViewerOutOfAPublishedBracket: the page resends the browser's last view
// token, and refusing it would protect nothing since a tokenless request is answered.
func TestAStaleTokenDoesNotLockAViewerOutOfAPublishedBracket(t *testing.T) {
	t.Parallel()

	recorder := ask(t, publishedServer(t, &fakeRecords{}), http.MethodGet, bracketPath, "", "not-the-token")
	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d (%s), want 200", recorder.Code, recorder.Body.String())
	}
}
