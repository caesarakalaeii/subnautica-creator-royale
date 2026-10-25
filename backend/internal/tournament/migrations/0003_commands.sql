-- Console commands an operator queued for the mod. Rows are never deleted, and the verb, asker and
-- queue time are never rewritten; only the lifecycle columns fill in afterwards.
CREATE TABLE IF NOT EXISTS commands (
    -- The id the mod dedupes against: it ignores anything at or below the highest id it has run.
    id BIGSERIAL PRIMARY KEY,

    -- The verb as typed, whitespace squeezed. Not validated here; the mod decides what a verb may do.
    verb TEXT NOT NULL,

    -- A label the operator chose; the operator token is shared, so this is not an identity.
    asked_by TEXT NOT NULL,

    queued_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- When the command was first handed to a push, and when the mod reported it ran.
    sent_at TIMESTAMPTZ,
    ran_at TIMESTAMPTZ,

    -- The mod's reply, verbatim.
    answer TEXT,

    -- queued -> sent -> ran, or queued -> expired. Expiry uses only the backend's clock, never the
    -- game server's.
    state TEXT NOT NULL DEFAULT 'queued'
        CHECK (state IN ('queued', 'sent', 'ran', 'expired'))
);

-- Every push asks what is still owed, oldest first; finished rows are never read on that path.
CREATE INDEX IF NOT EXISTS commands_owed_idx
    ON commands (id) WHERE state IN ('queued', 'sent');
