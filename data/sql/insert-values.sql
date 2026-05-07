-- characters
INSERT INTO public.characters(id, codename, real_name, status, power_class, first_issue, notes)
	VALUES
    (1, 'Phoenix', 'Jean Grey', 'active', ARRAY['Astral projection', 'Telekinesis', 'Telepathy', 'Empathy'], 'X-Men #1 (July, 1963)', NULL),
    (2, 'Professor X', 'Charles Xavier', 'active', ARRAY['Telepathy', 'Telekinesis', 'Genius intelligence'], 'X-Men #1 (July, 1963)', NULL),
    (3, 'Cyclops', 'Scott Summers', 'active', ARRAY['Optic blasts', 'Spatial awareness', 'Energy resistance', 'Expert pilot', 'Master tactician and strategist', 'Master martial artist & hand-to-hand combatant'], 'X-Men #1 (July, 1963)', NULL),
    (4, 'Havok', 'Alex Summers', 'active', ARRAY['Cosmic energy absorption', 'Plasma beams'], 'X-Men #54 (January, 1969)', NULL),
    (5, 'Mister Sinister', 'Nathaniel Essex', 'active', ARRAY['Molecular Manipulation', 'Shapeshifting', 'Regenerative Healing Factor', 'Longevity', 'Enhanced Durability', 'Superhuman Strength', 'Telepathy', 'Telekinesis', 'Energy Projection', 'Expert genetic engineer and surgeon', 'Cloning Expert', 'Precognition'], 'Uncanny X-Men #213 (October, 1986)', NULL),
    (6, 'Cable', 'Nathan Summers', 'active', ARRAY['telepathic','telekinetic'], 'Uncanny X-Men #201 (October, 1985)', NULL),
    (7, 'Goblin Queen', 'Madelyne Pryor', 'active', ARRAY['Telekinesis','Telepathy', 'Sorcery'], 'Uncanny X-Men #168 (January, 1983)', NULL);

-- team_memberships
-- Phoenix
INSERT INTO public.team_memberships(character_id, team)
    VALUES (1, 'X-Men'), (1, 'Quiet Council of Krakoa'), (1, 'Brides of Set'), (1, 'Muir Island X-Men'), (1, 'Clan Rebellion'), (1, 'X-Terminators'), (1, 'Hellfire Club'), (1, 'The Twelve'), (1, 'X-Factor'), (1, 'X-Force');

-- Cyclops
INSERT INTO public.team_memberships(character_id, team)
    VALUES (3, 'X-Men'), (3, 'X-Force'), (3, 'X-Factor'), (3, 'Phoenix Five'), (3, 'X-Corporation'), (3, 'Hounds'), (3, 'Starjammers'), (3, 'Time-Displaced Cyclops: Champions');

-- Mister Sinister
INSERT INTO public.team_memberships(character_id, team)
    VALUES (5, 'Marauders'), (5, 'Nasty Boys'), (5, 'Intelligencia'), (5, 'Quiet Council of Krakoa'), (5, 'Hellions');

-- relationships
INSERT INTO public.relationships(character_a, character_b, relation)
    VALUES
    (1, 3, 'lover'), -- Phoenix and Cyclops
    (3, 4, 'sibling'), -- Cyclops and Havok
    (3, 7, 'spouse'), -- Cyclops and Goblin Queen
    (4, 7, 'lover'), -- Havok and Goblin Queen
    (2, 1, 'mentor'), -- Professor X and Phoenix
    (2, 3, 'mentor'), -- Professor X and Cyclops
    (7, 6, 'parent'), -- Goblin Queen and Cable
    (3, 6, 'parent'), -- Cyclops and Cable,
    (6, 7, 'child'), -- Cable and Goblin Queen
    (6, 3, 'child'), -- Cable and Cyclops
    (7, 1, 'clone'); -- Goblin Queen and Phoenix