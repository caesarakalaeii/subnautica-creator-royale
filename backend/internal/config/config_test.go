package config_test

import (
	"log/slog"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// env builds a getenv function over a map, so tests never touch the process environment.
func env(pairs map[string]string) func(string) string {
	return func(key string) string { return pairs[key] }
}

func TestAnEmptyEnvironmentGivesTheDocumentedDefaults(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("empty environment should be valid, got %v", err)
	}

	if cfg.Port != 8080 {
		t.Errorf("Port = %d, want 8080 (the port the ClusterIP service targets)", cfg.Port)
	}
	if cfg.Addr() != ":8080" {
		t.Errorf("Addr() = %q, want %q", cfg.Addr(), ":8080")
	}
	if cfg.LogLevel != slog.LevelInfo {
		t.Errorf("LogLevel = %v, want info", cfg.LogLevel)
	}
	if cfg.LogFormat != config.LogFormatJSON {
		t.Errorf("LogFormat = %q, want json", cfg.LogFormat)
	}
	if cfg.ShutdownTimeout != 15*time.Second {
		t.Errorf("ShutdownTimeout = %v, want 15s", cfg.ShutdownTimeout)
	}
}

func TestEveryValueCanBeSetFromTheEnvironment(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_PORT":             "9000",
		"LMS_LOG_LEVEL":        "debug",
		"LMS_LOG_FORMAT":       "text",
		"LMS_SHUTDOWN_TIMEOUT": "45s",
	}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if cfg.Port != 9000 || cfg.Addr() != ":9000" {
		t.Errorf("Port = %d, Addr = %q, want 9000", cfg.Port, cfg.Addr())
	}
	if cfg.LogLevel != slog.LevelDebug {
		t.Errorf("LogLevel = %v, want debug", cfg.LogLevel)
	}
	if cfg.LogFormat != config.LogFormatText {
		t.Errorf("LogFormat = %q, want text", cfg.LogFormat)
	}
	if cfg.ShutdownTimeout != 45*time.Second {
		t.Errorf("ShutdownTimeout = %v, want 45s", cfg.ShutdownTimeout)
	}
}

func TestABadValueIsAStartupErrorNamingTheVariable(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name     string
		environ  map[string]string
		wantName string
	}{
		{"port is not a number", map[string]string{"LMS_PORT": "http"}, "LMS_PORT"},
		{"port is zero", map[string]string{"LMS_PORT": "0"}, "LMS_PORT"},
		{"port is above the range", map[string]string{"LMS_PORT": "70000"}, "LMS_PORT"},
		{"port is negative", map[string]string{"LMS_PORT": "-1"}, "LMS_PORT"},
		{"log level is unknown", map[string]string{"LMS_LOG_LEVEL": "chatty"}, "LMS_LOG_LEVEL"},
		{"log format is unknown", map[string]string{"LMS_LOG_FORMAT": "yaml"}, "LMS_LOG_FORMAT"},
		{"shutdown timeout is not a duration", map[string]string{"LMS_SHUTDOWN_TIMEOUT": "soon"}, "LMS_SHUTDOWN_TIMEOUT"},
		{"shutdown timeout is negative", map[string]string{"LMS_SHUTDOWN_TIMEOUT": "-5s"}, "LMS_SHUTDOWN_TIMEOUT"},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			_, err := config.Load(env(tc.environ))
			if err == nil {
				t.Fatalf("want an error for %v, got none", tc.environ)
			}
			if !strings.Contains(err.Error(), tc.wantName) {
				t.Errorf("error %q does not name %s", err, tc.wantName)
			}
		})
	}
}

func TestAnEmptyVariableFallsBackToItsDefaultRatherThanFailing(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{"LMS_PORT": "", "LMS_LOG_LEVEL": ""}))
	if err != nil {
		t.Fatalf("an unset variable is not an invalid one: %v", err)
	}
	if cfg.Port != 8080 || cfg.LogLevel != slog.LevelInfo {
		t.Errorf("Port = %d, LogLevel = %v, want the defaults", cfg.Port, cfg.LogLevel)
	}
}

func TestTheIngestTokenIsOptionalAndOffWhenAbsent(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	booth := cfg.Booths[0]
	if booth.IngestToken != "" {
		t.Errorf("token = %q, want empty so ingest is off rather than open", booth.IngestToken)
	}
}

func TestTheIngestTokenIsTakenVerbatim(t *testing.T) {
	t.Parallel()

	// Not trimmed: a secret is taken verbatim.
	const secret = "  spaces are part of it  "

	cfg, err := config.Load(env(map[string]string{"LMS_INGEST_TOKEN": secret}))
	if err != nil {
		t.Fatalf("load: %v", err)
	}

	if got := cfg.Booths[0].IngestToken; got != secret {
		t.Errorf("token = %q, want it verbatim", got)
	}
}

func TestTheLimitsHaveDefaultsAndCanBeSet(t *testing.T) {
	t.Parallel()

	defaults, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("empty environment should be valid, got %v", err)
	}

	if defaults.IngestRate != 20 {
		t.Errorf("IngestRate = %v, want 20/s (the mod pushes about one)", defaults.IngestRate)
	}
	if defaults.MaxLiveClients != 64 {
		t.Errorf("MaxLiveClients = %d, want 64", defaults.MaxLiveClients)
	}

	set, err := config.Load(env(map[string]string{
		"LMS_INGEST_RATE":      "2.5",
		"LMS_MAX_LIVE_CLIENTS": "8",
		"LMS_MAX_BOOTHS":       "40",
		"LMS_BOOTH_RATE":       "0.05",
		"LMS_CLIENT_IP_HEADER": "X-Forwarded-For",
	}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if set.IngestRate != 2.5 {
		t.Errorf("IngestRate = %v, want 2.5 — a fractional rate is a legitimate thing to want", set.IngestRate)
	}
	if set.MaxLiveClients != 8 {
		t.Errorf("MaxLiveClients = %d, want 8", set.MaxLiveClients)
	}
	if set.MaxBooths != 40 {
		t.Errorf("MaxBooths = %d, want 40", set.MaxBooths)
	}
	if set.BoothRate != 0.05 {
		t.Errorf("BoothRate = %v, want 0.05", set.BoothRate)
	}
	if set.ClientIPHeader != "X-Forwarded-For" {
		t.Errorf("ClientIPHeader = %q, want X-Forwarded-For", set.ClientIPHeader)
	}
}

func TestNoForwardedHeaderIsTrustedUnlessOneIsNamed(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("empty environment should be valid, got %v", err)
	}

	if cfg.ClientIPHeader != "" {
		t.Errorf("ClientIPHeader = %q with nothing configured, want no header trusted", cfg.ClientIPHeader)
	}
}

// Unlike the other bounds this defaults on, because `POST /booths` takes no token.
func TestTheBoothCeilingHasADefaultRatherThanBeingOffUnlessAsked(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("empty environment should be valid, got %v", err)
	}

	if !cfg.BoothsLimited() {
		t.Fatalf("MaxBooths = %d with nothing configured, want a ceiling", cfg.MaxBooths)
	}
}

func TestZeroSwitchesALimitOff(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_INGEST_RATE":      "0",
		"LMS_MAX_LIVE_CLIENTS": "0",
		"LMS_MAX_BOOTHS":       "0",
		"LMS_BOOTH_RATE":       "0",
	}))
	if err != nil {
		t.Fatalf("zero should be valid and mean unlimited, got %v", err)
	}

	if cfg.IngestRate != 0 || cfg.MaxLiveClients != 0 || cfg.MaxBooths != 0 {
		t.Errorf("IngestRate = %v, MaxLiveClients = %d, MaxBooths = %d, want all 0",
			cfg.IngestRate, cfg.MaxLiveClients, cfg.MaxBooths)
	}
	if cfg.IngestRateLimited() {
		t.Error("IngestRateLimited() is true at a rate of 0")
	}
	if cfg.BoothsLimited() {
		t.Error("BoothsLimited() is true at a maximum of 0")
	}
	if cfg.BoothRateLimited() {
		t.Error("BoothRateLimited() is true at a rate of 0")
	}
}

func TestANegativeLimitIsRefusedByName(t *testing.T) {
	t.Parallel()

	for variable, value := range map[string]string{
		"LMS_INGEST_RATE":      "-1",
		"LMS_MAX_LIVE_CLIENTS": "-4",
		"LMS_MAX_BOOTHS":       "-1",
		"LMS_BOOTH_RATE":       "-0.5",
	} {
		if _, err := config.Load(env(map[string]string{variable: value})); err == nil {
			t.Errorf("%s=%s was accepted, want an error", variable, value)
		} else if !strings.Contains(err.Error(), variable) {
			t.Errorf("error for %s does not name the variable: %v", variable, err)
		}
	}

	for variable, value := range map[string]string{
		"LMS_INGEST_RATE":      "often",
		"LMS_MAX_LIVE_CLIENTS": "a few",
		"LMS_MAX_BOOTHS":       "lots",
		"LMS_BOOTH_RATE":       "rarely",
	} {
		if _, err := config.Load(env(map[string]string{variable: value})); err == nil {
			t.Errorf("%s=%s was accepted, want an error", variable, value)
		} else if !strings.Contains(err.Error(), variable) {
			t.Errorf("error for %s does not name the variable: %v", variable, err)
		}
	}
}

// The view token is shared with casters and ends up in overlay URLs, so writes need a separate
// token.
func TestWritingNeedsItsOwnTokenAndIsOffWithoutOne(t *testing.T) {
	t.Parallel()

	off, err := config.Load(env(map[string]string{"LMS_VIEW_TOKEN": "watch"}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if off.Booths[0].OperatorToken != "" {
		t.Error("the view token switched writing on, which is the hole the third token closes")
	}

	on, err := config.Load(env(map[string]string{"LMS_OPERATOR_TOKEN": "write"}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if got := on.Booths[0].OperatorToken; got != "write" {
		t.Errorf("OperatorToken = %q, want the token it was given", got)
	}
}

func TestTheDatabaseIsAssembledFromTheClustersOwnVariables(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"DB_HOST":     "db.example.internal",
		"DB_NAME":     "lms",
		"DB_USER":     "lms_user",
		"DB_PASSWORD": "s3cret",
	}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !cfg.DatabaseEnabled() {
		t.Fatal("DatabaseEnabled() = false, want true")
	}

	for _, want := range []string{
		"postgres://lms_user:s3cret@db.example.internal:5432/lms",
		"sslmode=disable",
	} {
		if !strings.Contains(cfg.DatabaseURL, want) {
			t.Errorf("DatabaseURL = %q, want it to contain %q", cfg.DatabaseURL, want)
		}
	}
}

func TestTheDatabaseDescribesItselfWithoutThePassword(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"DB_HOST":     "db.internal",
		"DB_NAME":     "lms",
		"DB_USER":     "lms_user",
		"DB_PASSWORD": "s3cret",
	}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	described := cfg.DatabaseDescription()
	if strings.Contains(described, "s3cret") {
		t.Errorf("DatabaseDescription() = %q, which carries the password", described)
	}

	if !strings.Contains(described, "db.internal") || !strings.Contains(described, "lms") {
		t.Errorf("DatabaseDescription() = %q, want the host and the database in it", described)
	}
}

func TestAHalfConfiguredDatabaseIsRefusedByName(t *testing.T) {
	t.Parallel()

	_, err := config.Load(env(map[string]string{"DB_HOST": "db.internal", "DB_USER": "lms_user"}))
	if err == nil {
		t.Fatal("a DB_HOST with no DB_NAME should be refused")
	}

	if !strings.Contains(err.Error(), "DB_NAME") {
		t.Errorf("error = %q, want it to name DB_NAME", err)
	}
}

func TestAWholeDatabaseUrlOverridesTheParts(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(map[string]string{
		"LMS_DATABASE_URL": "postgres://someone@localhost:5433/scratch?sslmode=disable",
		"DB_HOST":          "ignored.internal",
		"DB_NAME":          "ignored",
		"DB_USER":          "ignored",
	}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if cfg.DatabaseURL != "postgres://someone@localhost:5433/scratch?sslmode=disable" {
		t.Errorf("DatabaseURL = %q, want the one given whole", cfg.DatabaseURL)
	}
}

func TestNoDatabaseIsAValidConfiguration(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(env(nil))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if cfg.DatabaseEnabled() {
		t.Error("DatabaseEnabled() = true with nothing configured")
	}
}
