package tournament

import (
	"context"
	"database/sql"
	"fmt"
	"strings"
)

// upsertPerson finds a person in one booth by name, ignoring case, or adds them. It never renames:
// a correction is an edit by id (SetPeople), which keeps group membership.
func upsertPerson(ctx context.Context, tx *sql.Tx, booth, name string) (int64, error) {
	var id int64

	err := tx.QueryRowContext(ctx,
		`SELECT id FROM people WHERE booth_id = $1 AND lower(name) = lower($2)`,
		booth, name).Scan(&id)
	if err == nil {
		return id, nil
	}

	if err != sql.ErrNoRows {
		return 0, fmt.Errorf("look for %s: %w", name, err)
	}

	if err := tx.QueryRowContext(ctx,
		`INSERT INTO people (booth_id, name, label) VALUES ($1, $2, $2) RETURNING id`,
		booth, name).Scan(&id); err != nil {
		return 0, fmt.Errorf("write down %s: %w", name, err)
	}

	return id, nil
}

// People is everybody on the booth's roster, ordered by label.
func (s *Store) People(ctx context.Context) ([]Person, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, name, label, notes
		FROM people
		WHERE booth_id = $1
		ORDER BY lower(label), id`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the people: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Person{}
	for rows.Next() {
		var person Person
		if err := rows.Scan(&person.ID, &person.Name, &person.Label, &person.Notes); err != nil {
			return nil, fmt.Errorf("read the people: %w", err)
		}

		held = append(held, person)
	}

	return held, rows.Err()
}

// SetPeople writes the roster: rows with an id are edited, rows without are added, and anybody not
// listed is removed. Editing by id keeps a renamed person in their group. One transaction.
func (s *Store) SetPeople(ctx context.Context, people []Person) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("write the people: %w", err)
	}
	defer func() { _ = tx.Rollback() }()

	kept := make([]int64, 0, len(people))

	for _, person := range people {
		if person.ID == 0 {
			var id int64
			if err := tx.QueryRowContext(ctx, `
				INSERT INTO people (booth_id, name, label, notes) VALUES ($1, $2, $3, $4)
				RETURNING id`,
				s.booth, person.Name, person.Label, person.Notes).Scan(&id); err != nil {
				return fmt.Errorf("write down %s: %w", person.Name, err)
			}

			kept = append(kept, id)

			continue
		}

		result, err := tx.ExecContext(ctx, `
			UPDATE people SET name = $1, label = $2, notes = $3
			WHERE booth_id = $4 AND id = $5`,
			person.Name, person.Label, person.Notes, s.booth, person.ID)
		if err != nil {
			return fmt.Errorf("edit %s: %w", person.Name, err)
		}

		changed, err := result.RowsAffected()
		if err != nil {
			return fmt.Errorf("edit %s: %w", person.Name, err)
		}

		if changed == 0 {
			// An unknown id means the roster changed underneath the caller; refuse rather than insert a
			// duplicate person.
			return fmt.Errorf("there is no person %d to edit; the roster has changed underneath this", person.ID)
		}

		kept = append(kept, person.ID)
	}

	// The booth predicate keeps this from deleting other booths' rosters.
	if _, err := tx.ExecContext(ctx,
		`DELETE FROM people WHERE booth_id = $1 AND NOT (id = ANY($2))`,
		s.booth, int64Array(kept)); err != nil {
		return fmt.Errorf("write the people: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return fmt.Errorf("write the people: %w", err)
	}

	return nil
}

// Overrides is every override ever written, ordered by id because the last one wins.
func (s *Store) Overrides(ctx context.Context) ([]Override, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT o.id, o.person_id, p.name, o.role, o.note, o.written_by, o.at
		FROM overrides o
		JOIN people p ON p.id = o.person_id AND p.booth_id = o.booth_id
		WHERE o.booth_id = $1
		ORDER BY o.id`, s.booth)
	if err != nil {
		return nil, fmt.Errorf("read the overrides: %w", err)
	}
	defer func() { _ = rows.Close() }()

	held := []Override{}
	for rows.Next() {
		var override Override
		var at sql.NullTime

		if err := rows.Scan(&override.ID, &override.Person, &override.Name, &override.Role,
			&override.Note, &override.By, &at); err != nil {
			return nil, fmt.Errorf("read the overrides: %w", err)
		}

		if at.Valid {
			override.At = at.Time.UTC().Format("2006-01-02T15:04:05.000Z")
		}

		held = append(held, override)
	}

	return held, rows.Err()
}

// Override appends one override. Rows are never updated or deleted; `derived` takes one back.
func (s *Store) Override(ctx context.Context, person int64, role, note, by string) (Override, error) {
	written := Override{Person: person, Role: role, Note: note, By: by}

	// The person's name is returned with the row.
	var at sql.NullTime
	// The person is looked up inside this booth, so naming somebody in another booth writes nothing.
	err := s.db.QueryRowContext(ctx, `
		WITH subject AS (
			SELECT id, name FROM people WHERE booth_id = $1 AND id = $2
		), written AS (
			INSERT INTO overrides (booth_id, person_id, role, note, written_by)
			SELECT $1, subject.id, $3, $4, $5 FROM subject
			RETURNING id, at
		)
		SELECT written.id, written.at, subject.name FROM written, subject`,
		s.booth, person, role, note, by).Scan(&written.ID, &at, &written.Name)
	if err != nil {
		return Override{}, fmt.Errorf("write the override: %w", err)
	}

	if at.Valid {
		written.At = at.Time.UTC().Format("2006-01-02T15:04:05.000Z")
	}

	return written, nil
}

// Outstanding is the verbs of every command the mod has not answered yet, so the reconciler does
// not queue the same verb twice.
func (s *Store) Outstanding(ctx context.Context) ([]string, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT verb FROM commands WHERE booth_id = $1 AND state IN ($2, $3) ORDER BY id`,
		s.booth, StateQueued, StateSent)
	if err != nil {
		return nil, fmt.Errorf("read what is outstanding: %w", err)
	}
	defer func() { _ = rows.Close() }()

	verbs := []string{}
	for rows.Next() {
		var verb string
		if err := rows.Scan(&verb); err != nil {
			return nil, fmt.Errorf("read what is outstanding: %w", err)
		}

		verbs = append(verbs, verb)
	}

	return verbs, rows.Err()
}

// Withdraw expires one author's queued commands whose verbs are not in keep. Only commands not yet
// sent are withdrawn, since a sent one may already be running. This stops stale reconcile verbs
// from firing after the intent has moved on.
func (s *Store) Withdraw(ctx context.Context, by string, keep []string) (int, error) {
	result, err := s.db.ExecContext(ctx, `
		UPDATE commands SET state = $1
		WHERE booth_id = $2 AND state = $3 AND asked_by = $4 AND NOT (verb = ANY($5))`,
		StateExpired, s.booth, StateQueued, by, textArray(keep))
	if err != nil {
		return 0, fmt.Errorf("withdraw what %s no longer wants: %w", by, err)
	}

	affected, err := result.RowsAffected()
	if err != nil {
		return 0, fmt.Errorf("withdraw what %s no longer wants: %w", by, err)
	}

	return int(affected), nil
}

// textArray formats values as a Postgres array literal for `ANY($1)`.
func textArray(values []string) string {
	if len(values) == 0 {
		return "{}"
	}

	quoted := make([]string, 0, len(values))
	for _, value := range values {
		escaped := strings.ReplaceAll(value, `\`, `\\`)
		escaped = strings.ReplaceAll(escaped, `"`, `\"`)
		quoted = append(quoted, `"`+escaped+`"`)
	}

	return "{" + strings.Join(quoted, ",") + "}"
}

// int64Array formats ids as a Postgres array literal for `ANY($1)`.
func int64Array(ids []int64) string {
	if len(ids) == 0 {
		return "{}"
	}

	written := make([]string, 0, len(ids))
	for _, id := range ids {
		written = append(written, fmt.Sprintf("%d", id))
	}

	return "{" + strings.Join(written, ",") + "}"
}
