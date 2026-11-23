ALTER TABLE meetings ADD COLUMN initial_card_count INTEGER
    CHECK (initial_card_count IS NULL OR initial_card_count >= 0);

CREATE TABLE topics (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    kind TEXT NOT NULL CHECK (kind IN ('Card', 'Group')),
    card_id TEXT,
    group_id TEXT,
    PRIMARY KEY (meeting_id, id),
    CHECK (
        (kind = 'Card' AND card_id IS NOT NULL AND group_id IS NULL)
        OR (kind = 'Group' AND group_id IS NOT NULL AND card_id IS NULL)),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, card_id) REFERENCES cards(meeting_id, id),
    FOREIGN KEY (meeting_id, group_id) REFERENCES groups(meeting_id, id)
);

CREATE TABLE personal_ballots (
    meeting_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    version INTEGER NOT NULL CHECK (version >= 0),
    PRIMARY KEY (meeting_id, user_id),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE TABLE votes (
    meeting_id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    count INTEGER NOT NULL CHECK (count > 0),
    PRIMARY KEY (meeting_id, topic_id, user_id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id)
);

CREATE UNIQUE INDEX idx_topics_card_source ON topics(meeting_id, card_id) WHERE card_id IS NOT NULL;
CREATE UNIQUE INDEX idx_topics_group_source ON topics(meeting_id, group_id) WHERE group_id IS NOT NULL;
CREATE INDEX idx_votes_user ON votes(meeting_id, user_id);

UPDATE meetings
SET initial_card_count = (SELECT COUNT(*) FROM cards WHERE cards.meeting_id = meetings.id)
WHERE phase IN ('Vote', 'Discuss', 'Closed');

INSERT INTO topics (meeting_id, id, kind, card_id, group_id)
SELECT meeting_id, id, 'Card', id, NULL FROM cards
WHERE group_id IS NULL
  AND EXISTS (
      SELECT 1 FROM meetings
      WHERE meetings.id = cards.meeting_id AND meetings.phase IN ('Vote', 'Discuss', 'Closed'));

INSERT INTO topics (meeting_id, id, kind, card_id, group_id)
SELECT meeting_id, id, 'Group', NULL, id FROM groups
WHERE EXISTS (
    SELECT 1 FROM meetings
    WHERE meetings.id = groups.meeting_id AND meetings.phase IN ('Vote', 'Discuss', 'Closed'));

INSERT INTO personal_ballots (meeting_id, user_id, version)
SELECT meeting_id, user_id, 0 FROM members
WHERE EXISTS (
    SELECT 1 FROM meetings
    WHERE meetings.id = members.meeting_id AND meetings.phase IN ('Vote', 'Discuss'));
