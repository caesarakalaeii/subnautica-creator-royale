package caster

import (
	"html"
	"strings"
)

// SourceURL is where this service's Corresponding Source is. `backend/` is AGPL-3.0-or-later, so
// §13 requires offering it to anybody who interacts with a running booth over a network.
const SourceURL = "https://github.com/caesarakalaeii/subnautica-creator-royale"

// unknownRevision is what Revision answers when the build stamped nothing; the source link then
// points at the repository rather than a revision the binary might not be running.
const unknownRevision = "unknown"

// revision is the commit this binary was built from, stamped with
// `-ldflags "-X <module>/internal/caster.revision=$(git rev-parse HEAD)"` (see backend/Dockerfile).
// `debug.ReadBuildInfo` cannot supply it: the module root is not the git root and the image build
// context has no `.git`.
var revision string

// Revision is the commit this binary was built from, or "unknown".
func Revision() string {
	return revisionOf(revision)
}

// revisionOf is Revision over any stamp. Anything that is not a hexadecimal commit id reads as
// unknown, because the result is interpolated into a URL on every page.
func revisionOf(stamped string) string {
	stamped = strings.TrimSpace(stamped)
	if !isCommitID(stamped) {
		return unknownRevision
	}

	return stamped
}

// isCommitID is whether this is a git object name: hexadecimal, and long enough to be one.
func isCommitID(stamped string) bool {
	// Git's shortest abbreviation, and a full SHA-256 object name.
	const shortest, longest = 7, 64

	if len(stamped) < shortest || len(stamped) > longest {
		return false
	}

	for _, character := range stamped {
		switch {
		case character >= '0' && character <= '9', character >= 'a' && character <= 'f':
		default:
			return false
		}
	}

	return true
}

// sourceOffer is the AGPL §13 link, as the HTML that replaces web.SourcePlaceholder in every page.
// It links the exact revision rather than a branch, which may have moved since the build.
// Values are escaped even though Revision already allows only hex, as defence in depth.
func sourceOffer() string {
	return offerOf(Revision())
}

// offerOf is sourceOffer for an already resolved revision.
func offerOf(at string) string {
	href := SourceURL
	if at != unknownRevision {
		href += "/tree/" + at
	}

	// The link text shows a short id; the href carries the full revision.
	const shown = 12

	named := at
	if len(named) > shown {
		named = named[:shown]
	}

	return `<a class="source" href="` + html.EscapeString(href) + `" rel="nofollow">` +
		`source (AGPL-3.0) · ` + html.EscapeString(named) + `</a>`
}
