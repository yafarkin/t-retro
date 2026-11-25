ALTER TABLE discussion_state ADD COLUMN selection_version INTEGER NOT NULL DEFAULT 0;
ALTER TABLE actions ADD COLUMN description TEXT CHECK (description IS NULL OR length(trim(description)) BETWEEN 1 AND 4000);

ALTER TABLE comments ADD COLUMN created_version INTEGER;
UPDATE comments
SET created_version = (
    SELECT d.result_version
    FROM command_deduplication d
    WHERE d.meeting_id = comments.meeting_id AND d.result_id = comments.id
      AND d.operation_type = 'CreateComment'
);

ALTER TABLE decisions ADD COLUMN created_version INTEGER;
UPDATE decisions
SET created_version = (
    SELECT d.result_version
    FROM command_deduplication d
    WHERE d.meeting_id = decisions.meeting_id AND d.result_id = decisions.id
      AND d.operation_type = 'CreateDecision'
);

ALTER TABLE actions ADD COLUMN created_version INTEGER;
UPDATE actions
SET created_version = (
    SELECT d.result_version
    FROM command_deduplication d
    WHERE d.meeting_id = actions.meeting_id AND d.result_id = actions.id
      AND d.operation_type = 'CreateAction'
);
