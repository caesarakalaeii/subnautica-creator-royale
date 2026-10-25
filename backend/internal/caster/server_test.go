package caster_test

import (
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/caster"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

func testConfig(t *testing.T) config.Config {
	t.Helper()

	cfg, err := config.Load(func(string) string { return "" })
	if err != nil {
		t.Fatalf("default config should load: %v", err)
	}
	return cfg
}

func discard() *slog.Logger {
	return slog.New(slog.NewJSONHandler(io.Discard, nil))
}

func TestHealthzAnswersWithSomethingAProbeCanAssertOn(t *testing.T) {
	t.Parallel()

	srv := httptest.NewServer(caster.NewServer(testConfig(t), discard()).Handler())
	defer srv.Close()

	res, err := http.Get(srv.URL + "/healthz")
	if err != nil {
		t.Fatalf("GET /healthz: %v", err)
	}
	defer res.Body.Close()

	if res.StatusCode != http.StatusOK {
		t.Errorf("status = %d, want 200", res.StatusCode)
	}
	if got := res.Header.Get("Content-Type"); got != "application/json" {
		t.Errorf("Content-Type = %q, want application/json", got)
	}

	var body struct {
		Status string `json:"status"`
	}
	if err := json.NewDecoder(res.Body).Decode(&body); err != nil {
		t.Fatalf("body is not JSON: %v", err)
	}
	if body.Status != "ok" {
		t.Errorf("status field = %q, want %q", body.Status, "ok")
	}
}

func TestAnUnknownRouteIs404AndNotAPanic(t *testing.T) {
	t.Parallel()

	srv := httptest.NewServer(caster.NewServer(testConfig(t), discard()).Handler())
	defer srv.Close()

	res, err := http.Get(srv.URL + "/no/such/thing")
	if err != nil {
		t.Fatalf("GET unknown route: %v", err)
	}
	defer res.Body.Close()

	if res.StatusCode != http.StatusNotFound {
		t.Errorf("status = %d, want 404", res.StatusCode)
	}
}

func TestPostingToHealthzIsRejectedRatherThanTreatedAsAProbe(t *testing.T) {
	t.Parallel()

	srv := httptest.NewServer(caster.NewServer(testConfig(t), discard()).Handler())
	defer srv.Close()

	res, err := http.Post(srv.URL+"/healthz", "application/json", nil)
	if err != nil {
		t.Fatalf("POST /healthz: %v", err)
	}
	defer res.Body.Close()

	if res.StatusCode != http.StatusMethodNotAllowed {
		t.Errorf("status = %d, want 405", res.StatusCode)
	}
}

func TestShutdownDrainsAnInFlightRequest(t *testing.T) {
	t.Parallel()

	started := make(chan struct{})
	slow := http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		close(started)
		time.Sleep(150 * time.Millisecond)
		_, _ = w.Write([]byte("finished"))
	})

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	served := make(chan error, 1)
	go func() { served <- caster.Serve(ctx, listener, slow, 5*time.Second, discard()) }()

	type result struct {
		status int
		body   string
		err    error
	}
	responses := make(chan result, 1)
	go func() {
		res, err := http.Get("http://" + listener.Addr().String() + "/slow")
		if err != nil {
			responses <- result{err: err}
			return
		}
		defer res.Body.Close()
		body, err := io.ReadAll(res.Body)
		responses <- result{status: res.StatusCode, body: string(body), err: err}
	}()

	<-started
	cancel() // the SIGTERM equivalent, while the request is still in flight

	got := <-responses
	if got.err != nil {
		t.Fatalf("in-flight request was dropped: %v", got.err)
	}
	if got.status != http.StatusOK || got.body != "finished" {
		t.Errorf("got %d %q, want 200 %q", got.status, got.body, "finished")
	}

	if err := <-served; err != nil {
		t.Errorf("Serve returned %v, want nil after a clean shutdown", err)
	}
}

func TestServeStopsWhenTheContextIsAlreadyCancelled(t *testing.T) {
	t.Parallel()

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	done := make(chan error, 1)
	go func() {
		done <- caster.Serve(ctx, listener, http.NotFoundHandler(), time.Second, discard())
	}()

	select {
	case err := <-done:
		if err != nil {
			t.Errorf("Serve returned %v, want nil", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("Serve did not return for a cancelled context")
	}
}
