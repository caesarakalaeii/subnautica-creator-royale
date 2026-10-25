// Package web is the casting booth's front-end: the operator dashboard and the overlay pages OBS
// loads as browser sources. It is embedded in the binary, so the service and its front-end always
// deploy together.
//
// There is no build step: plain ES modules, one stylesheet, no npm dependencies. The pure logic in
// static/view.js is tested with `node --test`.
package web

import (
	"bytes"
	"embed"
	"io"
	"io/fs"
	"net/http"
	"path"
	"strings"
	"time"
)

// static is the front-end. No `all:` prefix, so files starting with a dot or underscore (editor
// leftovers) are never embedded.
//
//go:embed static
var static embed.FS

// indexFile is what the bare root serves.
const indexFile = "index.html"

// revalidate makes browsers revalidate on every load; the assets carry no content hash, so this
// guarantees a redeployed booth is the one being shown.
const revalidate = "no-cache"

// allowed is the methods this handler accepts. It is mounted for every method so it does not
// conflict with method-scoped routes, so it refuses other verbs itself.
const allowed = "GET, HEAD"

// SourcePlaceholder marks where a page puts the AGPL §13 source offer. An HTML comment, so a page
// with nothing substituted is still valid.
const SourcePlaceholder = "<!-- source-offer -->"

// Handler serves the front-end with `offer` substituted for SourcePlaceholder in every page.
//
// Mount it as the fallback route: Go's mux prefers more specific patterns such as /healthz,
// /ingest and /live.
func Handler(offer string) http.Handler {
	files, err := fs.Sub(static, "static")
	if err != nil {
		// Unreachable: the directory is embedded at compile time.
		panic("web: embedded front-end is not readable: " + err.Error())
	}

	return newSourcedHandler(files, offer)
}

// newHandler is Handler over any file system with nothing substituted, for testing against trees
// with subdirectories.
func newHandler(files fs.FS) http.Handler {
	return newSourcedHandler(files, "")
}

// newSourcedHandler is newHandler with the offer that replaces SourcePlaceholder in served HTML.
func newSourcedHandler(files fs.FS, offer string) http.Handler {
	return &handler{files: files, offer: offer}
}

type handler struct {
	files fs.FS

	// offer is the AGPL §13 link substituted for SourcePlaceholder in HTML. Fixed for the life of the
	// process, since it names the revision this binary was built from.
	offer string
}

func (h *handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		w.Header().Set("Allow", allowed)
		http.Error(w, "this path takes "+allowed, http.StatusMethodNotAllowed)
		return
	}

	name := strings.TrimPrefix(path.Clean("/"+r.URL.Path), "/")
	if name == "" {
		name = indexFile
	}

	// fs.ValidPath rejects what path.Clean cannot: absolute leftovers, surviving dot segments, empty
	// elements.
	if !fs.ValidPath(name) {
		http.NotFound(w, r)
		return
	}

	// HTML pages (all under 10 KB) are read whole and rewritten; everything else streams. Decided by
	// extension, before anything is read.
	if strings.HasSuffix(name, ".html") {
		h.servePage(w, r, name)

		return
	}

	// Opened, not read whole: the mesh is about 60 MB, and buffering it per concurrent request
	// would exhaust memory. Streaming costs one copy buffer per response.
	file, err := h.files.Open(name)
	if err != nil {
		http.NotFound(w, r)
		return
	}

	defer func() { _ = file.Close() }()

	// A directory opens fine and only fails on read, so Stat first: directories get a 404, never a
	// listing.
	info, err := file.Stat()
	if err != nil || info.IsDir() {
		http.NotFound(w, r)
		return
	}

	// Embedded files implement io.ReadSeeker, as does fstest.MapFS. Any other file system is read
	// whole as a defensive fallback; Handler never reaches it.
	content, seekable := file.(io.ReadSeeker)
	if !seekable {
		body, err := fs.ReadFile(h.files, name)
		if err != nil {
			http.NotFound(w, r)
			return
		}

		content = bytes.NewReader(body)
	}

	w.Header().Set("Cache-Control", revalidate)

	// A zero modification time disables Last-Modified negotiation: embedded files all carry the same
	// build time, which would be a misleading validator.
	http.ServeContent(w, r, name, time.Time{}, content)
}

// servePage answers one HTML page with the AGPL §13 offer substituted in. Read whole, since pages
// are small and the substitution needs the bytes; ServeContent still handles ranges, HEAD and
// Content-Type.
func (h *handler) servePage(w http.ResponseWriter, r *http.Request, name string) {
	page, err := fs.ReadFile(h.files, name)
	if err != nil {
		// A directory named `something.html` lands here too and gets the same 404.
		http.NotFound(w, r)

		return
	}

	// Unconditional: a page without the placeholder is returned unchanged.
	offered := bytes.ReplaceAll(page, []byte(SourcePlaceholder), []byte(h.offer))

	w.Header().Set("Cache-Control", revalidate)
	http.ServeContent(w, r, name, time.Time{}, bytes.NewReader(offered))
}
