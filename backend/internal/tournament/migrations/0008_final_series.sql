-- The final is seated from series of games. Which games form a series is stored on the group row;
-- how many seats each series sends is the final's spec. Both are replaced whole by PUT /groups.
-- The final's players are still computed, not stored.
ALTER TABLE groups ADD COLUMN IF NOT EXISTS series TEXT NOT NULL DEFAULT '';

CREATE TABLE IF NOT EXISTS final_series (
    booth_id TEXT NOT NULL DEFAULT 'tournament',

    -- Seating order: a player seated by an earlier series is skipped by later ones.
    position INTEGER NOT NULL,

    series TEXT NOT NULL,
    seats INTEGER NOT NULL CHECK (seats > 0),

    PRIMARY KEY (booth_id, position),
    UNIQUE (booth_id, series)
);
