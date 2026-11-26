CREATE TABLE schema_migrations (
    version INTEGER NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE users (
    id UUID NOT NULL PRIMARY KEY,
    subject TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE meetings (
    id UUID NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    phase TEXT NOT NULL CHECK (phase IN ('Draft', 'Collect', 'Group', 'Vote', 'Discuss', 'Closed')),
    anonymous_cards BOOLEAN NOT NULL,
    private_collection BOOLEAN NOT NULL,
    anonymous_votes BOOLEAN NOT NULL CHECK (anonymous_votes),
    vote_mode TEXT NOT NULL CHECK (vote_mode IN ('Percent', 'Fixed')),
    vote_percent INTEGER NOT NULL CHECK (vote_percent BETWEEN 0 AND 100),
    vote_minimum INTEGER NOT NULL CHECK (vote_minimum >= 1),
    vote_fixed INTEGER CHECK (vote_fixed IS NULL OR vote_fixed >= 1),
    facilitator_id UUID,
    invitation_version INTEGER NOT NULL CHECK (invitation_version >= 1),
    version INTEGER NOT NULL CHECK (version >= 1),
    board_version INTEGER NOT NULL CHECK (board_version >= 1),
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    FOREIGN KEY (facilitator_id) REFERENCES users(id)
);

CREATE TABLE members (
    meeting_id UUID NOT NULL,
    user_id UUID NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('Active', 'Excluded')),
    joined_at_utc TIMESTAMPTZ NOT NULL,
    excluded_at_utc TIMESTAMPTZ,
    PRIMARY KEY (meeting_id, user_id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE columns (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    name TEXT NOT NULL,
    color TEXT NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    PRIMARY KEY (meeting_id, id),
    UNIQUE (meeting_id, position),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE
);

CREATE TABLE templates (
    id UUID NOT NULL PRIMARY KEY,
    owner_id UUID NOT NULL,
    name TEXT NOT NULL,
    visibility TEXT NOT NULL CHECK (visibility IN ('Personal', 'Shared')),
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    FOREIGN KEY (owner_id) REFERENCES users(id)
);

CREATE TABLE template_columns (
    template_id UUID NOT NULL,
    id UUID NOT NULL,
    name TEXT NOT NULL,
    color TEXT NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    PRIMARY KEY (template_id, id),
    UNIQUE (template_id, position),
    FOREIGN KEY (template_id) REFERENCES templates(id) ON DELETE CASCADE
);

CREATE INDEX idx_members_user ON members(user_id, status);
CREATE INDEX idx_templates_owner ON templates(owner_id);

ALTER TABLE meetings ADD COLUMN facilitator_election_deadline_utc TIMESTAMPTZ;

ALTER TABLE meetings ADD COLUMN facilitator_generation INTEGER NOT NULL DEFAULT 1;

CREATE TABLE readiness (
    meeting_id UUID NOT NULL,
    user_id UUID NOT NULL,
    phase TEXT NOT NULL CHECK (phase IN ('Collect', 'Group', 'Vote')),
    PRIMARY KEY (meeting_id, user_id, phase),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE INDEX idx_meetings_election_deadline
ON meetings(facilitator_election_deadline_utc);

CREATE TABLE cards (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    column_id UUID NOT NULL,
    text TEXT NOT NULL CHECK (length(text) BETWEEN 1 AND 4000),
    version INTEGER NOT NULL CHECK (version >= 1),
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, column_id) REFERENCES columns(meeting_id, id)
);

CREATE TABLE card_authors (
    meeting_id UUID NOT NULL,
    card_id UUID NOT NULL,
    user_id UUID NOT NULL,
    PRIMARY KEY (meeting_id, card_id),
    FOREIGN KEY (meeting_id, card_id) REFERENCES cards(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id)
);

CREATE TABLE command_deduplication (
    meeting_id UUID NOT NULL,
    user_id UUID NOT NULL,
    operation_id UUID NOT NULL,
    operation_type TEXT NOT NULL,
    fingerprint TEXT NOT NULL,
    result_id UUID,
    result_version INTEGER NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (meeting_id, user_id, operation_id),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE INDEX idx_cards_column ON cards(meeting_id, column_id, created_at_utc, id);
CREATE INDEX idx_card_authors_user ON card_authors(meeting_id, user_id);

CREATE TABLE groups (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    column_id UUID NOT NULL,
    name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 120),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, column_id) REFERENCES columns(meeting_id, id)
);

ALTER TABLE cards ADD COLUMN group_id UUID;

ALTER TABLE command_deduplication ADD COLUMN result_board_version INTEGER;

CREATE TABLE ai_proposals (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    facilitator_id UUID NOT NULL,
    board_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, facilitator_id) REFERENCES members(meeting_id, user_id)
);

CREATE INDEX idx_cards_group ON cards(meeting_id, group_id);
CREATE INDEX idx_ai_proposals_facilitator ON ai_proposals(meeting_id, facilitator_id);

ALTER TABLE meetings ADD COLUMN initial_card_count INTEGER
    CHECK (initial_card_count IS NULL OR initial_card_count >= 0);

CREATE TABLE topics (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    kind TEXT NOT NULL CHECK (kind IN ('Card', 'Group')),
    card_id UUID,
    group_id UUID,
    PRIMARY KEY (meeting_id, id),
    CHECK (
        (kind = 'Card' AND card_id IS NOT NULL AND group_id IS NULL)
        OR (kind = 'Group' AND group_id IS NOT NULL AND card_id IS NULL)),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, card_id) REFERENCES cards(meeting_id, id),
    FOREIGN KEY (meeting_id, group_id) REFERENCES groups(meeting_id, id)
);

CREATE TABLE personal_ballots (
    meeting_id UUID NOT NULL,
    user_id UUID NOT NULL,
    version INTEGER NOT NULL CHECK (version >= 0),
    PRIMARY KEY (meeting_id, user_id),
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id) ON DELETE CASCADE
);

CREATE TABLE votes (
    meeting_id UUID NOT NULL,
    topic_id UUID NOT NULL,
    user_id UUID NOT NULL,
    count INTEGER NOT NULL CHECK (count > 0),
    PRIMARY KEY (meeting_id, topic_id, user_id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, user_id) REFERENCES members(meeting_id, user_id)
);

CREATE UNIQUE INDEX idx_topics_card_source ON topics(meeting_id, card_id) WHERE card_id IS NOT NULL;
CREATE UNIQUE INDEX idx_topics_group_source ON topics(meeting_id, group_id) WHERE group_id IS NOT NULL;
CREATE INDEX idx_votes_user ON votes(meeting_id, user_id);

CREATE TABLE discussion_topics (
    meeting_id UUID NOT NULL,
    topic_id UUID NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Discussed', 'Skipped')),
    PRIMARY KEY (meeting_id, topic_id),
    UNIQUE (meeting_id, position),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE discussion_state (
    meeting_id UUID NOT NULL PRIMARY KEY,
    current_topic_id UUID,
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, current_topic_id) REFERENCES discussion_topics(meeting_id, topic_id)
);

CREATE TABLE comments (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    topic_id UUID NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE decisions (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    topic_id UUID NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE
);

CREATE TABLE actions (
    meeting_id UUID NOT NULL,
    id UUID NOT NULL,
    topic_id UUID NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) BETWEEN 1 AND 4000),
    assignee_id UUID,
    PRIMARY KEY (meeting_id, id),
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, assignee_id) REFERENCES members(meeting_id, user_id)
);

CREATE INDEX idx_comments_topic ON comments(meeting_id, topic_id, id);
CREATE INDEX idx_decisions_topic ON decisions(meeting_id, topic_id, id);
CREATE INDEX idx_actions_topic ON actions(meeting_id, topic_id, id);
CREATE INDEX idx_actions_assignee ON actions(meeting_id, assignee_id);

CREATE TABLE exports (
    meeting_id UUID NOT NULL,
    action_id UUID NOT NULL,
    external_request_id UUID NOT NULL UNIQUE,
    status TEXT NOT NULL CHECK (status IN ('Pending', 'Created', 'Rejected', 'Unknown', 'Unavailable')),
    issue_key TEXT,
    issue_url TEXT,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (meeting_id, action_id),
    FOREIGN KEY (meeting_id, action_id) REFERENCES actions(meeting_id, id) ON DELETE CASCADE,
    CHECK (
        (status = 'Created' AND issue_key IS NOT NULL AND issue_url IS NOT NULL)
        OR (status <> 'Created' AND issue_key IS NULL AND issue_url IS NULL))
);

CREATE INDEX idx_exports_status ON exports(status);

CREATE TABLE meeting_totals (
    meeting_id UUID NOT NULL PRIMARY KEY,
    closed_at_utc TIMESTAMPTZ NOT NULL,
    participant_count INTEGER NOT NULL CHECK (participant_count >= 0),
    card_count INTEGER NOT NULL CHECK (card_count >= 0),
    topic_count INTEGER NOT NULL CHECK (topic_count >= 0),
    vote_count INTEGER NOT NULL CHECK (vote_count >= 0),
    payload_json TEXT NOT NULL,
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE
);

CREATE TABLE topic_totals (
    meeting_id UUID NOT NULL,
    topic_id UUID NOT NULL,
    total_votes INTEGER NOT NULL CHECK (total_votes >= 0),
    PRIMARY KEY (meeting_id, topic_id),
    FOREIGN KEY (meeting_id) REFERENCES meeting_totals(meeting_id) ON DELETE CASCADE,
    FOREIGN KEY (meeting_id, topic_id) REFERENCES topics(meeting_id, id)
);

CREATE TABLE reports (
    meeting_id UUID NOT NULL PRIMARY KEY,
    pdf BYTEA NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    FOREIGN KEY (meeting_id) REFERENCES meeting_totals(meeting_id) ON DELETE CASCADE
);

ALTER TABLE columns ADD COLUMN description TEXT CHECK (length(description) <= 400);
ALTER TABLE template_columns ADD COLUMN description TEXT CHECK (length(description) <= 400);

ALTER TABLE cards ADD COLUMN anonymous BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE command_deduplication ADD COLUMN anonymous_card BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE meetings ADD COLUMN voting_enabled BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE cards ADD COLUMN original_column_color TEXT;

ALTER TABLE discussion_state ADD COLUMN selection_version INTEGER NOT NULL DEFAULT 0;
ALTER TABLE actions ADD COLUMN description TEXT CHECK (description IS NULL OR length(trim(description)) BETWEEN 1 AND 4000);
ALTER TABLE comments ADD COLUMN created_version INTEGER;
ALTER TABLE decisions ADD COLUMN created_version INTEGER;
ALTER TABLE actions ADD COLUMN created_version INTEGER;

ALTER TABLE meetings ADD COLUMN archived_at_utc TIMESTAMPTZ;

CREATE INDEX idx_meetings_facilitator_archive ON meetings(facilitator_id, archived_at_utc)
    WHERE archived_at_utc IS NOT NULL;

ALTER TABLE meetings ADD CONSTRAINT fk_meeting_facilitator
    FOREIGN KEY (id, facilitator_id) REFERENCES members(meeting_id, user_id)
    DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE groups ADD CONSTRAINT uq_group_column UNIQUE (meeting_id, id, column_id);
-- Moves can update the group and its cards in either order within the same transaction.
ALTER TABLE cards ADD CONSTRAINT fk_card_group_column
    FOREIGN KEY (meeting_id, group_id, column_id) REFERENCES groups(meeting_id, id, column_id)
    DEFERRABLE INITIALLY DEFERRED;
