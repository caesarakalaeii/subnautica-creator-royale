package caster

import (
	"net/http"
	"strings"
	"testing"
)

// everyPage is every page this service serves, listed by hand rather than walked from the embedded
// tree so a newly added page has to be added here too.
var everyPage = []string{
	"/",             // the dashboard a caster watches
	"/home.html",    // the front door: creation, the OBS sources and links
	"/overlay.html", // the OBS browser sources, which is what a viewer sees
	"/bracket.html", // the standings, linked and shared
	"/console.html", // the operator console
	"/events.html",  // the feed's archive
}

// TestEveryServedPageCarriesASourceLink enforces the AGPL §13 offer of Corresponding Source to
// network users.
func TestEveryServedPageCarriesASourceLink(t *testing.T) {
	t.Parallel()

	server := newTestServer(t, token)

	for _, page := range everyPage {
		recorder := ask(t, server, http.MethodGet, page, "", "")

		if recorder.Code != http.StatusOK {
			t.Errorf("GET %s: status = %d, want the page", page, recorder.Code)

			continue
		}

		body := recorder.Body.String()
		if !strings.Contains(body, SourceURL) {
			t.Errorf("GET %s does not carry the source link %q", page, SourceURL)
		}
	}
}

func TestTheSourceLinkNamesTheRunningRevision(t *testing.T) {
	t.Parallel()

	server := newTestServer(t, token)

	for _, page := range everyPage {
		body := ask(t, server, http.MethodGet, page, "", "").Body.String()

		if !strings.Contains(body, Revision()) {
			t.Errorf("GET %s carries a source link that does not name the running revision %q",
				page, Revision())
		}
	}
}

func TestTheOverlayPagesCarryTheSourceLinkToo(t *testing.T) {
	t.Parallel()

	server := newTestServer(t, token)

	body := ask(t, server, http.MethodGet, "/overlay.html", "", "").Body.String()

	if !strings.Contains(body, SourceURL) {
		t.Errorf("the overlay page does not carry the source link %q", SourceURL)
	}

	if !strings.Contains(body, Revision()) {
		t.Errorf("the overlay page's source link does not name the revision %q", Revision())
	}
}

func TestABoothsOwnPagesCarryTheSourceLink(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	made := create(t, server)

	for _, page := range []string{made.Dashboard, made.Dashboard + "overlay.html"} {
		body := ask(t, server, http.MethodGet, page, "", "").Body.String()

		if !strings.Contains(body, SourceURL) {
			t.Errorf("GET %s does not carry the source link", page)
		}
	}
}

func TestARevisionThatIsNotACommitIdIsTreatedAsUnknown(t *testing.T) {
	t.Parallel()

	for _, stamped := range []string{
		"",                        // an unstamped build
		"main",                    // a branch
		"v1.2.3",                  // a tag
		"abc",                     // shorter than any abbreviation git would print
		"../../etc/passwd",        // a path
		`" onmouseover="alert(1)`, // an attribute break
		"deadbeef?x=1",            // a query, which HTML escaping does not touch
	} {
		if got := revisionOf(stamped); got != unknownRevision {
			t.Errorf("revisionOf(%q) = %q, want %q", stamped, got, unknownRevision)
		}
	}
}

func TestACommitIdIsTakenAndLinkedTo(t *testing.T) {
	t.Parallel()

	const stamped = "613f891b98ec17d438b1ebce25877d3c182a2f16"

	if got := revisionOf(stamped); got != stamped {
		t.Fatalf("revisionOf(%q) = %q, want it taken", stamped, got)
	}

	offer := offerOf(stamped)

	if !strings.Contains(offer, SourceURL+"/tree/"+stamped) {
		t.Errorf("the offer does not link to the revision's tree: %s", offer)
	}

	// Shown short; the full id is in the href.
	if !strings.Contains(offer, ">source (AGPL-3.0) · 613f891b98ec<") {
		t.Errorf("the offer does not name the revision readably: %s", offer)
	}
}

func TestAnUnknownRevisionStillLinksToTheRepository(t *testing.T) {
	t.Parallel()

	offer := offerOf(unknownRevision)

	if !strings.Contains(offer, `href="`+SourceURL+`"`) {
		t.Errorf("the offer does not link to the repository: %s", offer)
	}

	if strings.Contains(offer, "/tree/") {
		t.Errorf("the offer links to a tree for a revision nobody knows: %s", offer)
	}
}
