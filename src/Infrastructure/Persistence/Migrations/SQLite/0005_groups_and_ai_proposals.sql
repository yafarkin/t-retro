CREATE TABLE groups (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    column_id TEXT NOT NULL,
    name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 120),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, column_id) REFERENCES columns(meeting_id, id)
);

ALTER TABLE cards ADD COLUMN group_id TEXT;

ALTER TABLE command_deduplication ADD COLUMN result_board_version INTEGER;

CREATE TABLE ai_proposals (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    facilitator_id TEXT NOT NULL,
    board_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, facilitator_id) REFERENCES members(meeting_id, user_id)
);

CREATE INDEX idx_cards_group ON cards(meeting_id, group_id);
CREATE INDEX idx_ai_proposals_facilitator ON ai_proposals(meeting_id, facilitator_id);
