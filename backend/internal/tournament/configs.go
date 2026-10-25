package tournament

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"strings"
	"time"
)

// The states a config revision passes through. `waiting` means valid and written to the game
// server's disk, but held because a match is running.
const (
	ConfigQueued     = "queued"
	ConfigSent       = "sent"
	ConfigWaiting    = "waiting"
	ConfigSuperseded = "superseded"
	ConfigAdopted    = "adopted"
	ConfigRefused    = "refused"
)

// Config is one revision of the tournament's rules and its lifecycle.
type Config struct {
	Revision int64  `json:"revision"`
	Yaml     string `json:"yaml"`
	By       string `json:"by"`
	Note     string `json:"note"`
	State    string `json:"state"`

	// Answer is the game server's reply and Problems its loader's field paths, both verbatim.
	Answer   string   `json:"answer"`
	Problems []string `json:"problems"`

	At        time.Time  `json:"-"`
	SentAt    *time.Time `json:"-"`
	SettledAt *time.Time `json:"-"`
}

// WriteConfig stores a new revision and supersedes any earlier one still owed. A config is a state,
// not an event, so only the latest revision is sent. One transaction.
func (s *Store) WriteConfig(ctx context.Context, yaml, by, note string) (Config, error) {
	row := Config{Yaml: yaml, By: by, Note: note, State: ConfigQueued, Problems: []string{}}

	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return Config{}, fmt.Errorf("write the config: %w", err)
	}
	defer func() { _ = tx.Rollback() }()

	if _, err := tx.ExecContext(ctx, `
		UPDATE configs SET state = $1 WHERE booth_id = $2 AND state IN ($3, $4, $5)`,
		ConfigSuperseded, s.booth, ConfigQueued, ConfigSent, ConfigWaiting); err != nil {
		return Config{}, fmt.Errorf("supersede the earlier configs: %w", err)
	}

	if err := tx.QueryRowContext(ctx, `
		INSERT INTO configs (booth_id, yaml, written_by, note)
		VALUES ($1, $2, $3, $4)
		RETURNING id, at`, s.booth, yaml, by, note).Scan(&row.Revision, &row.At); err != nil {
		return Config{}, fmt.Errorf("write the config: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return Config{}, fmt.Errorf("write the config: %w", err)
	}

	return row, nil
}

// NewestConfig is the latest revision, whatever its state, or false when there is none.
func (s *Store) NewestConfig(ctx context.Context) (Config, bool, error) {
	row := Config{Problems: []string{}}

	var answer, problems sql.NullString
	var sent, settled sql.NullTime

	err := s.db.QueryRowContext(ctx, `
		SELECT id, yaml, written_by, note, state, answer, problems, at, sent_at, settled_at
		FROM configs
		WHERE booth_id = $1
		ORDER BY id DESC
		LIMIT 1`, s.booth).Scan(&row.Revision, &row.Yaml, &row.By, &row.Note, &row.State,
		&answer, &problems, &row.At, &sent, &settled)

	switch {
	case errors.Is(err, sql.ErrNoRows):
		return Config{}, false, nil
	case err != nil:
		return Config{}, false, fmt.Errorf("read the newest config: %w", err)
	}

	row.Answer = answer.String
	row.Problems = splitProblems(problems.String)

	if sent.Valid {
		at := sent.Time
		row.SentAt = &at
	}

	if settled.Valid {
		at := settled.Time
		row.SettledAt = &at
	}

	return row, true, nil
}

// ConfigOwed is the revision one game server should be sent, or false when it is up to date. Only a
// revision above the one it reports and not yet settled is owed. `waiting` is still owed so a
// restarted server, which reports zero, gets it again.
func (s *Store) ConfigOwed(ctx context.Context, running int64) (Config, bool, error) {
	row := Config{Problems: []string{}}

	// Only the newest row counts: if it is settled there is nothing to send, even when an older row
	// is still unsettled.
	err := s.db.QueryRowContext(ctx, `
		SELECT id, yaml, written_by, state
		FROM configs
		WHERE booth_id = $1
		ORDER BY id DESC
		LIMIT 1`, s.booth).Scan(&row.Revision, &row.Yaml, &row.By, &row.State)

	switch {
	case errors.Is(err, sql.ErrNoRows):
		return Config{}, false, nil
	case err != nil:
		return Config{}, false, fmt.Errorf("read what config is owed: %w", err)
	}

	if row.Revision <= running {
		return Config{}, false, nil
	}

	switch row.State {
	case ConfigQueued, ConfigSent, ConfigWaiting:
		return row, true, nil
	default:
		// Adopted, refused or superseded: nothing to send until a new revision is written.
		return Config{}, false, nil
	}
}

// ConfigSentTo marks a revision as sent.
func (s *Store) ConfigSentTo(ctx context.Context, revision int64, at time.Time) error {
	if _, err := s.db.ExecContext(ctx, `
		UPDATE configs
		SET state = $1, sent_at = COALESCE(sent_at, $2)
		WHERE booth_id = $3 AND id = $4 AND state = $5`,
		ConfigSent, at, s.booth, revision, ConfigQueued); err != nil {
		return fmt.Errorf("mark config %d sent: %w", revision, err)
	}

	return nil
}

// ConfigSettled records the game server's answer about a revision. `final` distinguishes a refusal
// from a revision that is still waiting.
func (s *Store) ConfigSettled(ctx context.Context, revision int64, adopted, final bool,
	answer string, problems []string, at time.Time) error {
	state := ConfigWaiting

	switch {
	case adopted:
		state = ConfigAdopted
	case final:
		state = ConfigRefused
	}

	// An adopted revision stays adopted, whatever a later resend's answer says.
	if _, err := s.db.ExecContext(ctx, `
		UPDATE configs
		SET state = $1, settled_at = $2, answer = $3, problems = $4
		WHERE booth_id = $5 AND id = $6 AND state <> $7`,
		state, at, answer, strings.Join(problems, "\n"), s.booth, revision,
		ConfigAdopted); err != nil {
		return fmt.Errorf("settle config %d: %w", revision, err)
	}

	return nil
}

// Configs is the most recent revisions, newest first, without their YAML documents.
func (s *Store) Configs(ctx context.Context, limit int) ([]Config, error) {
	if limit <= 0 {
		return []Config{}, nil
	}

	rows, err := s.db.QueryContext(ctx, `
		SELECT id, written_by, note, state, answer, problems, at
		FROM configs
		WHERE booth_id = $1
		ORDER BY id DESC
		LIMIT $2`, s.booth, limit)
	if err != nil {
		return nil, fmt.Errorf("read the configs: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Config{}
	for rows.Next() {
		row := Config{Problems: []string{}}
		var answer, problems sql.NullString

		if err := rows.Scan(&row.Revision, &row.By, &row.Note, &row.State,
			&answer, &problems, &row.At); err != nil {
			return nil, fmt.Errorf("read the configs: %w", err)
		}

		row.Answer = answer.String
		row.Problems = splitProblems(problems.String)

		held = append(held, row)
	}

	return held, rows.Err()
}

// ConfigAt is one revision's document, for restoring a previous revision.
func (s *Store) ConfigAt(ctx context.Context, revision int64) (Config, bool, error) {
	row := Config{Problems: []string{}}

	err := s.db.QueryRowContext(ctx, `
		SELECT id, yaml, written_by, note, state FROM configs WHERE booth_id = $1 AND id = $2`,
		s.booth, revision).Scan(&row.Revision, &row.Yaml, &row.By, &row.Note, &row.State)

	switch {
	case errors.Is(err, sql.ErrNoRows):
		return Config{}, false, nil
	case err != nil:
		return Config{}, false, fmt.Errorf("read config %d: %w", revision, err)
	}

	return row, true, nil
}

// splitProblems splits the stored newline-separated field paths.
func splitProblems(joined string) []string {
	if strings.TrimSpace(joined) == "" {
		return []string{}
	}

	return strings.Split(joined, "\n")
}
