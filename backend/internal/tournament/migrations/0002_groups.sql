-- The bracket's groups. This is the plan rather than evidence, so it is replaced, not appended to.
-- The final is not stored: its seats are computed from the standings.
CREATE TABLE IF NOT EXISTS groups (
    number INTEGER PRIMARY KEY,
    name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS group_members (
    group_number INTEGER NOT NULL REFERENCES groups (number) ON DELETE CASCADE,

    player TEXT NOT NULL,

    -- The order the operator listed members in.
    seat INTEGER NOT NULL,

    PRIMARY KEY (group_number, player)
);
