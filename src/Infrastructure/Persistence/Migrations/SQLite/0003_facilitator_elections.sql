ALTER TABLE meetings ADD COLUMN facilitator_election_deadline_utc TEXT;

ALTER TABLE meetings ADD COLUMN facilitator_generation INTEGER NOT NULL DEFAULT 1;

CREATE TABLE readiness (
    meeting_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    phase TEXT NOT NULL,
    PRIMARY KEY (meeting_id, user_id, phase),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE INDEX idx_meetings_election_deadline
ON meetings(facilitator_election_deadline_utc);
