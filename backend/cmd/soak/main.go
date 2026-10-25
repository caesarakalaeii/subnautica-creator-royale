// Command soak drives the booth under sustained load: a pump pushing telemetry at the mod's
// cadence and a set of dashboards and overlays reading it back. It checks that memory stays flat
// and connected clients keep receiving.
//
//	go run ./cmd/soak -duration 48h -rate 1 -clients 12
//	LMS_INGEST_TOKEN=… LMS_VIEW_TOKEN=… go run ./cmd/soak -url https://booth.example -duration 48h
//
// With no -url it starts a booth in this process, configured with the deployed defaults. With -url
// it puts real load on a running service.
//
// It exits non-zero if the run was not clean.
package main

import (
	"context"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/soak"
)

// The mod pushes about once a second; a realistic soak uses that rate.
const (
	defaultRate     = 1.0
	defaultClients  = 12
	defaultDuration = 10 * time.Minute
	defaultReport   = 30 * time.Second
	defaultFixture  = "../docs/telemetry/example.json"
)

func main() {
	duration := flag.Duration("duration", defaultDuration, "how long to push for")
	rate := flag.Float64("rate", defaultRate, "pushes per second")
	clients := flag.Int("clients", defaultClients, "dashboards and overlays to hold open")
	report := flag.Duration("report", defaultReport, "how often to print a line")
	fixture := flag.String("fixture", defaultFixture, "a telemetry document to build the load from")
	url := flag.String("url", "", "a booth that is already running; empty starts one in this process")
	flag.Parse()

	payload, err := os.ReadFile(filepath.Clean(*fixture))
	if err != nil {
		fmt.Fprintf(os.Stderr, "soak: reading %s: %v\n", *fixture, err)
		os.Exit(1)
	}

	options := soak.Options{
		Duration: *duration,
		Rate:     *rate,
		Clients:  *clients,
		Report:   *report,
		Fixture:  payload,
		URL:      *url,
		Observe:  func(r soak.Report) { fmt.Println(r) },
	}

	target := "a booth in this process"
	if *url != "" {
		options.IngestToken = os.Getenv("LMS_INGEST_TOKEN")
		options.ViewToken = os.Getenv("LMS_VIEW_TOKEN")

		if options.IngestToken == "" || options.ViewToken == "" {
			fmt.Fprintln(os.Stderr, "soak: -url needs LMS_INGEST_TOKEN and LMS_VIEW_TOKEN set to that booth's tokens")
			os.Exit(2)
		}

		target = *url
	}

	// Printed first, so the scrollback records what load was applied.
	fmt.Printf("soaking %s for %s: %g pushes/s, %d clients\n", target, *duration, *rate, *clients)

	// Interrupting still prints the final report.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	result, err := soak.Run(ctx, options)
	if err != nil {
		fmt.Fprintf(os.Stderr, "soak: %v\n", err)
		os.Exit(1)
	}

	fmt.Println(result)

	if len(result.Problems) == 0 {
		fmt.Println("clean")
		return
	}

	for _, problem := range result.Problems {
		fmt.Fprintln(os.Stderr, "problem: "+problem)
	}

	os.Exit(1)
}
