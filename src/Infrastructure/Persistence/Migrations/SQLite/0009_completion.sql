CREATE TABLE meeting_totals (
    meeting_id TEXT NOT NULL PRIMARY KEY,
    closed_at_utc TEXT NOT NULL,
    participant_count INTEGER NOT NULL CHECK (participant_count >= 0),
    card_count INTEGER NOT NULL CHECK (card_count >= 0),
    topic_count INTEGER NOT NULL CHECK (topic_count >= 0),
    vote_count INTEGER NOT NULL CHECK (vote_count >= 0),
    payload_json TEXT NOT NULL,
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE
);

CREATE TABLE topic_totals (
    meeting_id TEXT NOT NULL,
    topic_id TEXT NOT NULL,
    total_votes INTEGER NOT NULL CHECK (total_votes >= 0),
    PRIMARY KEY (meeting_id, topic_id),
    FOREIGN KEY (meeting_id) REFERENCES meeting_totals(meeting_id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id)
);

CREATE TABLE reports (
    meeting_id TEXT NOT NULL PRIMARY KEY,
    pdf BLOB NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY (meeting_id) REFERENCES meeting_totals(meeting_id) ON DELETE CASCADE
);
