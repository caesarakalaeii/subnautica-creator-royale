// Command replay pushes a captured match back into the booth, so the front-end can be built and
// rehearsed without launching Subnautica.
//
// It reads cmd/record's one-document-per-line files and POSTs each to /ingest on a cadence, as the
// mod would. A file with a single document is a still frame; docs/telemetry/example.json is one.
//
//	LMS_INGEST_TOKEN=… go run ./cmd/replay ../docs/telemetry/example.json http://127.0.0.1:8080/ingest
//	LMS_INGEST_TOKEN=… go run ./cmd/replay -loop -interval 500ms match.ndjson http://127.0.0.1:8080/ingest
//
// A development tool; not included in the container image.
package main

import (
	"bufio"
	"bytes"
	"context"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"
)

// defaultInterval matches the mod's push cadence.
const defaultInterval = time.Second

// readLimit bounds one line; a longer line means a corrupt recording.
const readLimit = 8 << 20

func main() {
	interval := flag.Duration("interval", defaultInterval, "wait between pushes")
	loop := flag.Bool("loop", false, "start the recording again when it ends")
	flag.Parse()

	if flag.NArg() != 2 {
		fmt.Fprintln(os.Stderr, "usage: replay [-interval 1s] [-loop] <recording> <ingest url>")
		fmt.Fprintln(os.Stderr, "LMS_INGEST_TOKEN must be set to the booth's push token.")
		os.Exit(2)
	}

	token := os.Getenv("LMS_INGEST_TOKEN")
	if token == "" {
		fmt.Fprintln(os.Stderr, "replay: LMS_INGEST_TOKEN is not set, and the booth refuses a push without it")
		os.Exit(2)
	}

	if err := replay(flag.Arg(0), flag.Arg(1), token, *interval, *loop); err != nil {
		fmt.Fprintln(os.Stderr, "replay:", err)
		os.Exit(1)
	}
}

func replay(path, url, token string, interval time.Duration, loop bool) error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Read once, so editing the file mid-replay has no effect.
	documents, err := load(path)
	if err != nil {
		return err
	}

	if len(documents) == 0 {
		return fmt.Errorf("%s has no telemetry documents in it", path)
	}

	fmt.Fprintf(os.Stderr, "%d documents, one every %s\n", len(documents), interval)

	for pushes := 0; ; {
		for _, document := range documents {
			if err := push(ctx, url, token, document); err != nil {
				// Not fatal: the booth may be restarting, and the next push will land.
				fmt.Fprintf(os.Stderr, "\npush %d: %v\n", pushes, err)
			}

			pushes++
			fmt.Fprintf(os.Stderr, "\r%d pushes", pushes)

			select {
			case <-ctx.Done():
				fmt.Fprintf(os.Stderr, "\nstopped after %d pushes\n", pushes)
				return nil
			case <-time.After(interval):
			}
		}

		if !loop {
			fmt.Fprintf(os.Stderr, "\nreplayed %d documents\n", pushes)
			return nil
		}
	}
}

func load(path string) ([][]byte, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer file.Close()

	var documents [][]byte

	lines := bufio.NewScanner(file)
	lines.Buffer(make([]byte, 0, 64<<10), readLimit)

	for lines.Scan() {
		line := bytes.TrimSpace(lines.Bytes())
		if len(line) == 0 {
			continue
		}

		documents = append(documents, bytes.Clone(line))
	}

	return documents, lines.Err()
}

func push(ctx context.Context, url, token string, document []byte) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(document))
	if err != nil {
		return err
	}

	request.Header.Set("Authorization", "Bearer "+token)
	request.Header.Set("Content-Type", "application/json")

	response, err := http.DefaultClient.Do(request)
	if err != nil {
		return err
	}
	defer response.Body.Close()

	body, _ := io.ReadAll(io.LimitReader(response.Body, 1<<12))

	if response.StatusCode != http.StatusAccepted {
		return fmt.Errorf("booth answered %s: %s", response.Status, bytes.TrimSpace(body))
	}

	return nil
}
