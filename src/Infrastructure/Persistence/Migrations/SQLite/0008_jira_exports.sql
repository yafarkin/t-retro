CREATE TABLE exports (
    meeting_id TEXT NOT NULL,
    action_id TEXT NOT NULL,
    external_request_id TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Created', 'Rejected', 'Unknown', 'Unavailable')),
    issue_key TEXT,
    issue_url TEXT,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (meeting_id, action_id),
    FOREIGN KEY (meeting_id, action_id) REFERENCES actions(meeting_id, id) ON DELETE CASCADE,
    CHECK (
        (status = 'Created' AND issue_key IS NOT NULL AND issue_url IS NOT NULL)
        OR (status <> 'Created' AND issue_key IS NULL AND issue_url IS NULL))
);

CREATE INDEX idx_exports_status ON exports(status);
