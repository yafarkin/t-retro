CREATE TABLE cards (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    column_id TEXT NOT NULL,
    text TEXT NOT NULL CHECK (length(text) BETWEEN 1 AND 4000),
    version INTEGER NOT NULL CHECK (version >= 1),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, column_id) REFERENCES columns(meeting_id, id)
);

CREATE TABLE card_authors (
    meeting_id TEXT NOT NULL,
    card_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    PRIMARY KEY (meeting_id, card_id),
    FOREIGN KEY (meeting_id, card_id) REFERENCES cards(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id)
);

CREATE TABLE command_deduplication (
    meeting_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    fingerprint TEXT NOT NULL,
    result_id TEXT,
    result_version INTEGER NOT NULL,
    created_at_utc TEXT NOT NULL,
    PRIMARY KEY (meeting_id, user_id, operation_id),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE INDEX idx_cards_column ON cards(meeting_id, column_id, created_at_utc, id);
CREATE INDEX idx_card_authors_user ON card_authors(meeting_id, user_id);
