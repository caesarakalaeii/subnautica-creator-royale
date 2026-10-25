package tournament

import (
	"context"
	"database/sql"
	"fmt"
	"time"
)

// The states a command passes through. `sent` means it was put on a reply, not that the mod read
// it, which is why a command is resent until answered.
const (
	StateQueued  = "queued"
	StateSent    = "sent"
	StateRan     = "ran"
	StateExpired = "expired"
)

// MaxCommandsPerReply bounds how many commands one push is handed; the rest go on the next push.
const MaxCommandsPerReply = 8

// Command is one command an operator queued and its lifecycle.
type Command struct {
	ID     int64  `json:"id"`
	Verb   string `json:"verb"`
	By     string `json:"by"`
	State  string `json:"state"`
	Answer string `json:"answer,omitempty"`

	QueuedAt time.Time  `json:"queuedAt"`
	SentAt   *time.Time `json:"sentAt"`
	RanAt    *time.Time `json:"ranAt"`
}

// Queue stores one command and returns the row as the database stamped it. The id comes from a
// sequence because the mod dedupes against it.
func (s *Store) Queue(ctx context.Context, verb, by string) (Command, error) {
	row := Command{Verb: verb, By: by, State: StateQueued}

	err := s.db.QueryRowContext(ctx, `
		INSERT INTO commands (booth_id, verb, asked_by)
		VALUES ($1, $2, $3)
		RETURNING id, queued_at`, s.booth, verb, by).Scan(&row.ID, &row.QueuedAt)
	if err != nil {
		return Command{}, fmt.Errorf("queue the command: %w", err)
	}

	return row, nil
}

// Expire expires every queued or sent command queued before the cutoff and returns how many. Uses
// the backend's clock only, never the game server's. A sent but unanswered command expires too.
func (s *Store) Expire(ctx context.Context, before time.Time) (int, error) {
	result, err := s.db.ExecContext(ctx, `
		UPDATE commands SET state = $1
		WHERE booth_id = $2 AND state IN ($3, $4) AND queued_at < $5`,
		StateExpired, s.booth, StateQueued, StateSent, before)
	if err != nil {
		return 0, fmt.Errorf("expire the commands: %w", err)
	}

	affected, err := result.RowsAffected()
	if err != nil {
		return 0, fmt.Errorf("expire the commands: %w", err)
	}

	return int(affected), nil
}

// Owed is the unexpired, unanswered commands above the mod's watermark, oldest first, at most one
// reply's worth. A restarted mod reports watermark zero and so gets unanswered commands again.
func (s *Store) Owed(ctx context.Context, above int64) ([]Command, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, verb, asked_by, state, queued_at
		FROM commands
		WHERE booth_id = $1 AND state IN ($2, $3) AND id > $4
		ORDER BY id
		LIMIT $5`,
		s.booth, StateQueued, StateSent, above, MaxCommandsPerReply)
	if err != nil {
		return nil, fmt.Errorf("read what is owed: %w", err)
	}
	defer func() { _ = rows.Close() }()

	owed := []Command{}
	for rows.Next() {
		var row Command
		if err := rows.Scan(&row.ID, &row.Verb, &row.By, &row.State, &row.QueuedAt); err != nil {
			return nil, fmt.Errorf("read what is owed: %w", err)
		}

		owed = append(owed, row)
	}

	return owed, rows.Err()
}

// MarkSent marks commands as sent. sent_at keeps the first send time across resends.
func (s *Store) MarkSent(ctx context.Context, ids []int64, at time.Time) error {
	if len(ids) == 0 {
		return nil
	}

	for _, id := range ids {
		if _, err := s.db.ExecContext(ctx, `
			UPDATE commands
			SET state = $1, sent_at = COALESCE(sent_at, $2)
			WHERE booth_id = $3 AND id = $4 AND state = $5`,
			StateSent, at, s.booth, id, StateQueued); err != nil {
			return fmt.Errorf("mark command %d sent: %w", id, err)
		}
	}

	return nil
}

// MarkRan records the mod's answer verbatim. A command already run is not overwritten; an expired
// one moves to `ran`, since the answer shows it ran after all.
func (s *Store) MarkRan(ctx context.Context, id int64, answer string, at time.Time) error {
	if _, err := s.db.ExecContext(ctx, `
		UPDATE commands
		SET state = $1, ran_at = $2, answer = $3
		WHERE booth_id = $4 AND id = $5 AND state IN ($6, $7, $8)`,
		StateRan, at, answer, s.booth, id, StateQueued, StateSent, StateExpired); err != nil {
		return fmt.Errorf("mark command %d run: %w", id, err)
	}

	return nil
}

// Commands is the most recent rows, newest first, at most limit.
func (s *Store) Commands(ctx context.Context, limit int) ([]Command, error) {
	if limit <= 0 {
		return []Command{}, nil
	}

	rows, err := s.db.QueryContext(ctx, `
		SELECT id, verb, asked_by, state, answer, queued_at, sent_at, ran_at
		FROM commands
		WHERE booth_id = $1
		ORDER BY id DESC
		LIMIT $2`, s.booth, limit)
	if err != nil {
		return nil, fmt.Errorf("read the commands: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Command{}
	for rows.Next() {
		var row Command
		var answer sql.NullString
		var sent, ran sql.NullTime

		if err := rows.Scan(&row.ID, &row.Verb, &row.By, &row.State, &answer,
			&row.QueuedAt, &sent, &ran); err != nil {
			return nil, fmt.Errorf("read the commands: %w", err)
		}

		row.Answer = answer.String
		if sent.Valid {
			at := sent.Time
			row.SentAt = &at
		}
		if ran.Valid {
			at := ran.Time
			row.RanAt = &at
		}

		held = append(held, row)
	}

	return held, rows.Err()
}
