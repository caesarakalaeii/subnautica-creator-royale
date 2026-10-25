-- Points an operator gives or takes against one game, with a reason. Append-only: taking one back
-- is writing its opposite.
CREATE TABLE IF NOT EXISTS adjustments (
    id BIGSERIAL PRIMARY KEY,
    booth_id TEXT NOT NULL,

    -- The game it is for, which decides the series the points count in.
    group_number INTEGER NOT NULL,

    player TEXT NOT NULL,
    points INTEGER NOT NULL,
    reason TEXT NOT NULL,
    at TIMESTAMPTZ NOT NULL DEFAULT now(),
    written_by TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS adjustments_booth_idx ON adjustments (booth_id, id);
