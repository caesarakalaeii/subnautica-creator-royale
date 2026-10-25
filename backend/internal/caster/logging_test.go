package caster_test

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/caster"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

func TestTheDefaultLoggerWritesStructuredJSONLines(t *testing.T) {
	t.Parallel()

	var out bytes.Buffer
	caster.NewLogger(testConfig(t), &out).Info("match started", "players", 5)

	var line map[string]any
	if err := json.Unmarshal(out.Bytes(), &line); err != nil {
		t.Fatalf("log line is not JSON: %v (%q)", err, out.String())
	}
	if line["msg"] != "match started" || line["level"] != "INFO" {
		t.Errorf("line = %v, want msg and level fields", line)
	}
	if line["players"] != float64(5) {
		t.Errorf("attributes are dropped: %v", line)
	}
}

func TestTheLogLevelFromConfigIsHonoured(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(func(key string) string {
		if key == "LMS_LOG_LEVEL" {
			return "warn"
		}
		return ""
	})
	if err != nil {
		t.Fatalf("config: %v", err)
	}

	var out bytes.Buffer
	logger := caster.NewLogger(cfg, &out)
	logger.Info("this is below the threshold")
	logger.Warn("this is not")

	if strings.Contains(out.String(), "below the threshold") {
		t.Errorf("info was logged at warn level: %q", out.String())
	}
	if !strings.Contains(out.String(), "this is not") {
		t.Errorf("warning was dropped: %q", out.String())
	}
}

func TestTextFormatIsAvailableForALocalTerminal(t *testing.T) {
	t.Parallel()

	cfg, err := config.Load(func(key string) string {
		if key == "LMS_LOG_FORMAT" {
			return "text"
		}
		return ""
	})
	if err != nil {
		t.Fatalf("config: %v", err)
	}

	var out bytes.Buffer
	caster.NewLogger(cfg, &out).Log(nil, slog.LevelInfo, "human readable")

	if !strings.Contains(out.String(), "msg=\"human readable\"") {
		t.Errorf("not text format: %q", out.String())
	}
}
