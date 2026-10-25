-- The people on the roster, assigned to groups before anybody joins. This is the plan, so it is
-- edited in place; the append-only record of exceptions is `overrides` below.
CREATE TABLE IF NOT EXISTS people (
    -- A surrogate key so a rename is an update, not a delete that drops the person from their group.
    id BIGSERIAL PRIMARY KEY,

    -- The Nitrox name they join with: the only handle the mod has on a player.
    name TEXT NOT NULL,

    -- The display name. Defaults to the Nitrox name.
    label TEXT NOT NULL,

    notes TEXT NOT NULL DEFAULT '',

    added_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Unique case-insensitively, matching how the mod compares player names.
CREATE UNIQUE INDEX IF NOT EXISTS people_name_idx ON people (lower(name));

-- Everybody already in a group becomes a person, so existing membership survives.
INSERT INTO people (name, label)
SELECT DISTINCT ON (lower(player)) player, player
FROM group_members
ORDER BY lower(player), player
ON CONFLICT DO NOTHING;

-- Group membership references people instead of names.
ALTER TABLE group_members ADD COLUMN IF NOT EXISTS person_id BIGINT REFERENCES people (id) ON DELETE CASCADE;

UPDATE group_members m SET person_id = p.id FROM people p WHERE lower(p.name) = lower(m.player);

ALTER TABLE group_members DROP CONSTRAINT IF EXISTS group_members_pkey;
ALTER TABLE group_members DROP COLUMN IF EXISTS player;
ALTER TABLE group_members ALTER COLUMN person_id SET NOT NULL;
ALTER TABLE group_members ADD PRIMARY KEY (group_number, person_id);

-- Exceptions to the derived role rule. Append-only and last-one-wins, like attributions; a `derived`
-- row takes an override back.
CREATE TABLE IF NOT EXISTS overrides (
    id BIGSERIAL PRIMARY KEY,

    person_id BIGINT NOT NULL REFERENCES people (id) ON DELETE CASCADE,

    role TEXT NOT NULL CHECK (role IN ('ghost', 'contestant', 'derived')),

    note TEXT NOT NULL DEFAULT '',

    -- A label the operator chose, not an identity.
    written_by TEXT NOT NULL,

    at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS overrides_person_idx ON overrides (person_id, id);
