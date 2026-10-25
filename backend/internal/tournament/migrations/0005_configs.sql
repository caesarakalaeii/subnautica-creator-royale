-- Tournament config revisions edited from the booth. Append-only, so every revision stays readable
-- and an operator can return to an earlier one. The mod validates the YAML, not the backend.
CREATE TABLE IF NOT EXISTS configs (
    -- The revision the mod dedupes against, like a command's id.
    id BIGSERIAL PRIMARY KEY,

    -- The document exactly as written; never reformatted.
    yaml TEXT NOT NULL,

    -- A label the operator chose, not an identity. It is passed through to the mod's audit log.
    written_by TEXT NOT NULL,

    -- An optional description of the change.
    note TEXT NOT NULL DEFAULT '',

    at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- The lifecycle, filled in from the mod's console replies.
    -- `waiting`: valid and written to the game server's disk, but held because a match is running.
    -- `superseded`: a newer revision was written before this one was sent; only the latest is owed.
    state TEXT NOT NULL DEFAULT 'queued'
        CHECK (state IN ('queued', 'sent', 'waiting', 'superseded', 'adopted', 'refused')),

    sent_at TIMESTAMPTZ,
    settled_at TIMESTAMPTZ,

    -- The mod's reply and the loader's field-path problems, verbatim.
    answer TEXT,
    problems TEXT
);

-- Every push asks whether there is a revision newer than the one the server is running.
CREATE INDEX IF NOT EXISTS configs_newest_idx ON configs (id DESC);
