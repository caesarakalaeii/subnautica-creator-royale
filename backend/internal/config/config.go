// Package config turns the process environment into a validated configuration. Nothing here
// reads the environment itself: Load takes a lookup function, so configuration is testable
// without mutating global state.
package config

import (
	"fmt"
	"log/slog"
	"net"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// The log formats the service understands.
const (
	LogFormatJSON = "json"
	LogFormatText = "text"
)

// Defaults. The port default lets the container be probed with no environment at all.
const (
	defaultPort            = 8080
	defaultLogFormat       = LogFormatJSON
	defaultShutdownTimeout = 15 * time.Second

	// The mod pushes about once a second, so twenty a second leaves ample headroom while still
	// bounding a misbehaving client.
	defaultIngestRate = 20.0

	// Comfortably above a dashboard and a few overlays per machine plus casters' tabs.
	defaultMaxLiveClients = 64

	// How many booths one process holds at once. A full booth is about 0.9 MiB of heap; rerun
	// `go test ./internal/caster -run TestABoothsFootprintIsAboutWhatTheCeilingAssumes -v` before
	// changing it. Defaulted because the booth-creation endpoint takes no token.
	defaultMaxBooths = 64

	// How often one source address may create a booth: one every twenty seconds, with a burst of
	// one. Defaulted because the endpoint takes no token.
	defaultBoothRate = 0.05

	// The standard Postgres port.
	defaultDatabasePort = 5432

	// Unencrypted by default, for a database on a private network; set `DB_SSLMODE` otherwise.
	defaultDatabaseSSLMode = "disable"

	// How many players the final seats.
	defaultFinalSeats = 5

	// How long a queued command waits to be collected. Longer than any ordinary gap in the push (the
	// mod backs off to thirty seconds), short enough that a stale command does not fire much later.
	defaultCommandTTL = 2 * time.Minute
)

// Config is the whole configuration surface of the service.
type Config struct {
	Port            int
	LogLevel        slog.Level
	LogFormat       string
	ShutdownTimeout time.Duration

	// IngestRate bounds telemetry pushes per second, with a two-second burst. Zero is off.
	IngestRate float64

	// MaxLiveClients bounds how many dashboards and overlays may be connected at once. Zero is off.
	// A refused client is told why over the socket.
	MaxLiveClients int

	// BoothRate bounds how often one source address may create a booth. Zero is off. Per source, so
	// one client cannot block booth creation for everybody.
	BoothRate float64

	// ClientIPHeader is the header a trusted proxy writes the caller's address into, or empty to use
	// the connection's address. Empty by default, since a client could otherwise choose its own bucket.
	ClientIPHeader string

	// MaxBooths bounds how many booths, created and configured, this process holds at once. Zero is
	// off.
	MaxBooths int

	// DatabaseURL is the Postgres DSN, password included; never logged. Empty means no database: the
	// live path still works and writes are refused with a reason.
	DatabaseURL string

	// DatabaseDescribed is the same connection with the secret taken out.
	DatabaseDescribed string

	// FinalSeats is how many players the final seats from the top of the standings.
	FinalSeats int

	// CommandTTL is how long a queued console command waits before it expires. Zero is off, which
	// risks a stale command firing when a game server next connects.
	CommandTTL time.Duration

	// Booths is every booth this process serves, in the order named. Never empty: with LMS_BOOTHS
	// unset it holds the reserved booth built from LMS_INGEST_TOKEN and its two siblings.
	Booths []Booth
}

// IngestRateLimited is whether pushes are rate limited at all.
func (c Config) IngestRateLimited() bool {
	return c.IngestRate > 0
}

// LiveClientsLimited is whether the number of live clients is bounded at all.
func (c Config) LiveClientsLimited() bool {
	return c.MaxLiveClients > 0
}

// BoothsLimited is whether the number of booths this process holds is bounded at all.
func (c Config) BoothsLimited() bool {
	return c.MaxBooths > 0
}

// BoothRateLimited is whether booth creation is rate limited at all.
func (c Config) BoothRateLimited() bool {
	return c.BoothRate > 0
}

// CommandsExpire is whether a queued command is ever given up on.
func (c Config) CommandsExpire() bool {
	return c.CommandTTL > 0
}

// DatabaseEnabled is whether there is a store of record to write to.
func (c Config) DatabaseEnabled() bool {
	return c.DatabaseURL != ""
}

// DatabaseDescription is which database, for a log line. Never the password.
func (c Config) DatabaseDescription() string {
	return c.DatabaseDescribed
}

// Addr is the listen address, in the form http.Server wants.
func (c Config) Addr() string {
	return ":" + strconv.Itoa(c.Port)
}

// Load reads configuration through getenv, usually os.Getenv. An unset or empty variable takes
// its default; a set but unusable one is an error naming the variable, so a misconfiguration fails
// at startup.
func Load(getenv func(string) string) (Config, error) {
	cfg := Config{
		Port:            defaultPort,
		LogLevel:        slog.LevelInfo,
		LogFormat:       defaultLogFormat,
		ShutdownTimeout: defaultShutdownTimeout,
		IngestRate:      defaultIngestRate,
		MaxLiveClients:  defaultMaxLiveClients,
		MaxBooths:       defaultMaxBooths,
		BoothRate:       defaultBoothRate,
		FinalSeats:      defaultFinalSeats,
		CommandTTL:      defaultCommandTTL,
	}

	if raw := lookup(getenv, "LMS_PORT"); raw != "" {
		port, err := strconv.Atoi(raw)
		if err != nil || port < 1 || port > 65535 {
			return Config{}, fmt.Errorf("LMS_PORT: %q is not a TCP port between 1 and 65535", raw)
		}
		cfg.Port = port
	}

	if raw := lookup(getenv, "LMS_LOG_LEVEL"); raw != "" {
		var level slog.Level
		if err := level.UnmarshalText([]byte(raw)); err != nil {
			return Config{}, fmt.Errorf("LMS_LOG_LEVEL: %q is not one of debug, info, warn, error", raw)
		}
		cfg.LogLevel = level
	}

	if raw := lookup(getenv, "LMS_LOG_FORMAT"); raw != "" {
		switch strings.ToLower(raw) {
		case LogFormatJSON:
			cfg.LogFormat = LogFormatJSON
		case LogFormatText:
			cfg.LogFormat = LogFormatText
		default:
			return Config{}, fmt.Errorf("LMS_LOG_FORMAT: %q is not one of %s, %s", raw, LogFormatJSON, LogFormatText)
		}
	}

	// Both bounds take zero to mean "off", so a limit can be lifted with only an environment change.
	if raw := lookup(getenv, "LMS_INGEST_RATE"); raw != "" {
		rate, err := strconv.ParseFloat(raw, 64)
		if err != nil || rate < 0 {
			return Config{}, fmt.Errorf("LMS_INGEST_RATE: %q is not a number of pushes per second, or 0 for no limit", raw)
		}
		cfg.IngestRate = rate
	}

	if raw := lookup(getenv, "LMS_MAX_LIVE_CLIENTS"); raw != "" {
		clients, err := strconv.Atoi(raw)
		if err != nil || clients < 0 {
			return Config{}, fmt.Errorf("LMS_MAX_LIVE_CLIENTS: %q is not a count of clients, or 0 for no limit", raw)
		}
		cfg.MaxLiveClients = clients
	}

	if raw := lookup(getenv, "LMS_MAX_BOOTHS"); raw != "" {
		booths, err := strconv.Atoi(raw)
		if err != nil || booths < 0 {
			return Config{}, fmt.Errorf("LMS_MAX_BOOTHS: %q is not a count of booths, or 0 for no limit", raw)
		}
		cfg.MaxBooths = booths
	}

	if raw := lookup(getenv, "LMS_BOOTH_RATE"); raw != "" {
		rate, err := strconv.ParseFloat(raw, 64)
		if err != nil || rate < 0 {
			return Config{}, fmt.Errorf(
				"LMS_BOOTH_RATE: %q is not a number of booth creations per second per source, "+
					"or 0 for no limit", raw)
		}
		cfg.BoothRate = rate
	}

	cfg.ClientIPHeader = lookup(getenv, "LMS_CLIENT_IP_HEADER")

	if raw := lookup(getenv, "LMS_FINAL_SEATS"); raw != "" {
		seats, err := strconv.Atoi(raw)
		if err != nil || seats < 0 {
			return Config{}, fmt.Errorf("LMS_FINAL_SEATS: %q is not a count of players in the final", raw)
		}
		cfg.FinalSeats = seats
	}

	if raw := lookup(getenv, "LMS_COMMAND_TTL"); raw != "" {
		ttl, err := time.ParseDuration(raw)
		if err != nil {
			return Config{}, fmt.Errorf("LMS_COMMAND_TTL: %q is not a duration such as 2m", raw)
		}
		if ttl < 0 {
			return Config{}, fmt.Errorf("LMS_COMMAND_TTL: %q cannot be negative; 0 switches expiry off", raw)
		}
		cfg.CommandTTL = ttl
	}

	if err := loadDatabase(&cfg, getenv); err != nil {
		return Config{}, err
	}

	if err := loadBooths(&cfg, getenv); err != nil {
		return Config{}, err
	}

	if raw := lookup(getenv, "LMS_SHUTDOWN_TIMEOUT"); raw != "" {
		timeout, err := time.ParseDuration(raw)
		if err != nil {
			return Config{}, fmt.Errorf("LMS_SHUTDOWN_TIMEOUT: %q is not a duration such as 15s", raw)
		}
		if timeout <= 0 {
			return Config{}, fmt.Errorf("LMS_SHUTDOWN_TIMEOUT: %q must be positive, or in-flight requests are cut rather than drained", raw)
		}
		cfg.ShutdownTimeout = timeout
	}

	return cfg, nil
}

func lookup(getenv func(string) string, key string) string {
	return strings.TrimSpace(getenv(key))
}

// loadDatabase resolves the database DSN. LMS_DATABASE_URL wins outright; otherwise it is built
// from the conventional `DB_*` variables.
func loadDatabase(cfg *Config, getenv func(string) string) error {
	if whole := lookup(getenv, "LMS_DATABASE_URL"); whole != "" {
		cfg.DatabaseURL = whole
		cfg.DatabaseDescribed = "LMS_DATABASE_URL"

		return nil
	}

	host := lookup(getenv, "DB_HOST")
	if host == "" {
		// No database. The container has to be probeable with no environment at all.
		return nil
	}

	// Each part is required once DB_HOST is set: a half-configured database would silently lose
	// every write while the live path looks healthy.
	name := lookup(getenv, "DB_NAME")
	if name == "" {
		return fmt.Errorf("DB_NAME: DB_HOST is set to %q, so the database needs a name", host)
	}

	user := lookup(getenv, "DB_USER")
	if user == "" {
		return fmt.Errorf("DB_USER: DB_HOST is set to %q, so the database needs a role", host)
	}

	port := defaultDatabasePort
	if raw := lookup(getenv, "DB_PORT"); raw != "" {
		parsed, err := strconv.Atoi(raw)
		if err != nil || parsed < 1 || parsed > 65535 {
			return fmt.Errorf("DB_PORT: %q is not a TCP port between 1 and 65535", raw)
		}
		port = parsed
	}

	sslMode := defaultDatabaseSSLMode
	if raw := lookup(getenv, "DB_SSLMODE"); raw != "" {
		sslMode = raw
	}

	// Not trimmed, like the tokens: a secret is taken verbatim.
	password := getenv("DB_PASSWORD")

	// url.UserPassword escapes both halves, since a generated password may contain `@` or `/`.
	dsn := url.URL{
		Scheme:   "postgres",
		User:     url.UserPassword(user, password),
		Host:     net.JoinHostPort(host, strconv.Itoa(port)),
		Path:     "/" + name,
		RawQuery: url.Values{"sslmode": {sslMode}}.Encode(),
	}

	cfg.DatabaseURL = dsn.String()
	cfg.DatabaseDescribed = fmt.Sprintf("%s@%s/%s", user, net.JoinHostPort(host, strconv.Itoa(port)), name)

	return nil
}
