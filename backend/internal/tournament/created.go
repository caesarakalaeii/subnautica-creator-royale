package tournament

import (
	"context"
	"fmt"
	"time"
)

// CreatedBooth is one self-service booth: its id, the SHA-256 hashes of its three tokens, and its
// lifetime. Booths from configuration are not stored in this table.
type CreatedBooth struct {
	ID string

	IngestHash   string
	ViewHash     string
	OperatorHash string

	CreatedAt time.Time
	ExpiresAt time.Time
}

// Expired reports whether this booth's lifetime is over.
func (b CreatedBooth) Expired(now time.Time) bool {
	return !now.Before(b.ExpiresAt)
}

// CreateBooth stores one booth. Unscoped, since this table decides which booths exist.
func (s *Store) CreateBooth(ctx context.Context, booth CreatedBooth) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO booths (id, ingest_hash, view_hash, operator_hash, created_at, expires_at)
		VALUES ($1, $2, $3, $4, $5, $6)`,
		booth.ID, booth.IngestHash, booth.ViewHash, booth.OperatorHash,
		booth.CreatedAt, booth.ExpiresAt)
	if err != nil {
		return fmt.Errorf("write the booth down: %w", err)
	}

	return nil
}

// CreatedBooths is every booth in the table, oldest first, including expired ones so the reaper can
// drop them.
func (s *Store) CreatedBooths(ctx context.Context) ([]CreatedBooth, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, ingest_hash, view_hash, operator_hash, created_at, expires_at
		FROM booths
		ORDER BY created_at, id`)
	if err != nil {
		return nil, fmt.Errorf("read the booths: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []CreatedBooth{}
	for rows.Next() {
		var booth CreatedBooth
		if err := rows.Scan(&booth.ID, &booth.IngestHash, &booth.ViewHash, &booth.OperatorHash,
			&booth.CreatedAt, &booth.ExpiresAt); err != nil {
			return nil, fmt.Errorf("read the booths: %w", err)
		}

		held = append(held, booth)
	}

	return held, rows.Err()
}

// DropBooth removes one booth and every row it owns, in one transaction. Every table is deleted
// explicitly, children first, rather than relying on cascades. Idempotent, since the reaper repeats.
// The `booths` row goes last, as it is what tells the reaper there is work.
func (s *Store) DropBooth(ctx context.Context, id string) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("drop booth %s: %w", id, err)
	}
	defer func() { _ = tx.Rollback() }()

	for _, statement := range []string{
		`DELETE FROM group_members WHERE booth_id = $1`,
		`DELETE FROM groups WHERE booth_id = $1`,
		`DELETE FROM final_series WHERE booth_id = $1`,
		`DELETE FROM overrides WHERE booth_id = $1`,
		`DELETE FROM people WHERE booth_id = $1`,
		`DELETE FROM attributions WHERE booth_id = $1`,
		`DELETE FROM adjustments WHERE booth_id = $1`,
		`DELETE FROM commands WHERE booth_id = $1`,
		`DELETE FROM configs WHERE booth_id = $1`,
	} {
		if _, err := tx.ExecContext(ctx, statement, id); err != nil {
			return fmt.Errorf("drop booth %s: %w", id, err)
		}
	}

	if _, err := tx.ExecContext(ctx, `DELETE FROM booths WHERE id = $1`, id); err != nil {
		return fmt.Errorf("drop booth %s: %w", id, err)
	}

	if err := tx.Commit(); err != nil {
		return fmt.Errorf("drop booth %s: %w", id, err)
	}

	return nil
}
