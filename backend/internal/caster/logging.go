package caster

import (
	"io"
	"log/slog"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// ServiceName tags every log line with this service's name.
const ServiceName = "lms-caster"

// NewLogger builds the service logger: JSON by default for log pipelines, text for a terminal.
func NewLogger(cfg config.Config, out io.Writer) *slog.Logger {
	options := &slog.HandlerOptions{Level: cfg.LogLevel}

	var handler slog.Handler
	if cfg.LogFormat == config.LogFormatText {
		handler = slog.NewTextHandler(out, options)
	} else {
		handler = slog.NewJSONHandler(out, options)
	}

	return slog.New(handler).With("service", ServiceName)
}
