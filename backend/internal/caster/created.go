package caster

import (
	"context"
	"crypto/rand"
	"encoding/base32"
	"errors"
	"fmt"
	"net/http"
	"strings"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// boothsPath is where a player asks for a booth of their own. It is the only unauthenticated write,
// bounded by a per-source rate limit and a ceiling on how many booths the process holds.
const boothsPath = "/booths"

// boothLife is how long a created booth lasts. Short-lived booths need no backups or accounts, and
// bound resources by concurrent booths. A constant because the creation page promises 24 hours.
const boothLife = 24 * time.Hour

// idBytes and tokenBytes are how much randomness each carries. The id is public: 48 bits (ten
// base32 characters) is too many to enumerate yet short enough to read aloud. Tokens are the
// booth's only security, so 256 bits.
const (
	idBytes    = 6
	tokenBytes = 32
)

// errBoothsFull means the process already holds as many booths as `LMS_MAX_BOOTHS` allows.
var errBoothsFull = errors.New("this service is already holding as many booths as it will hold")

// boothRetryAfter is how long, in seconds, a caller refused at the ceiling is told to wait. Slots
// free only as day-old booths expire, so an hour avoids pointless polling.
const boothRetryAfter = "3600"

// reapInterval is how often expired booths are swept up. Expiry is enforced per request, so this
// only bounds how long an expired booth's memory and rows linger.
const reapInterval = 5 * time.Minute

// Registry is where created booths are kept, so they survive a restart. Separate from Records,
// which is narrowed to one booth, so a booth-scoped handler cannot change which booths exist.
type Registry interface {
	// CreateBooth writes one booth's hashes and its two timestamps.
	CreateBooth(ctx context.Context, booth tournament.CreatedBooth) error

	// CreatedBooths is every booth this table holds, expired ones included.
	CreatedBooths(ctx context.Context) ([]tournament.CreatedBooth, error)

	// DropBooth removes one booth and everything it owns, across every table that carries a
	// booth_id. Idempotent: dropping a booth that is already gone is not an error.
	DropBooth(ctx context.Context, id string) error
}

// createdBooth is the creation reply. The tokens are not stored and cannot be shown again.
type createdBooth struct {
	ID string `json:"id"`

	Ingest   string `json:"ingestToken"`
	View     string `json:"viewToken"`
	Operator string `json:"operatorToken"`

	// Expires is when the booth and everything in it goes away, as RFC 3339.
	Expires string `json:"expiresAt"`

	// Dashboard is the URL to open, already carrying the id.
	Dashboard string `json:"dashboardUrl"`

	// IngestURL is the push URL for the player's `lms-tournament.yaml`.
	IngestURL string `json:"ingestUrl"`
}

// createBooth makes one booth on request. Nothing is read from the request body: the id is always
// minted, so a caller can never claim an existing or reserved id.
func (s *Server) createBooth(w http.ResponseWriter, r *http.Request) {
	source := sourceOf(r, s.cfg.ClientIPHeader)

	// Rate limit first: there is no token to check, and the limit protects the randomness, hashing
	// and database write below.
	if allowed, firstRefusal := s.boothLimit.allow(source); !allowed {
		if firstRefusal {
			// Once per source per episode.
			s.logger.Warn("rate-limiting booth creation",
				"per_second", s.cfg.BoothRate, "remote", source)
		}

		w.Header().Set("Retry-After", retryAfter(s.cfg.BoothRate))

		// 429 means this caller is too fast; the ceiling's 503 means no room for anybody.
		writeError(w, http.StatusTooManyRequests,
			"too many booths asked for from your address; a booth lasts 24 hours, so you should "+
				"need one about once. Wait a moment and try again")

		return
	}

	if s.registry == nil {
		// No database: refuse rather than keep a booth only in memory, which a restart would lose.
		writeError(w, http.StatusServiceUnavailable,
			"this service does not create booths, because it has no database to keep one in")

		return
	}

	made, err := s.mintBooth(r.Context())

	switch {
	case errors.Is(err, errBoothsFull):
		// 503: the service is full for everybody, unlike the per-caller 429.
		s.logger.Warn("refused a booth: this service is full",
			"max_booths", s.cfg.MaxBooths, "remote", source)

		w.Header().Set("Retry-After", boothRetryAfter)

		writeError(w, http.StatusServiceUnavailable,
			"this service is full: it is already running as many booths as it can hold. "+
				"Every booth expires within 24 hours, so try again later")

		return

	case err != nil:
		s.logger.Error("could not create a booth", "error", err.Error())
		writeError(w, http.StatusServiceUnavailable, "the booth could not be created; nothing was kept")

		return
	}

	s.logger.Info("booth created", "booth", made.ID, "expires_at", made.Expires)

	replyJSON(w, http.StatusCreated, made)
}

// mintBooth makes the booth, writes its row, then adds it to the map; a row without a map entry is
// harmless, the reverse would vanish on restart. The ceiling is checked cheaply first, then
// atomically by addIfRoom; a loser's row is dropped so a restart cannot exceed the ceiling.
func (s *Server) mintBooth(ctx context.Context) (createdBooth, error) {
	if s.cfg.BoothsLimited() && s.booths.countLive(s.now()) >= s.cfg.MaxBooths {
		return createdBooth{}, errBoothsFull
	}

	id, err := newBoothID()
	if err != nil {
		return createdBooth{}, err
	}

	tokens := [3]string{}
	for i := range tokens {
		if tokens[i], err = newToken(); err != nil {
			return createdBooth{}, err
		}
	}

	ingest, view, operator := hashToken(tokens[0]), hashToken(tokens[1]), hashToken(tokens[2])

	created := s.now()
	expires := created.Add(boothLife)

	if err := s.registry.CreateBooth(ctx, tournament.CreatedBooth{
		ID:           id,
		IngestHash:   ingest.stored(),
		ViewHash:     view.stored(),
		OperatorHash: operator.stored(),
		CreatedAt:    created,
		ExpiresAt:    expires,
	}); err != nil {
		return createdBooth{}, err
	}

	room := s.booths.addIfRoom(&Booth{
		id: id, ingest: ingest, view: view, operator: operator, expiresAt: expires,
		store: NewStore(), hub: NewHub(s.cfg.MaxLiveClients), console: &console{},
		cameras: newCameras(),
	}, s.cfg.MaxBooths, created)

	if !room {
		if err := s.registry.DropBooth(ctx, id); err != nil {
			// Only warn: the caller is refused anyway, and the reaper removes the row on expiry.
			s.logger.Warn("could not drop the row of a booth refused at the ceiling",
				"booth", id, "error", err.Error())
		}

		return createdBooth{}, errBoothsFull
	}

	return createdBooth{
		ID:        id,
		Ingest:    tokens[0],
		View:      tokens[1],
		Operator:  tokens[2],
		Expires:   expires.UTC().Format(time.RFC3339),
		Dashboard: boothPrefix + id + "/",
		IngestURL: boothPrefix + id + ingestPath,
	}, nil
}

// LoadBooths restores created booths into the map at startup, since a booth outlives the process.
// Live match state is not restored. Expired booths are skipped; the reaper drops their rows.
func (s *Server) LoadBooths(ctx context.Context) error {
	if s.registry == nil {
		return nil
	}

	kept, err := s.registry.CreatedBooths(ctx)
	if err != nil {
		return fmt.Errorf("read the booths this service has created: %w", err)
	}

	now := s.now()
	restored := 0

	for _, booth := range kept {
		if booth.Expired(now) {
			continue
		}

		s.booths.add(&Booth{
			id:        booth.ID,
			ingest:    storedCredential(booth.IngestHash),
			view:      storedCredential(booth.ViewHash),
			operator:  storedCredential(booth.OperatorHash),
			expiresAt: booth.ExpiresAt,
			store:     NewStore(),
			hub:       NewHub(s.cfg.MaxLiveClients),
			console:   &console{},
			cameras:   newCameras(),
		})

		restored++
	}

	s.logger.Info("booths", "created_and_still_live", restored, "rows", len(kept))

	return nil
}

// Reap drops every expired booth every reapInterval until ctx is cancelled. It only reclaims
// resources; booths.asked already refuses expired booths on every request.
func (s *Server) Reap(ctx context.Context) {
	ticker := time.NewTicker(reapInterval)
	defer ticker.Stop()

	// Sweep once at start for booths that expired while the process was down.
	s.reap(ctx)

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			s.reap(ctx)
		}
	}
}

// reap is one sweep, driven by the registry rows rather than the map so booths not loaded in memory
// are cleaned too. Both drops are idempotent, so a failure is retried next sweep. Configured booths
// are not in the registry and are never reaped.
func (s *Server) reap(ctx context.Context) {
	if s.registry == nil {
		return
	}

	kept, err := s.registry.CreatedBooths(ctx)
	if err != nil {
		// Warn: expired booths already refuse requests, and the next sweep retries.
		s.logger.Warn("could not read the booths to reap", "error", err.Error())

		return
	}

	now := s.now()

	for _, booth := range kept {
		if !booth.Expired(now) {
			continue
		}

		// Memory first, so no push lands while its rows are being deleted.
		s.booths.drop(booth.ID)

		if err := s.registry.DropBooth(ctx, booth.ID); err != nil {
			s.logger.Warn("could not drop an expired booth's rows",
				"booth", booth.ID, "error", err.Error())

			continue
		}

		s.logger.Info("booth expired", "booth", booth.ID)
	}
}

// newBoothID is a booth's public name: lower-case unpadded base32, within the character set
// `config.validBoothID` accepts.
func newBoothID() (string, error) {
	raw := make([]byte, idBytes)
	if _, err := rand.Read(raw); err != nil {
		return "", err
	}

	id := strings.ToLower(base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(raw))

	// Unreachable at this length, but never hand out the reserved tournament id.
	if id == config.TournamentBooth {
		return newBoothID()
	}

	return id, nil
}

// newToken is one of a booth's three secrets: 256 bits, lower-case unpadded base32, which survives
// hand-copying into YAML better than base64.
func newToken() (string, error) {
	raw := make([]byte, tokenBytes)
	if _, err := rand.Read(raw); err != nil {
		return "", err
	}

	return strings.ToLower(base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(raw)), nil
}
