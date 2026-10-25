// Command record captures the casting booth's live feed to a file, one JSON document per line.
//
// Every message is written verbatim, so a recording can be replayed (cmd/replay) as a real fixture.
//
//	go run ./cmd/record "ws://127.0.0.1:8080/live?token=$LMS_VIEW_TOKEN" match.ndjson
package main

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"

	"github.com/coder/websocket"
)

// readLimit bounds one message; full pushes exceed the library's 32 KiB default.
const readLimit = 8 << 20

func main() {
	if len(os.Args) != 3 {
		fmt.Fprintln(os.Stderr, "usage: record <ws url, token included> <output file>")
		os.Exit(2)
	}

	if err := record(os.Args[1], os.Args[2]); err != nil {
		fmt.Fprintln(os.Stderr, "record:", err)
		os.Exit(1)
	}
}

func record(url, path string) error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Appended to, so a reconnect continues the same recording.
	out, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
	if err != nil {
		return err
	}
	defer out.Close()

	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		return err
	}
	defer conn.CloseNow()

	conn.SetReadLimit(readLimit)

	fmt.Fprintf(os.Stderr, "recording to %s — the first message is the whole state\n", path)

	for messages := 0; ; messages++ {
		_, payload, err := conn.Read(ctx)
		if err != nil {
			// Ctrl-C and the far end closing are both normal ends; the file is complete because every write
			// is synced.
			fmt.Fprintf(os.Stderr, "\nstopped after %d messages: %v\n", messages, err)
			return nil
		}

		// Verbatim, one document per line.
		if _, err := out.Write(append(payload, '\n')); err != nil {
			return err
		}

		// Synced per message, so a recording that ends in a crash is still complete.
		if err := out.Sync(); err != nil {
			return err
		}

		if messages%60 == 0 {
			fmt.Fprintf(os.Stderr, "\r%d messages", messages)
		}
	}
}
