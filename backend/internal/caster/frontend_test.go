package caster

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func request(t *testing.T, method, path string) *httptest.ResponseRecorder {
	t.Helper()

	recorder := httptest.NewRecorder()
	newTestServer(t, token).Handler().ServeHTTP(recorder, httptest.NewRequest(method, path, nil))

	return recorder
}

func TestTheBoothServesTheDashboardThatWatchesIt(t *testing.T) {
	recorder := request(t, http.MethodGet, "/")

	if recorder.Code != http.StatusOK {
		t.Fatalf("GET /: status = %d, want 200", recorder.Code)
	}

	if body := recorder.Body.String(); !strings.Contains(body, "last man standing") {
		t.Errorf("GET / did not serve the dashboard page: %.120q", body)
	}
}

func TestTheBoothServesTheOperatorConsole(t *testing.T) {
	recorder := request(t, http.MethodGet, "/console.html")

	if recorder.Code != http.StatusOK {
		t.Fatalf("GET /console.html: status = %d, want 200", recorder.Code)
	}

	if body := recorder.Body.String(); !strings.Contains(body, "operator console") {
		t.Errorf("GET /console.html did not serve the console: %.120q", body)
	}
}

func TestTheConsolePageDoesNotAnswerForTheConsoleRoute(t *testing.T) {
	recorder := request(t, http.MethodGet, consolePath)

	// /console (the token-guarded route, 503 here with no token configured) vs /console.html.
	if recorder.Code == http.StatusOK {
		t.Fatalf("GET %s served something: %.120q", consolePath, recorder.Body.String())
	}

	if strings.Contains(recorder.Body.String(), "<!doctype html>") {
		t.Errorf("GET %s answered with the page rather than the route", consolePath)
	}
}

// The front-end is the fallback route; a wrong method on an endpoint must still be a 405, not 404.
func TestTheFrontEndDoesNotAnswerForTheServicesOwnEndpoints(t *testing.T) {
	for _, endpoint := range []struct {
		method, path, allow string
	}{
		{http.MethodGet, ingestPath, http.MethodPost},
		{http.MethodPost, livePath, http.MethodGet},
		{http.MethodPost, healthPath, "GET, HEAD"},
		{http.MethodGet, commandsPath, http.MethodPost},
		{http.MethodGet, peoplePath, http.MethodPut},
		{http.MethodGet, overridesPath, http.MethodPost},
		{http.MethodPut, consolePath, "GET, POST"},
	} {
		recorder := request(t, endpoint.method, endpoint.path)

		if recorder.Code != http.StatusMethodNotAllowed {
			t.Errorf("%s %s: status = %d, want 405", endpoint.method, endpoint.path, recorder.Code)
		}

		if allow := recorder.Header().Get("Allow"); allow != endpoint.allow {
			t.Errorf("%s %s: Allow = %q, want %q", endpoint.method, endpoint.path, allow, endpoint.allow)
		}
	}
}
