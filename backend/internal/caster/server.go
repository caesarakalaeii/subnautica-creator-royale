// Package caster is the casting-booth service: health, an authenticated telemetry push, a
// WebSocket fan-out of it, and the front-end that watches it.
package caster

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"strings"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/web"
)

// healthPath is what the readiness and liveness probes hit.
const healthPath = "/healthz"

// Server owns the routes, the configuration they were built from, and the match state pushed
// into them.
type Server struct {
	cfg     config.Config
	logger  *slog.Logger
	handler http.Handler

	// booths is every tenant this process serves, each with its own store, fan-out and console.
	booths *booths

	// records narrows the store of record to one booth, or is nil when no database is configured.
	// Nothing on the live path touches it, so a database outage never affects the feed.
	records RecordsFor

	// registry is where booths created on request are recorded, or nil when there is no database
	// and booths cannot be created.
	registry Registry

	// now is this service's clock, injectable so tests can advance it past expiry.
	now func() time.Time

	// pushLimit bounds authenticated telemetry pushes, and badTokenLimit bounds logging about
	// unauthenticated ones. Separate so an unauthenticated flood cannot spend the mod's allowance.
	// Each is nil when limiting is off.
	pushLimit     *limiter
	badTokenLimit *limiter

	// writeLimit bounds operator writes, and badOperatorLimit bounds logging about bad operator
	// tokens, separately from ingest so an /ingest flood cannot hide operator-token guessing.
	// Each is nil when limiting is off.
	writeLimit       *limiter
	badOperatorLimit *limiter

	// boothLimit bounds how often one source address may create a booth; `POST /booths` takes no
	// token, so it is limited per source. Nil when limiting is off.
	boothLimit *perSource

	// heartbeat is how often an idle live connection is pinged; a field so tests can shorten it.
	heartbeat time.Duration
}

// NewServer wires the routes. It does not listen; that is ListenAndServe, so tests can exercise
// the handler over httptest without binding a port.
func NewServer(cfg config.Config, logger *slog.Logger) *Server {
	server := &Server{
		cfg:       cfg,
		logger:    logger,
		booths:    newBooths(cfg),
		heartbeat: heartbeatInterval,
		now:       time.Now,
	}

	// Every bucket reads the server's clock, so tests that advance `now` advance the limits too.
	now := func() time.Time { return server.now() }

	// One switch for all four buckets.
	if cfg.IngestRateLimited() {
		server.pushLimit = newLimiter(cfg.IngestRate, cfg.IngestRate*burstWindow.Seconds(), now)
		server.badTokenLimit = newLimiter(badTokenRate, badTokenRate*burstWindow.Seconds(), now)
		server.writeLimit = newLimiter(writeRate, writeRate*burstWindow.Seconds(), now)
		server.badOperatorLimit = newLimiter(badTokenRate, badTokenRate*burstWindow.Seconds(), now)
	}

	// A separate switch, so disabling the ingest limit does not open the unauthenticated endpoint.
	if cfg.BoothRateLimited() {
		server.boothLimit = newPerSource(cfg.BoothRate, cfg.BoothRate*burstWindow.Seconds(), now)
	}

	mux := http.NewServeMux()
	mux.HandleFunc("GET "+healthPath, health)
	mux.Handle(healthPath, onlyAllows(http.MethodGet, http.MethodHead))

	// Creating a booth belongs to the process, not to a booth: there is none to address yet.
	mux.HandleFunc(http.MethodPost+" "+boothsPath, server.createBooth)
	mux.Handle(boothsPath, onlyAllows(http.MethodPost))

	// Every booth route is registered twice: bare (the reserved tournament booth) and under
	// /b/{booth}/.
	for _, route := range server.boothRoutes() {
		for _, path := range []string{route.path, boothPrefix + "{" + boothParam + "}" + route.path} {
			for method, handler := range route.handlers {
				mux.HandleFunc(method+" "+path, handler)
			}

			// Answer a wrong verb with 405 here; otherwise the front-end fallback would 404 it.
			mux.Handle(path, onlyAllows(route.methods...))
		}
	}

	// The front-end, for every method: a GET-only fallback would conflict with the method-scoped
	// patterns above in Go's mux, so the page handler answers a wrong verb itself.
	pages := web.Handler(sourceOffer())
	mux.Handle("/", pages)

	// The same pages under /b/{booth}/; the page reads its booth from the address (static/booth.js).
	// No booth is looked up here: pages take no token, so a 404 for unknown ids would let anyone
	// enumerate booths. The data endpoints behind the page do the refusing.
	mux.Handle(boothPrefix+"{"+boothParam+"}/", underBooth(pages))

	server.sayWhatEachBoothWillRefuse()

	// Log the configured limits at startup.
	logger.Info("limits",
		"ingest_per_second", cfg.IngestRate,
		"ingest_burst", cfg.IngestRate*burstWindow.Seconds(),
		"max_live_clients", cfg.MaxLiveClients,
		"max_booths", cfg.MaxBooths,
		"command_ttl", cfg.CommandTTL.String())

	// Warn: a command that never expires could fire long after it was meant to.
	if !cfg.CommandsExpire() {
		logger.Warn("LMS_COMMAND_TTL is 0, so a queued command waits for a game server forever",
			"path", commandsPath)
	}

	// Unlimited is allowed, but never silently.
	if !cfg.IngestRateLimited() {
		logger.Warn("LMS_INGEST_RATE is 0, so telemetry pushes are not rate limited", "path", ingestPath)
	}
	if !cfg.LiveClientsLimited() {
		logger.Warn("LMS_MAX_LIVE_CLIENTS is 0, so the number of live clients is unbounded", "path", livePath)
	}

	// Booth creation takes no token, so unbounded means anyone can exhaust memory.
	if !cfg.BoothsLimited() {
		logger.Warn("LMS_MAX_BOOTHS is 0, so anybody may create booths until this process runs out "+
			"of memory", "path", boothsPath)
	}

	if !cfg.BoothRateLimited() {
		logger.Warn("LMS_BOOTH_RATE is 0, so one source may create booths as fast as it can ask",
			"path", boothsPath)
	}

	// Getting this wrong is silent: no header behind a proxy is one shared bucket, and a header
	// without a proxy is caller-controlled.
	if cfg.ClientIPHeader == "" {
		logger.Info("rate limits are keyed on the address the connection came from",
			"hint", "set LMS_CLIENT_IP_HEADER if a proxy sits in front of this service")
	} else {
		logger.Info("rate limits are keyed on a header this deployment's proxy writes",
			"header", cfg.ClientIPHeader)
	}

	server.handler = logRequests(logger, mux)
	return server
}

// boothRoute is one path and the handlers that answer it. The methods are also what a wrong verb
// is told the path takes.
type boothRoute struct {
	path     string
	methods  []string
	handlers map[string]http.HandlerFunc
}

// boothRoutes is every route that belongs to a booth rather than to the process, as a table so
// each is registered both bare and under /b/{booth}/.
func (s *Server) boothRoutes() []boothRoute {
	one := func(path, method string, handler http.HandlerFunc) boothRoute {
		return boothRoute{path: path, methods: []string{method},
			handlers: map[string]http.HandlerFunc{method: handler}}
	}

	return []boothRoute{
		one(ingestPath, http.MethodPost, s.ingest),
		one(livePath, http.MethodGet, s.live),

		// The camera channel has its own socket, since SubscriberBuffer counts /live messages.
		one(cameraPath, http.MethodGet, s.camera),

		// The booth's write path.
		one(attributionsPath, http.MethodPost, s.attribute),
		one(adjustmentsPath, http.MethodPost, s.adjust),
		one(groupsPath, http.MethodPut, s.setGroups),
		one(commandsPath, http.MethodPost, s.queueCommand),

		// One path, two tokens: the mod writes with the ingest token, a browser reads with the
		// operator token. Never public: it names who is connected.
		{path: consolePath, methods: []string{http.MethodGet, http.MethodPost},
			handlers: map[string]http.HandlerFunc{
				http.MethodGet:  s.showConsole,
				http.MethodPost: s.takeConsole,
			}},

		// The roster, and the exceptions to the rule it derives.
		one(peoplePath, http.MethodPut, s.setPeople),
		one(overridesPath, http.MethodPost, s.overrule),

		// The tournament's rules: the booth stores them; the game server validates and adopts them.
		one(rulesPath, http.MethodPut, s.setRules),

		// One revision back whole, so a bad edit can be reverted.
		one(rulesPath+"/{revision}", http.MethodGet, s.showRules),

		// The bracket, behind the view token. Longhand because it also answers HEAD.
		{path: bracketPath, methods: []string{http.MethodGet, http.MethodHead},
			handlers: map[string]http.HandlerFunc{http.MethodGet: s.bracket}},
	}
}

// sayWhatEachBoothWillRefuse logs every token that is not set, once, at startup, so a booth that
// will refuse everything is noticed before a match.
func (s *Server) sayWhatEachBoothWillRefuse() {
	for _, booth := range s.booths.all() {
		if !booth.ingestConfigured() {
			s.logger.Warn("no ingest token is set, so telemetry pushes to this booth will be refused",
				"booth", booth.ID(), "path", ingestPath)
		}

		// Unset means closed, not open: the feed carries every player's live position.
		if !booth.viewConfigured() {
			s.logger.Warn("no view token is set, so this booth's live feed will refuse every client",
				"booth", booth.ID(), "path", livePath)
		}

		// Unset means read-only, not open.
		if !booth.writeConfigured() {
			s.logger.Warn("no operator token is set, so this booth is read-only",
				"booth", booth.ID(),
				"paths", attributionsPath+", "+groupsPath+", "+commandsPath)
		}

		// Logged so a mistyped id in the published list is easy to spot.
		if booth.isPublic() {
			s.logger.Info("this booth's bracket is published and answers without a token",
				"booth", booth.ID(), "path", bracketPath)
		}
	}
}

// Booth is the booth an id names, or nil when this process serves none by that name.
func (s *Server) Booth(id string) *Booth {
	return s.booths.find(id)
}

// Store is the reserved tournament booth's match state; use Booth(id).Store() for other booths.
// Nil when no reserved booth is configured (e.g. `LMS_BOOTHS=ada,grace`).
func (s *Server) Store() *Store {
	return s.booths.find(config.TournamentBooth).Store()
}

// Hub is the reserved tournament booth's fan-out, exposed for reading its drop counts. Nil when no
// reserved booth is configured.
func (s *Server) Hub() *Hub {
	return s.booths.find(config.TournamentBooth).Hub()
}

// WithRecords gives the server its store of record, narrowed per booth. Call it before serving;
// nil means no database: the match is served and writes are refused.
func (s *Server) WithRecords(records RecordsFor) *Server {
	s.records = records

	return s
}

// WithBooths gives the server the table it creates booths in. Call it before serving; nil means
// booth creation is refused.
func (s *Server) WithBooths(registry Registry) *Server {
	s.registry = registry

	return s
}

// Handler is the routed, logged handler.
func (s *Server) Handler() http.Handler {
	return s.handler
}

// ListenAndServe binds the configured port and serves until ctx is cancelled.
func (s *Server) ListenAndServe(ctx context.Context) error {
	listener, err := net.Listen("tcp", s.cfg.Addr())
	if err != nil {
		return fmt.Errorf("listen on %s: %w", s.cfg.Addr(), err)
	}

	s.logger.Info("listening", "addr", listener.Addr().String())
	return Serve(ctx, listener, s.handler, s.cfg.ShutdownTimeout, s.logger)
}

// Serve runs h on ln until ctx is cancelled, then gives in-flight requests up to timeout to
// finish, so a rolling deploy does not drop requests mid-match.
func Serve(ctx context.Context, ln net.Listener, h http.Handler, timeout time.Duration, logger *slog.Logger) error {
	server := &http.Server{
		Handler: h,
		// Bounds a client that never finishes its headers. No blanket WriteTimeout: it would cut
		// WebSockets; streaming endpoints set their own.
		ReadHeaderTimeout: 10 * time.Second,
	}

	serveErr := make(chan error, 1)
	go func() { serveErr <- server.Serve(ln) }()

	select {
	case err := <-serveErr:
		if errors.Is(err, http.ErrServerClosed) {
			return nil
		}
		return fmt.Errorf("serve: %w", err)
	case <-ctx.Done():
		logger.Info("shutting down", "drain_timeout", timeout.String())
	}

	drain, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()

	if err := server.Shutdown(drain); err != nil {
		return fmt.Errorf("shutdown: %w", err)
	}

	return nil
}

// underBooth serves the front-end for a page addressed under /b/{booth}/ by stripping the booth
// prefix (a wildcard, so http.StripPrefix cannot). The request is cloned so the logging wrapper
// still sees the original path.
func underBooth(pages http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		under := boothPrefix + r.PathValue(boothParam)

		rest := strings.TrimPrefix(r.URL.Path, under)
		if rest == "" {
			rest = "/"
		}

		// Clone deep-copies the URL, so setting the path here does not touch the caller's.
		asked := r.Clone(r.Context())
		asked.URL.Path = rest

		pages.ServeHTTP(w, asked)
	})
}

// onlyAllows answers the verbs a route does not take with 405, as Go's mux would without the
// front-end fallback.
func onlyAllows(methods ...string) http.Handler {
	allow := strings.Join(methods, ", ")

	return http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Allow", allow)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusMethodNotAllowed)

		// A plain error body, not the push reply envelope.
		_, _ = w.Write([]byte(`{"error":"this path takes ` + allow + `"}` + "\n"))
	})
}

func health(w http.ResponseWriter, _ *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte(`{"status":"ok","service":"` + ServiceName + `"}` + "\n"))
}

func logRequests(logger *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		started := time.Now()
		recorder := &statusRecorder{ResponseWriter: w, status: http.StatusOK}

		next.ServeHTTP(recorder, r)

		// Probes hit /healthz every few seconds; log them at debug.
		level := slog.LevelInfo
		if r.URL.Path == healthPath {
			level = slog.LevelDebug
		}

		logger.Log(r.Context(), level, "request",
			"method", r.Method,
			"path", r.URL.Path,
			"status", recorder.status,
			"duration_ms", time.Since(started).Milliseconds())
	})
}

// statusRecorder remembers the status code so it can be logged.
type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (r *statusRecorder) WriteHeader(status int) {
	r.status = status
	r.ResponseWriter.WriteHeader(status)
}

// Unwrap keeps http.ResponseController working through the wrapper, so the WebSocket upgrade can
// hijack the connection.
func (r *statusRecorder) Unwrap() http.ResponseWriter {
	return r.ResponseWriter
}
