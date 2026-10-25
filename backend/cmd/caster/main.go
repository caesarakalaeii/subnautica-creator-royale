// Command caster runs the tournament's casting-booth service: telemetry ingest, WebSocket fan-out,
// the dashboard, overlays and the bracket.
package main

import (
	"context"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/caster"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

func main() {
	cfg, err := config.Load(os.Getenv)
	if err != nil {
		// Too early for the logger: its level and format come from the config that just failed.
		fmt.Fprintf(os.Stderr, "invalid configuration: %v\n", err)
		os.Exit(1)
	}

	logger := caster.NewLogger(cfg, os.Stdout)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	store, closeRecords := openRecords(ctx, cfg, logger)
	defer closeRecords()

	server := caster.NewServer(cfg, logger).WithRecords(recordsFor(store)).WithBooths(registry(store))

	// Restore booths created before this process started. Not fatal: the live path does not need them.
	if err := server.LoadBooths(ctx); err != nil {
		logger.Error("could not read the booths this service has created", "error", err.Error())
	}

	// Sweep expired booths for the life of the process. Every request checks expiry itself, so a
	// stalled sweep only delays cleanup.
	go server.Reap(ctx)

	if err := server.ListenAndServe(ctx); err != nil {
		logger.Error("stopped", "error", err)
		os.Exit(1)
	}

	logger.Info("stopped cleanly")
}

// openRecords connects to the database and migrates it, returning nil rather than exiting when it
// cannot. The live path never touches the database, so an outage should only cost the bracket page
// and writes. Logged at error level, since nothing else will show it.
func openRecords(ctx context.Context, cfg config.Config, logger *slog.Logger) (*tournament.Store, func()) {
	nothing := func() {}

	if !cfg.DatabaseEnabled() {
		logger.Warn("no database is configured, so attribution and the bracket are not kept",
			"hint", "set DB_HOST, DB_NAME, DB_USER and DB_PASSWORD, or LMS_DATABASE_URL")

		return nil, nothing
	}

	store, err := tournament.Open(ctx, cfg.DatabaseURL)
	if err != nil {
		logger.Error("could not reach the database, so attribution and the bracket are not kept",
			"database", cfg.DatabaseDescription(), "error", err.Error())

		return nil, nothing
	}

	if err := store.Migrate(ctx); err != nil {
		logger.Error("could not migrate the database, so attribution and the bracket are not kept",
			"database", cfg.DatabaseDescription(), "error", err.Error())
		_ = store.Close()

		return nil, nothing
	}

	applied, err := store.Applied(ctx)
	if err != nil {
		logger.Warn("could not read which migrations are applied", "error", err.Error())
	}

	logger.Info("database", "database", cfg.DatabaseDescription(), "migrations", applied)

	return store, func() {
		if err := store.Close(); err != nil {
			logger.Warn("database did not close cleanly", "error", err.Error())
		}
	}
}

// recordsFor narrows the store to the booth whose token was presented. It returns an untyped nil
// when there is no database, since a nil *tournament.Store in the interface would not compare nil.
func recordsFor(store *tournament.Store) caster.RecordsFor {
	if store == nil {
		return nil
	}

	return func(booth string) caster.Records { return store.For(booth) }
}

// registry is the unscoped store used for booth creation and reaping, or an untyped nil when there
// is no database, so booth creation is refused.
func registry(store *tournament.Store) caster.Registry {
	if store == nil {
		return nil
	}

	return store
}
