CREATE TABLE characters (
    id          SERIAL PRIMARY KEY,
    codename    TEXT NOT NULL,          -- "Phoenix"
    real_name   TEXT,                  -- "Jean Grey"
    status      TEXT,                  -- active | deceased | villain | depowered
    power_class TEXT[],                -- ARRAY['telepath','telekinetic']
    first_issue TEXT,                  -- "X-Men #1 (1963)"
    notes       TEXT
);

CREATE TABLE team_memberships (
    character_id INT REFERENCES characters(id),
    team         TEXT,                 -- "X-Men", "X-Force", "Brotherhood"
    joined_year  INT,
    left_year    INT                   -- NULL = still a member
);

CREATE TABLE relationships (
    character_a  INT REFERENCES characters(id),
    character_b  INT REFERENCES characters(id),
    relation     TEXT                  -- "sibling", "nemesis", "mentor", "lover"
);