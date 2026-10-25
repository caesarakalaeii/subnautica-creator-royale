package web

import (
	"io/fs"
	"net/http"
	"net/http/httptest"
	"runtime"
	"strings"
	"testing"
	"testing/fstest"
)

// files stands in for the embedded tree, with a directory and a nested file like the map assets.
var files = fstest.MapFS{
	"index.html":     {Data: []byte("<!-- root -->")},
	"map/heightmap":  {Data: []byte("terrain")},
	"map/asset.json": {Data: []byte("{}")},
}

func status(t *testing.T, path string) int {
	t.Helper()

	recorder := httptest.NewRecorder()
	newHandler(files).ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, path, nil))

	return recorder.Code
}

func TestADirectoryIsNotBrowsable(t *testing.T) {
	// http.FileServer would list a directory without an index; a listing must never be served.
	for _, path := range []string{"/map/", "/map"} {
		if code := status(t, path); code != http.StatusNotFound {
			t.Errorf("%s: got %d for a directory, want 404", path, code)
		}
	}
}

func TestANestedFileIsStillServed(t *testing.T) {
	if code := status(t, "/map/heightmap"); code != http.StatusOK {
		t.Errorf("got %d, want 200", code)
	}
}

func TestATraversalAttemptDoesNotEscapeTheServedTree(t *testing.T) {
	for _, path := range []string{"/../web.go", "/map/../../web.go", "//etc/passwd", "/./../go.mod"} {
		if code := status(t, path); code == http.StatusOK {
			t.Errorf("%s: served something, want it refused", path)
		}
	}
}

func TestAVerbThePageCannotAnswerIsSaidToBeTheWrongVerb(t *testing.T) {
	// The handler is the fallback for every method, so it must refuse wrong verbs itself.
	recorder := httptest.NewRecorder()
	newHandler(files).ServeHTTP(recorder, httptest.NewRequest(http.MethodPost, "/", nil))

	if recorder.Code != http.StatusMethodNotAllowed {
		t.Errorf("got %d, want 405", recorder.Code)
	}

	if allow := recorder.Header().Get("Allow"); allow != "GET, HEAD" {
		t.Errorf("Allow = %q, want %q", allow, "GET, HEAD")
	}
}

func TestHeadIsAnswered(t *testing.T) {
	recorder := httptest.NewRecorder()
	newHandler(files).ServeHTTP(recorder, httptest.NewRequest(http.MethodHead, "/", nil))

	if recorder.Code != http.StatusOK {
		t.Errorf("got %d, want 200", recorder.Code)
	}
}

// discard is a ResponseWriter that counts the body and throws it away, so a recorder holding the
// body does not distort the allocation measurement.
type discard struct {
	header http.Header
	code   int
	n      int64
}

func (d *discard) Header() http.Header {
	if d.header == nil {
		d.header = http.Header{}
	}

	return d.header
}

func (d *discard) Write(p []byte) (int, error) {
	d.n += int64(len(p))

	return len(p), nil
}

func (d *discard) WriteHeader(code int) {
	d.code = code
}

func TestABigFileIsStreamedRatherThanCopiedOntoTheHeap(t *testing.T) {
	// No t.Parallel(): TotalAlloc is process-wide.
	const size = 32 << 20

	handler := newHandler(fstest.MapFS{"map/worldmesh.bin.gz": {Data: make([]byte, size)}})

	var before, after runtime.MemStats

	runtime.GC()
	runtime.ReadMemStats(&before)

	// Four concurrent fetches, which would buffer four copies of the file in a read-whole handler.
	for i := 0; i < 4; i++ {
		writer := &discard{}
		handler.ServeHTTP(writer, httptest.NewRequest(http.MethodGet, "/map/worldmesh.bin.gz", nil))

		// Asserted so a handler that serves nothing is not mistaken for a memory improvement.
		if writer.code != http.StatusOK {
			t.Fatalf("got %d, want 200", writer.code)
		}

		if writer.n != size {
			t.Fatalf("wrote %d bytes, want %d", writer.n, size)
		}
	}

	runtime.ReadMemStats(&after)

	// A quarter of one copy across four responses: room for the copy buffer and headers, far below a
	// whole copy per request.
	if allocated := after.TotalAlloc - before.TotalAlloc; allocated > size/4 {
		t.Errorf("serving a %d-byte file four times allocated %d bytes, want at most %d",
			size, allocated, size/4)
	}
}

func TestAPieceOfAFileCanBeAskedForWithoutTheWholeFile(t *testing.T) {
	// Ranges let a browser resume an interrupted mesh download.
	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/map/heightmap", nil)
	request.Header.Set("Range", "bytes=0-3")

	newHandler(files).ServeHTTP(recorder, request)

	if recorder.Code != http.StatusPartialContent {
		t.Fatalf("got %d, want 206", recorder.Code)
	}

	if body := recorder.Body.String(); body != "terr" {
		t.Errorf("got body %q, want %q", body, "terr")
	}

	if got := recorder.Header().Get("Content-Range"); got != "bytes 0-3/7" {
		t.Errorf("got Content-Range %q, want %q", got, "bytes 0-3/7")
	}
}

// The source offer is in the HTML rather than added by script, so it is present with script off or
// in a saved page.
func TestAPagesSourcePlaceholderIsReplacedWithTheOffer(t *testing.T) {
	const offer = `<a href="https://example.invalid/tree/abc123">source</a>`

	pages := fstest.MapFS{
		"index.html": {Data: []byte("<body>" + SourcePlaceholder + "</body>")},
	}

	recorder := httptest.NewRecorder()
	newSourcedHandler(pages, offer).ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, "/", nil))

	body := recorder.Body.String()

	if !strings.Contains(body, offer) {
		t.Errorf("the page does not carry the offer: %q", body)
	}

	if strings.Contains(body, SourcePlaceholder) {
		t.Errorf("the placeholder is still in the served page: %q", body)
	}
}

// The mesh is large binary; a substitution pass over it would be wrong and expensive.
func TestOnlyHtmlIsRewritten(t *testing.T) {
	pages := fstest.MapFS{
		"style.css":  {Data: []byte("/* " + SourcePlaceholder + " */")},
		"index.html": {Data: []byte("<body>" + SourcePlaceholder + "</body>")},
	}

	recorder := httptest.NewRecorder()
	handler := newSourcedHandler(pages, "OFFER")
	handler.ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, "/style.css", nil))

	if body := recorder.Body.String(); !strings.Contains(body, SourcePlaceholder) {
		t.Errorf("the stylesheet was rewritten: %q", body)
	}
}

func TestAPageWithNoPlaceholderIsServedUnchanged(t *testing.T) {
	const page = "<body>no marker here</body>"

	pages := fstest.MapFS{"index.html": {Data: []byte(page)}}

	recorder := httptest.NewRecorder()
	newSourcedHandler(pages, "OFFER").ServeHTTP(recorder, httptest.NewRequest(http.MethodGet, "/", nil))

	if body := recorder.Body.String(); body != page {
		t.Errorf("got %q, want the page unchanged", body)
	}
}

// The page rewrite reads whole files, so big non-HTML files must stay on the streaming path.
func TestRewritingDoesNotPutABigFileOnTheHeap(t *testing.T) {
	// No t.Parallel(): TotalAlloc is process-wide.
	const size = 32 << 20

	handler := newSourcedHandler(fstest.MapFS{"map/worldmesh.bin.gz": {Data: make([]byte, size)}}, "OFFER")

	var before, after runtime.MemStats

	runtime.GC()
	runtime.ReadMemStats(&before)

	writer := &discard{}
	handler.ServeHTTP(writer, httptest.NewRequest(http.MethodGet, "/map/worldmesh.bin.gz", nil))

	runtime.ReadMemStats(&after)

	if writer.n != size {
		t.Fatalf("wrote %d bytes, want %d", writer.n, size)
	}

	if allocated := after.TotalAlloc - before.TotalAlloc; allocated > size/4 {
		t.Errorf("serving a %d-byte file allocated %d bytes, want at most %d",
			size, allocated, size/4)
	}
}

// Walks the tree, so a page added without the placeholder fails here.
func TestEveryEmbeddedPageMarksWhereTheSourceOfferGoes(t *testing.T) {
	pages := 0

	tree := embeddedPages(t)

	err := fs.WalkDir(tree, ".", func(name string, entry fs.DirEntry, err error) error {
		if err != nil || entry.IsDir() || !strings.HasSuffix(name, ".html") {
			return err
		}

		pages++

		body, err := fs.ReadFile(tree, name)
		if err != nil {
			return err
		}

		if !strings.Contains(string(body), SourcePlaceholder) {
			t.Errorf("%s does not mark where the source offer goes; add %s to it",
				name, SourcePlaceholder)
		}

		return nil
	})
	if err != nil {
		t.Fatalf("walking the front-end: %v", err)
	}

	// A walk that found nothing would pass silently.
	if pages == 0 {
		t.Fatal("no pages were found, so this test asserted nothing")
	}
}

// embeddedPages is the real embedded front-end.
func embeddedPages(t *testing.T) fs.FS {
	t.Helper()

	tree, err := fs.Sub(static, "static")
	if err != nil {
		t.Fatalf("reading the embedded front-end: %v", err)
	}

	return tree
}
