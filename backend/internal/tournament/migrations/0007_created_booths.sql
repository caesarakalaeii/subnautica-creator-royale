-- Self-service booths created over HTTP, each with an expiry. Booths from configuration are not
-- stored here and never expire; rows here may be reaped along with everything the booth owns.
CREATE TABLE IF NOT EXISTS booths (
    -- The booth id used in URLs and in every other table's booth_id. Not a foreign key, because
    -- configured booths have no row here.
    id TEXT PRIMARY KEY,

    -- Hex SHA-256 of each token. Tokens are 256 random bits, so no salt or work factor is needed.
    ingest_hash TEXT NOT NULL,
    view_hash TEXT NOT NULL,
    operator_hash TEXT NOT NULL,

    created_at TIMESTAMPTZ NOT NULL,

    -- Stored rather than derived, so changing the lifetime constant does not move existing expiries.
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS booths_expiry_idx ON booths (expires_at);
