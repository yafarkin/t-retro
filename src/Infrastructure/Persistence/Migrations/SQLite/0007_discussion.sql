CREATE TABLE discussion_topics (
    meeting_id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Discussed', 'Skipped')),
    PRIMARY KEY (meeting_id, topic_id),
    UNIQUE (meeting_id, position),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE discussion_state (
    meeting_id TEXT NOT NULL PRIMARY KEY,
    current_topic_id TEXT,
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, current_topic_id) REFERENCES discussion_topics(meeting_id, topic_id)
);

CREATE TABLE comments (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE decisions (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE actions (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    assignee_id TEXT,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, assignee_id) REFERENCES members(meeting_id, user_id)
);

CREATE INDEX idx_comments_topic ON comments(meeting_id, topic_id, id);
CREATE INDEX idx_decisions_topic ON decisions(meeting_id, topic_id, id);
CREATE INDEX idx_actions_topic ON actions(meeting_id, topic_id, id);
CREATE INDEX idx_actions_assignee ON actions(meeting_id, assignee_id);

WITH ranked AS (
    SELECT t.meeting_id, t.id AS topic_id,
           ROW_NUMBER() OVER (
               PARTITION BY t.meeting_id
               ORDER BY COALESCE(SUM(v.count), 0) DESC, col.position, t.id) - 1 AS position
    FROM topics t
    LEFT JOIN votes v ON v.meeting_id = t.meeting_id AND v.topic_id = t.id
    LEFT JOIN cards c ON c.meeting_id = t.meeting_id AND c.id = t.card_id
    LEFT JOIN groups g ON g.meeting_id = t.meeting_id AND g.id = t.group_id
    LEFT JOIN columns col ON col.meeting_id = t.meeting_id
        AND col.id = COALESCE(c.column_id, g.column_id)
    JOIN meetings m ON m.id = t.meeting_id AND m.phase = 'Discuss'
    GROUP BY t.meeting_id, t.id, col.position
)
INSERT INTO discussion_topics (meeting_id, topic_id, position, status)
SELECT meeting_id, topic_id, position, 'Pending' FROM ranked;

INSERT INTO discussion_state (meeting_id, current_topic_id)
SELECT m.id, (
    SELECT d.topic_id FROM discussion_topics d
    WHERE d.meeting_id = m.id AND d.position = 0)
FROM meetings m WHERE m.phase = 'Discuss';
