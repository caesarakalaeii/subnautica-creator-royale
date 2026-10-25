package web_test

import (
	"net/http"
	"net/http/httptest"
	"runtime"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/web"
)

// offer stands in for the AGPL §13 link the service substitutes into every page.
const offer = `<a href="https://example.invalid">source</a>`

func get(t *testing.T, path string) *http.Response {
	t.Helper()

	recorder := httptest.NewRecorder()
	web.Handler(offer).ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, path, nil))

	return recorder.Result()
}

func TestRootServesTheDashboardPage(t *testing.T) {
	response := get(t, "/")

	if response.StatusCode != http.StatusOK {
		t.Fatalf("got %d, want 200", response.StatusCode)
	}

	if contentType := response.Header.Get("Content-Type"); !strings.HasPrefix(contentType, "text/html") {
		t.Errorf("got Content-Type %q, want text/html", contentType)
	}
}

func TestTheStylesheetAndTheModulesAreServedWithTypesABrowserWillExecute(t *testing.T) {
	// Browsers refuse a module served as text/plain under strict MIME checking.
	for path, want := range map[string]string{
		"/style.css":       "text/css",
		"/overlay.css":     "text/css",
		"/view.js":         "javascript",
		"/feed.js":         "javascript",
		"/events.js":       "javascript",
		"/overlays.js":     "javascript",
		"/overlay-page.js": "javascript",
		"/mapview.js":      "javascript",
	} {
		response := get(t, path)

		if response.StatusCode != http.StatusOK {
			t.Errorf("%s: got %d, want 200", path, response.StatusCode)
			continue
		}

		if contentType := response.Header.Get("Content-Type"); !strings.Contains(contentType, want) {
			t.Errorf("%s: got Content-Type %q, want it to contain %q", path, contentType, want)
		}
	}
}

func TestAPageThatWasRedeployedIsNotServedFromTheBrowserCache(t *testing.T) {
	// The assets carry no content hash, so they must be revalidated on every load.
	response := get(t, "/")

	if cacheControl := response.Header.Get("Cache-Control"); !strings.Contains(cacheControl, "no-cache") {
		t.Errorf("got Cache-Control %q, want it to ask for revalidation", cacheControl)
	}
}

func TestSomethingThatIsNotThereIsNotFound(t *testing.T) {
	if response := get(t, "/nothing-here.js"); response.StatusCode != http.StatusNotFound {
		t.Errorf("got %d, want 404", response.StatusCode)
	}
}

// Scene collections reference the overlay by path, so a rename would blank the scene.
func TestTheOverlayPageIsServedWhereAScenePointsAtIt(t *testing.T) {
	response := get(t, "/overlay.html")

	if response.StatusCode != http.StatusOK {
		t.Fatalf("got %d, want 200", response.StatusCode)
	}

	if contentType := response.Header.Get("Content-Type"); !strings.HasPrefix(contentType, "text/html") {
		t.Errorf("got Content-Type %q, want text/html", contentType)
	}
}

func TestTheMapsTerrainIsServed(t *testing.T) {
	response := get(t, "/map/terrain.png")

	if response.StatusCode != http.StatusOK {
		t.Fatalf("got %d, want 200", response.StatusCode)
	}

	if contentType := response.Header.Get("Content-Type"); contentType != "image/png" {
		t.Errorf("got Content-Type %q, want image/png", contentType)
	}
}

func TestTheRealMeshIsServedFromTheEmbeddedTreeWithoutBeingCopiedWhole(t *testing.T) {
	// handler_test.go proves the streaming branch over fstest.MapFS; this proves the embedded tree
	// takes it too. A range response alone cannot tell the branches apart, so measure allocation.
	// No t.Parallel(): TotalAlloc is process-wide.
	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/map/worldmesh.bin.gz", nil)
	request.Header.Set("Range", "bytes=0-15")

	var before, after runtime.MemStats

	runtime.GC()
	runtime.ReadMemStats(&before)

	web.Handler(offer).ServeHTTP(recorder, request)

	runtime.ReadMemStats(&after)

	if recorder.Code != http.StatusPartialContent {
		t.Fatalf("got %d, want 206", recorder.Code)
	}

	if got := recorder.Body.Len(); got != 16 {
		t.Errorf("got %d bytes, want 16", got)
	}

	// The total after the slash is the whole file's size, which lets a browser resume.
	if got := recorder.Header().Get("Content-Range"); !strings.HasPrefix(got, "bytes 0-15/") {
		t.Errorf("got Content-Range %q, want it to start with %q", got, "bytes 0-15/")
	}

	// One MiB separates the branches without pinning an exact allocation count.
	if allocated := after.TotalAlloc - before.TotalAlloc; allocated > 1<<20 {
		t.Errorf("asking for 16 bytes of the mesh allocated %d bytes, want at most %d -- "+
			"the handler read the whole file rather than seeking into it", allocated, 1<<20)
	}
}
