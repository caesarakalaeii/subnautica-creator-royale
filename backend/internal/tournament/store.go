package tournament

import (
	"context"
	"database/sql"
	"embed"
	"errors"
	"fmt"
	"io/fs"
	"sort"
	"time"

	// The Postgres driver, behind database/sql. lib/pq has no transitive dependencies.
	_ "github.com/lib/pq"
)

// migrations is the schema, applied in name order. Embedded so the image carries its own schema.
//
//go:embed migrations/*.sql
var migrations embed.FS

// connectTimeout bounds Open's first round trip and each migration, so startup does not hang when
// the database is unreachable; only the bracket page needs it.
const connectTimeout = 10 * time.Second

// Group is one group of the bracket, and the players in it in the order they were written down.
type Group struct {
	Number int    `json:"number"`
	Name   string `json:"name"`

	// Members is the players' Nitrox names, in seat order.
	Members []string `json:"members"`

	// Series is the series toward the final this game counts in, or empty for none.
	Series string `json:"series,omitempty"`

	// Seats is the same people with their ids and labels. Read-only: SetGroups takes Members.
	Seats []Seat `json:"seats"`
}

// Store is the backend's durable state. A nil *Store means no database is configured.
//
// For scopes a Store to one booth, and every statement filters on that booth. The unscoped Store
// Open returns is only for the connection and migrations: its empty booth matches no rows.
type Store struct {
	db *sql.DB

	// booth is whose rows these are. It comes from the caller's token, never from a payload.
	booth string
}

// For returns a copy of this store narrowed to one booth, sharing the connection pool.
func (s *Store) For(booth string) *Store {
	narrowed := *s
	narrowed.booth = booth

	return &narrowed
}

// Open connects and verifies the connection. It does not migrate; Migrate does, so a caller can log
// between the two.
func Open(ctx context.Context, dsn string) (*Store, error) {
	db, err := sql.Open("postgres", dsn)
	if err != nil {
		// Never wrapped with the DSN: it carries the password.
		return nil, fmt.Errorf("open the database: %w", err)
	}

	// A small fixed pool: the backend runs a handful of statements against a shared database.
	db.SetMaxOpenConns(2)
	db.SetMaxIdleConns(1)
	db.SetConnMaxLifetime(time.Hour)

	ping, cancel := context.WithTimeout(ctx, connectTimeout)
	defer cancel()

	if err := db.PingContext(ping); err != nil {
		_ = db.Close()

		return nil, fmt.Errorf("reach the database: %w", err)
	}

	return &Store{db: db}, nil
}

// Close gives the pool back.
func (s *Store) Close() error {
	return s.db.Close()
}

// Migrate applies whatever has not been applied, in name order.
//
// Each file runs in one transaction with the row that records it, so a failed migration leaves
// neither half.
func (s *Store) Migrate(ctx context.Context) error {
	if _, err := s.db.ExecContext(ctx, `
		CREATE TABLE IF NOT EXISTS schema_migrations (
			name TEXT PRIMARY KEY,
			applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
		)`); err != nil {
		return fmt.Errorf("record of migrations: %w", err)
	}

	names, err := migrationNames()
	if err != nil {
		return err
	}

	for _, name := range names {
		applied, err := s.alreadyApplied(ctx, name)
		if err != nil {
			return err
		}

		if applied {
			continue
		}

		body, err := migrations.ReadFile("migrations/" + name)
		if err != nil {
			return fmt.Errorf("read migration %s: %w", name, err)
		}

		if err := s.apply(ctx, name, string(body)); err != nil {
			return err
		}
	}

	return nil
}

// Applied lists the migrations this database has applied.
func (s *Store) Applied(ctx context.Context) ([]string, error) {
	rows, err := s.db.QueryContext(ctx,
		`SELECT name FROM schema_migrations ORDER BY name`)
	if err != nil {
		return nil, fmt.Errorf("read the record of migrations: %w", err)
	}
	defer func() { _ = rows.Close() }()

	names := []string{}
	for rows.Next() {
		var name string
		if err := rows.Scan(&name); err != nil {
			return nil, fmt.Errorf("read the record of migrations: %w", err)
		}

		names = append(names, name)
	}

	return names, rows.Err()
}

// Add writes one attribution and gives back the row as the database stamped it.
//
// Rows are only ever appended; a correction is another row and the last one wins.
func (s *Store) Add(ctx context.Context, row Attribution) (Attribution, error) {
	var actor any
	if row.Actor != "" {
		actor = row.Actor
	}

	err := s.db.QueryRowContext(ctx, `
		INSERT INTO attributions (booth_id, group_number, generation, victim, actor, written_by)
		VALUES ($1, $2, $3, $4, $5, $6)
		RETURNING id, at`,
		s.booth, row.Group, row.Generation, row.Victim, actor, row.By).Scan(&row.ID, &row.At)
	if err != nil {
		return Attribution{}, fmt.Errorf("write the attribution: %w", err)
	}

	return row, nil
}

// Attributions is every row, in the order they were written.
//
// Ordered by id, because the last row for a victim wins.
func (s *Store) Attributions(ctx context.Context) ([]Attribution, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, group_number, generation, victim, actor, at, written_by
		FROM attributions
		WHERE booth_id = $1
		ORDER BY id`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the attributions: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Attribution{}
	for rows.Next() {
		var row Attribution
		var actor sql.NullString

		if err := rows.Scan(&row.ID, &row.Group, &row.Generation, &row.Victim, &actor,
			&row.At, &row.By); err != nil {
			return nil, fmt.Errorf("read the attributions: %w", err)
		}

		row.Actor = actor.String
		held = append(held, row)
	}

	return held, rows.Err()
}

// AddAdjustment appends one adjustment and returns the row as the database stamped it.
func (s *Store) AddAdjustment(ctx context.Context, row Adjustment) (Adjustment, error) {
	err := s.db.QueryRowContext(ctx, `
		INSERT INTO adjustments (booth_id, group_number, player, points, reason, written_by)
		VALUES ($1, $2, $3, $4, $5, $6)
		RETURNING id, at`,
		s.booth, row.Group, row.Name, row.Points, row.Reason, row.By).Scan(&row.ID, &row.At)
	if err != nil {
		return Adjustment{}, fmt.Errorf("write the adjustment: %w", err)
	}

	return row, nil
}

// Adjustments is every row, in the order they were written.
func (s *Store) Adjustments(ctx context.Context) ([]Adjustment, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, group_number, player, points, reason, at, written_by
		FROM adjustments
		WHERE booth_id = $1
		ORDER BY id`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the adjustments: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Adjustment{}
	for rows.Next() {
		var row Adjustment
		if err := rows.Scan(&row.ID, &row.Group, &row.Name, &row.Points, &row.Reason,
			&row.At, &row.By); err != nil {
			return nil, fmt.Errorf("read the adjustments: %w", err)
		}

		held = append(held, row)
	}

	return held, rows.Err()
}

// Groups is the bracket's shape, lowest group number first.
func (s *Store) Groups(ctx context.Context) ([]Group, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT g.number, g.name, g.series, p.id, p.name, p.label
		FROM groups g
		LEFT JOIN group_members m ON m.booth_id = g.booth_id AND m.group_number = g.number
		LEFT JOIN people p ON p.booth_id = m.booth_id AND p.id = m.person_id
		WHERE g.booth_id = $1
		ORDER BY g.number, m.seat`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the groups: %w", err)
	}
	defer func() { _ = rows.Close() }()

	groups := []Group{}
	for rows.Next() {
		var number int
		var name, series string
		var person sql.NullInt64
		var player, label sql.NullString

		if err := rows.Scan(&number, &name, &series, &person, &player, &label); err != nil {
			return nil, fmt.Errorf("read the groups: %w", err)
		}

		// One row per member; a group with no members yields one row with a null player.
		if len(groups) == 0 || groups[len(groups)-1].Number != number {
			groups = append(groups, Group{
				Number: number, Name: name, Series: series, Members: []string{}, Seats: []Seat{},
			})
		}

		if player.Valid {
			last := &groups[len(groups)-1]
			last.Members = append(last.Members, player.String)
			last.Seats = append(last.Seats, Seat{
				Person: person.Int64, Name: player.String, Label: label.String,
			})
		}
	}

	return groups, rows.Err()
}

// Final is the final's spec in seating order. Empty means the top of the whole table is seated.
func (s *Store) Final(ctx context.Context) ([]FinalSeries, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT series, seats FROM final_series WHERE booth_id = $1 ORDER BY position`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the final: %w", err)
	}
	defer func() { _ = rows.Close() }()

	final := []FinalSeries{}
	for rows.Next() {
		var row FinalSeries
		if err := rows.Scan(&row.Series, &row.Seats); err != nil {
			return nil, fmt.Errorf("read the final: %w", err)
		}

		final = append(final, row)
	}

	return final, rows.Err()
}

// SetGroups replaces the bracket's shape and the final's spec with the ones given.
//
// The plan is replaced, not appended to. One transaction, so readers never see half a bracket or a
// final naming a series the groups no longer carry.
func (s *Store) SetGroups(ctx context.Context, groups []Group, final []FinalSeries) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("write the groups: %w", err)
	}
	defer func() { _ = tx.Rollback() }()

	// Deleting a group cascades to its members.
	if _, err := tx.ExecContext(ctx,
		`DELETE FROM groups WHERE booth_id = $1`, s.booth); err != nil {
		return fmt.Errorf("write the groups: %w", err)
	}

	if _, err := tx.ExecContext(ctx,
		`DELETE FROM final_series WHERE booth_id = $1`, s.booth); err != nil {
		return fmt.Errorf("write the final: %w", err)
	}

	for position, row := range final {
		if _, err := tx.ExecContext(ctx,
			`INSERT INTO final_series (booth_id, position, series, seats) VALUES ($1, $2, $3, $4)`,
			s.booth, position, row.Series, row.Seats); err != nil {
			return fmt.Errorf("write the final's %s: %w", row.Series, err)
		}
	}

	for _, group := range groups {
		if _, err := tx.ExecContext(ctx,
			`INSERT INTO groups (booth_id, number, name, series) VALUES ($1, $2, $3, $4)`,
			s.booth, group.Number, group.Name, group.Series); err != nil {
			return fmt.Errorf("write group %d: %w", group.Number, err)
		}

		for seat, player := range group.Members {
			// An unknown name becomes a new person labelled with that name. Existing people are never
			// renamed here: a rename edits the person so it keeps their group membership.
			person, err := upsertPerson(ctx, tx, s.booth, player)
			if err != nil {
				return err
			}

			if _, err := tx.ExecContext(ctx,
				`INSERT INTO group_members (booth_id, group_number, person_id, seat)
				VALUES ($1, $2, $3, $4)`,
				s.booth, group.Number, person, seat); err != nil {
				return fmt.Errorf("write %s into group %d: %w", player, group.Number, err)
			}
		}
	}

	if err := tx.Commit(); err != nil {
		return fmt.Errorf("write the groups: %w", err)
	}

	return nil
}

func (s *Store) alreadyApplied(ctx context.Context, name string) (bool, error) {
	var one int

	err := s.db.QueryRowContext(ctx,
		`SELECT 1 FROM schema_migrations WHERE name = $1`, name).Scan(&one)

	switch {
	case errors.Is(err, sql.ErrNoRows):
		return false, nil
	case err != nil:
		return false, fmt.Errorf("read the record of migrations: %w", err)
	default:
		return true, nil
	}
}

func (s *Store) apply(ctx context.Context, name, body string) error {
	run, cancel := context.WithTimeout(ctx, connectTimeout)
	defer cancel()

	tx, err := s.db.BeginTx(run, nil)
	if err != nil {
		return fmt.Errorf("apply migration %s: %w", name, err)
	}
	defer func() { _ = tx.Rollback() }()

	if _, err := tx.ExecContext(run, body); err != nil {
		return fmt.Errorf("apply migration %s: %w", name, err)
	}

	if _, err := tx.ExecContext(run,
		`INSERT INTO schema_migrations (name) VALUES ($1)`, name); err != nil {
		return fmt.Errorf("record migration %s: %w", name, err)
	}

	if err := tx.Commit(); err != nil {
		return fmt.Errorf("apply migration %s: %w", name, err)
	}

	return nil
}

func migrationNames() ([]string, error) {
	found, err := fs.ReadDir(migrations, "migrations")
	if err != nil {
		// Unreachable: the directory is embedded at compile time.
		return nil, fmt.Errorf("read the embedded migrations: %w", err)
	}

	names := make([]string, 0, len(found))
	for _, entry := range found {
		names = append(names, entry.Name())
	}

	// Migrations are numbered so name order is apply order.
	sort.Strings(names)

	return names, nil
}
