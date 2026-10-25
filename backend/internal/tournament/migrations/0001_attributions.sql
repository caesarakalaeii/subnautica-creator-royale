-- Kill attributions written by an operator after the fact.
--
-- Append-only: rows are never edited or deleted, and the last row for a victim wins.
CREATE TABLE IF NOT EXISTS attributions (
    id BIGSERIAL PRIMARY KEY,

    -- The match the kill belongs to, named explicitly by the writer rather than taken from "now".
    -- `group` is a reserved word, so the column carries the suffix and the JSON does not.
    group_number INTEGER NOT NULL,
    generation INTEGER NOT NULL,

    victim TEXT NOT NULL,

    -- NULL means the kill is nobody's, which is how an attribution is taken back.
    actor TEXT,

    -- When the row was written, not when the death happened.
    at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- A label the operator chose; the operator token is shared, so this is not an identity.
    written_by TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS attributions_match_idx
    ON attributions (group_number, generation, victim, id);
