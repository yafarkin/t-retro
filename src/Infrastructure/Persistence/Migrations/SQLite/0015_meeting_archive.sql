ALTER TABLE meetings ADD COLUMN archived_at_utc TEXT;

CREATE INDEX idx_meetings_facilitator_archive ON meetings(facilitator_id, archived_at_utc)
    WHERE archived_at_utc IS NOT NULL;
