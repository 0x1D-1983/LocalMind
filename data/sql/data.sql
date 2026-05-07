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

-- characters
INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (1, 'Phoenix', 'Jean Grey', 'active', ARRAY['Astral projection', 'Telekinesis', 'Telepathy', 'Empathy'], 'X-Men #1 (July, 1963)', NULL);


INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (2, 'Professor X', 'Charles Xavier', 'active', ARRAY['Telepathy', 'Telekinesis', 'Genius intelligence'], 'X-Men #1 (July, 1963)', NULL);


INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (3, 'Cyclops', 'Scott Summers', 'active', ARRAY['Optic blasts', 'Spatial awareness', 'Energy resistance', 'Expert pilot', 'Master tactician and strategist', 'Master martial artist & hand-to-hand combatant'], 'X-Men #1 (July, 1963)', NULL);

INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (4, 'Havok', 'Alex Summers', 'active', ARRAY['Cosmic energy absorption', 'Plasma beams'], 'X-Men #54 (January, 1969)', NULL);

INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (5, 'Mister Sinister', 'Nathaniel Essex', 'active', ARRAY['Molecular Manipulation', 'Shapeshifting', 'Regenerative Healing Factor', 'Longevity', 'Enhanced Durability', 'Superhuman Strength', 'Telepathy', 'Telekinesis', 'Energy Projection', 'Expert genetic engineer and surgeon', 'Cloning Expert', 'Precognition'], 'Uncanny X-Men #213 (October, 1986)', NULL);

INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (6, 'Cable', 'Nathan Summers', 'active', ARRAY['telepathic','telekinetic'], 'Uncanny X-Men #201 (October, 1985)', NULL);

INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES (7, 'Goblin Queen', 'Madelyne Pryor', 'active', ARRAY['Telekinesis','Telepathy', 'Sorcery'], 'Uncanny X-Men #168 (January, 1983)', NULL);

-- team_memberships

INSERT INTO public.team_memberships(character_id, team)
    VALUES (1, 'X-Men'), (1, 'Quiet Council of Krakoa'), (1, 'Brides of Set'), (1, 'Muir Island X-Men'), (1, 'Clan Rebellion'), (1, 'X-Terminators'), (1, 'Hellfire Club'), (1, 'The Twelve'), (1, 'X-Factor'), (1, 'X-Force');


INSERT INTO public.team_memberships(character_id, team)
    VALUES (3, 'X-Men'), (3, 'X-Force'), (3, 'X-Factor'), (3, 'Phoenix Five'), (3, 'X-Corporation'), (3, 'Hounds'), (3, 'Starjammers'), (3, 'Time-Displaced Cyclops: Champions');

INSERT INTO public.team_memberships(character_id, team)
    VALUES (5, 'Marauders'), (5, 'Nasty Boys'), (5, 'Intelligencia'), (5, 'Quiet Council of Krakoa'), (5, 'Hellions');



-- relationships

INSERT INTO public.relationships(character_a, character_b, relation)
    VALUES (1, 3, 'lover'), (3, 4, 'sibling'), (3, 7, 'spouse'), (4, 7, 'lover'), (2, 1, 'mentor'), (2, 3, 'mentor');