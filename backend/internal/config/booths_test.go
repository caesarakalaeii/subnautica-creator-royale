package config_test

import (
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

func TestTheThreeOldTokensStillDescribeTheOneReservedBooth(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_INGEST_TOKEN":   "push",
		"LMS_VIEW_TOKEN":     "view",
		"LMS_OPERATOR_TOKEN": "write",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if len(cfg.Booths) != 1 {
		t.Fatalf("Booths = %d, want the one reserved booth", len(cfg.Booths))
	}

	booth := cfg.Booths[0]
	if booth.ID != config.TournamentBooth {
		t.Errorf("ID = %q, want %q", booth.ID, config.TournamentBooth)
	}
	if booth.IngestToken != "push" || booth.ViewToken != "view" || booth.OperatorToken != "write" {
		t.Errorf("booth = %+v, want the three tokens from the environment", booth)
	}
}

// The container has to be probeable with no environment, so paths answer "not configured" rather
// than "no such booth".
func TestAnEmptyEnvironmentStillHasTheReservedBooth(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if len(cfg.Booths) != 1 || cfg.Booths[0].ID != config.TournamentBooth {
		t.Fatalf("Booths = %+v, want just the reserved booth", cfg.Booths)
	}
}

func TestEachNamedBoothTakesItsOwnThreeTokens(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_BOOTHS":                     "ada, grace",
		"LMS_BOOTH_ADA_INGEST_TOKEN":     "ada-push",
		"LMS_BOOTH_ADA_VIEW_TOKEN":       "ada-view",
		"LMS_BOOTH_GRACE_INGEST_TOKEN":   "grace-push",
		"LMS_BOOTH_GRACE_VIEW_TOKEN":     "grace-view",
		"LMS_BOOTH_GRACE_OPERATOR_TOKEN": "grace-write",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if len(cfg.Booths) != 2 {
		t.Fatalf("Booths = %+v, want two", cfg.Booths)
	}

	if cfg.Booths[0].ID != "ada" || cfg.Booths[0].IngestToken != "ada-push" ||
		cfg.Booths[0].ViewToken != "ada-view" || cfg.Booths[0].OperatorToken != "" {
		t.Errorf("first booth = %+v, want ada with two of its three tokens", cfg.Booths[0])
	}

	if cfg.Booths[1].ID != "grace" || cfg.Booths[1].IngestToken != "grace-push" ||
		cfg.Booths[1].ViewToken != "grace-view" || cfg.Booths[1].OperatorToken != "grace-write" {
		t.Errorf("second booth = %+v, want grace with all three", cfg.Booths[1])
	}
}

func TestNamingBoothsLeavesNoReservedBoothBehind(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_BOOTHS":                 "ada",
		"LMS_BOOTH_ADA_INGEST_TOKEN": "ada-push",
		"LMS_INGEST_TOKEN":           "push",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if len(cfg.Booths) != 1 || cfg.Booths[0].ID != "ada" {
		t.Fatalf("Booths = %+v, want only ada", cfg.Booths)
	}
}

func TestABoothIdThatCouldNotBeAPathSegmentIsAStartupError(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name string
		ids  string
	}{
		{"a slash would be two segments", "ada/grace"},
		{"an upper-case id would be two spellings of one booth", "Ada"},
		{"a space is not a path segment", "ada booth"},
		{"a dot segment is not a booth", ".."},
		{"the same booth twice is two answers to one id", "ada,ada"},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			_, err := config.Load(env(map[string]string{"LMS_BOOTHS": tc.ids}))
			if err == nil {
				t.Fatalf("LMS_BOOTHS=%q should be refused", tc.ids)
			}
			if !strings.Contains(err.Error(), "LMS_BOOTHS") {
				t.Errorf("error %q does not name LMS_BOOTHS", err)
			}
		})
	}
}

func TestABoothWithNoIngestTokenIsAStartupErrorNamingIt(t *testing.T) {
	t.Parallel()

	_, err := config.Load(env(map[string]string{"LMS_BOOTHS": "ada"}))
	if err == nil {
		t.Fatal("a booth with no ingest token should be refused")
	}
	if !strings.Contains(err.Error(), "LMS_BOOTH_ADA_INGEST_TOKEN") {
		t.Errorf("error = %q, want it to name the variable that is missing", err)
	}
}

func TestTwoBoothsCannotShareAToken(t *testing.T) {
	t.Parallel()

	_, err := config.Load(env(map[string]string{
		"LMS_BOOTHS":                   "ada,grace",
		"LMS_BOOTH_ADA_INGEST_TOKEN":   "shared",
		"LMS_BOOTH_GRACE_INGEST_TOKEN": "shared",
	}))
	if err == nil {
		t.Fatal("two booths sharing an ingest token should be refused")
	}
	if !strings.Contains(err.Error(), "ada") || !strings.Contains(err.Error(), "grace") {
		t.Errorf("error = %q, want it to name both booths", err)
	}
}

// Publication is an id list, not a token: nothing secret is involved.
func TestLMS_PUBLIC_BRACKETSPublishesEachBoothItNames(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_BOOTHS":                   "ada,grace",
		"LMS_BOOTH_ADA_INGEST_TOKEN":   "ada-push",
		"LMS_BOOTH_GRACE_INGEST_TOKEN": "grace-push",
		"LMS_PUBLIC_BRACKETS":          " ada ,grace",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	for _, booth := range cfg.Booths {
		if booth.ID != "ada" && booth.ID != "grace" {
			t.Fatalf("Booths = %+v, want ada and grace", cfg.Booths)
		}

		if !booth.Published {
			t.Errorf("booth %q = Published false, want it published", booth.ID)
		}
	}
}

func TestAnUnsetPublicBracketsListPublishesNothing(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_BOOTHS":                   "ada,grace",
		"LMS_BOOTH_ADA_INGEST_TOKEN":   "ada-push",
		"LMS_BOOTH_GRACE_INGEST_TOKEN": "grace-push",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	for _, booth := range cfg.Booths {
		if booth.Published {
			t.Errorf("booth %q = Published true with no list set", booth.ID)
		}
	}
}

func TestAPublicBracketsListThatNamesNoBoothIdIsAStartupError(t *testing.T) {
	t.Parallel()

	_, err := config.Load(env(map[string]string{"LMS_PUBLIC_BRACKETS": "Tournament"}))
	if err == nil {
		t.Fatal("a list holding a non-id should be refused")
	}
	if !strings.Contains(err.Error(), "LMS_PUBLIC_BRACKETS") {
		t.Errorf("error %q does not name the variable", err)
	}
}

func TestAPublicBracketsListMayNameABoothTheConfigurationDoesNotServe(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_INGEST_TOKEN":    "push",
		"LMS_PUBLIC_BRACKETS": "tournament, ada",
	}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if len(cfg.Booths) != 1 || cfg.Booths[0].ID != config.TournamentBooth {
		t.Fatalf("Booths = %+v, want only the reserved booth", cfg.Booths)
	}

	if !cfg.Booths[0].Published {
		t.Error("the reserved booth is not published, want it published")
	}
}
