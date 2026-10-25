package config

import (
	"fmt"
	"strings"
)

// TournamentBooth is the reserved id of the default booth, which the bare paths resolve to. Players
// cannot create a booth with this id.
const TournamentBooth = "tournament"

// boothsVariable names the booths this process serves. Empty means only the reserved booth.
const boothsVariable = "LMS_BOOTHS"

// Booth is one tenant: an id and the three tokens that address it. The id is always looked up
// from the presented credential, never taken from a payload.
type Booth struct {
	// ID is the booth's name in URL paths and in every database row it owns.
	ID string

	// IngestToken is what the game server presents to push telemetry. Required.
	IngestToken string

	// ViewToken is what a dashboard or overlay presents to read this booth's feed.
	ViewToken string

	// OperatorToken is what an operator presents to write to this booth. Empty makes the booth
	// read-only.
	OperatorToken string

	// Published is whether this booth's bracket is readable with no token. Set only through
	// LMS_PUBLIC_BRACKETS.
	Published bool
}

// loadBooths reads the booths this process serves. With LMS_BOOTHS unset, the bare token variables
// describe the reserved booth; with it set, each id has its own variables and the bare ones are
// ignored.
func loadBooths(cfg *Config, getenv func(string) string) error {
	publication, err := publicBooths(getenv)
	if err != nil {
		return err
	}

	named := lookup(getenv, boothsVariable)

	if named == "" {
		cfg.Booths = []Booth{{
			ID:            TournamentBooth,
			IngestToken:   getenv("LMS_INGEST_TOKEN"),
			ViewToken:     getenv("LMS_VIEW_TOKEN"),
			OperatorToken: getenv("LMS_OPERATOR_TOKEN"),
			Published:     publication[TournamentBooth],
		}}

		return nil
	}

	ids, err := boothIDs(named)
	if err != nil {
		return err
	}

	booths := make([]Booth, 0, len(ids))
	for _, id := range ids {
		booth := Booth{
			ID:            id,
			IngestToken:   getenv(boothVariable(id, "INGEST")),
			ViewToken:     getenv(boothVariable(id, "VIEW")),
			OperatorToken: getenv(boothVariable(id, "OPERATOR")),
			Published:     publication[id],
		}

		// A misspelt variable name looks like a missing token, so fail loudly rather than serve a booth
		// that never shows a match.
		if booth.IngestToken == "" {
			return fmt.Errorf("%s: booth %q has no ingest token; set %s",
				boothsVariable, id, boothVariable(id, "INGEST"))
		}

		booths = append(booths, booth)
	}

	if err := refuseSharedTokens(booths); err != nil {
		return err
	}

	cfg.Booths = booths

	return nil
}

// boothIDs splits and validates the list. An id is a path segment and a database key: lower case
// so one id has one spelling, and no dots because the mux resolves `.` and `..` away.
func boothIDs(named string) ([]string, error) {
	ids := make([]string, 0, strings.Count(named, ",")+1)
	seen := map[string]bool{}

	for _, raw := range strings.Split(named, ",") {
		id := strings.TrimSpace(raw)
		if id == "" {
			return nil, fmt.Errorf("%s: %q has an empty booth id in it", boothsVariable, named)
		}

		if !validBoothID(id) {
			return nil, fmt.Errorf(
				"%s: %q is not a booth id; use lower-case letters, digits and dashes, "+
					"because an id is a path segment and a database key", boothsVariable, id)
		}

		if seen[id] {
			return nil, fmt.Errorf("%s: booth %q is named twice, so one id has two answers",
				boothsVariable, id)
		}
		seen[id] = true

		ids = append(ids, id)
	}

	return ids, nil
}

func validBoothID(id string) bool {
	// Bounded because the id goes in a URL and a database column.
	const maxBoothID = 40

	if len(id) > maxBoothID {
		return false
	}

	for _, r := range id {
		switch {
		case r >= 'a' && r <= 'z', r >= '0' && r <= '9', r == '-':
		default:
			return false
		}
	}

	return true
}

// refuseSharedTokens fails startup if two booths share a token, since a booth is resolved from the
// presented token and a shared one would be ambiguous.
func refuseSharedTokens(booths []Booth) error {
	holders := map[string]string{}

	for _, booth := range booths {
		for _, token := range []string{booth.IngestToken, booth.ViewToken, booth.OperatorToken} {
			if token == "" {
				continue
			}

			if held, taken := holders[token]; taken {
				return fmt.Errorf(
					"%s: booths %q and %q share a token, and a booth is whichever booth the "+
						"presented token belongs to", boothsVariable, held, booth.ID)
			}

			holders[token] = booth.ID
		}
	}

	return nil
}

// boothVariable is the environment variable holding one booth's token, upper-cased with dashes
// turned into underscores.
func boothVariable(id, which string) string {
	return "LMS_BOOTH_" + strings.ToUpper(strings.ReplaceAll(id, "-", "_")) + "_" + which + "_TOKEN"
}

// publicBoothsVariable names the booths whose brackets are public. Empty means none.
const publicBoothsVariable = "LMS_PUBLIC_BRACKETS"

// publicBooths parses the publication list, validated like LMS_BOOTHS. Ids that name no served
// booth are allowed.
func publicBooths(getenv func(string) string) (map[string]bool, error) {
	published := map[string]bool{}

	named := lookup(getenv, publicBoothsVariable)
	if named == "" {
		return published, nil
	}

	ids, err := boothIDs(named)
	if err != nil {
		return nil, fmt.Errorf("%s: %w", publicBoothsVariable, err)
	}

	for _, id := range ids {
		published[id] = true
	}

	return published, nil
}
