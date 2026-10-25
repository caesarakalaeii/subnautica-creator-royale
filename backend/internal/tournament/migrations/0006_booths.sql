-- Every row belongs to exactly one booth.
--
-- The booth is taken from the credential presented, never from a payload, so one server cannot
-- write into another booth by naming its id.

-- Existing rows move to the reserved `tournament` booth (config.TournamentBooth), which the bare
-- URLs resolve to.
ALTER TABLE attributions ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE groups ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE group_members ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE people ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE overrides ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE commands ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';
ALTER TABLE configs ADD COLUMN IF NOT EXISTS booth_id TEXT NOT NULL DEFAULT 'tournament';

-- The default is kept on purpose: a row written without a booth lands in the tournament's booth.

-- Group numbers are unique within a booth, not globally.
ALTER TABLE group_members DROP CONSTRAINT IF EXISTS group_members_group_number_fkey;
ALTER TABLE groups DROP CONSTRAINT IF EXISTS groups_pkey;
ALTER TABLE groups ADD PRIMARY KEY (booth_id, number);

ALTER TABLE group_members DROP CONSTRAINT IF EXISTS group_members_pkey;
ALTER TABLE group_members ADD PRIMARY KEY (booth_id, group_number, person_id);
ALTER TABLE group_members ADD CONSTRAINT group_members_group_fkey
    FOREIGN KEY (booth_id, group_number) REFERENCES groups (booth_id, number) ON DELETE CASCADE;

-- Person names are unique case-insensitively within a booth.
DROP INDEX IF EXISTS people_name_idx;
CREATE UNIQUE INDEX IF NOT EXISTS people_booth_name_idx ON people (booth_id, lower(name));

-- Hot-path indexes lead with booth_id, since every hot-path query filters on it first.
DROP INDEX IF EXISTS attributions_match_idx;
CREATE INDEX IF NOT EXISTS attributions_booth_match_idx
    ON attributions (booth_id, group_number, generation, victim, id);

DROP INDEX IF EXISTS commands_owed_idx;
CREATE INDEX IF NOT EXISTS commands_booth_owed_idx
    ON commands (booth_id, id) WHERE state IN ('queued', 'sent');

DROP INDEX IF EXISTS configs_newest_idx;
CREATE INDEX IF NOT EXISTS configs_booth_newest_idx ON configs (booth_id, id DESC);

DROP INDEX IF EXISTS overrides_person_idx;
CREATE INDEX IF NOT EXISTS overrides_booth_person_idx ON overrides (booth_id, person_id, id);

-- Ids stay globally unique rather than per booth: command and config ids are watermarks the mod
-- dedupes against, and per-booth sequences would collide.
