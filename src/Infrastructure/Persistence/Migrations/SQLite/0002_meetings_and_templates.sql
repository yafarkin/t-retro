CREATE TABLE meetings (
    id TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    phase TEXT NOT NULL CHECK (phase IN ('Draft', 'Collect', 'Group', 'Vote', 'Discuss', 'Closed')),
    anonymous_cards INTEGER NOT NULL CHECK (anonymous_cards IN (0, 1)),
    private_collection INTEGER NOT NULL CHECK (private_collection IN (0, 1)),
    anonymous_votes INTEGER NOT NULL CHECK (anonymous_votes IN (0, 1)),
    vote_mode TEXT NOT NULL CHECK (vote_mode IN ('Percent', 'Fixed')),
    vote_percent INTEGER NOT NULL CHECK (vote_percent BETWEEN 0 AND 100),
    vote_minimum INTEGER NOT NULL CHECK (vote_minimum >= 1),
    vote_fixed INTEGER CHECK (vote_fixed IS NULL OR vote_fixed >= 1),
    facilitator_id TEXT,
    invitation_version INTEGER NOT NULL CHECK (invitation_version >= 1),
    version INTEGER NOT NULL CHECK (version >= 1),
    board_version INTEGER NOT NULL CHECK (board_version >= 1),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (facilitator_id) REFERENCES users(id)
);

CREATE TABLE members (
    meeting_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('Active', 'Excluded')),
    joined_at_utc TEXT NOT NULL,
    excluded_at_utc TEXT,
    PRIMARY KEY (meeting_id, user_id),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE columns (
    meeting_id TEXT NOT NULL,
    id TEXT NOT NULL,
    name TEXT NOT NULL,
    color TEXT NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    PRIMARY KEY (meeting_id, id),
    UNIQUE (meeting_id, position),
    FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE
);

CREATE TABLE templates (
    id TEXT NOT NULL PRIMARY KEY,
    owner_id TEXT NOT NULL,
    name TEXT NOT NULL,
    visibility TEXT NOT NULL CHECK (visibility IN ('Personal', 'Shared')),
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    FOREIGN KEY (owner_id) REFERENCES users(id)
);

CREATE TABLE template_columns (
    template_id TEXT NOT NULL,
    id TEXT NOT NULL,
    name TEXT NOT NULL,
    color TEXT NOT NULL,
    position INTEGER NOT NULL CHECK (position >= 0),
    PRIMARY KEY (template_id, id),
    UNIQUE (template_id, position),
    FOREIGN KEY (template_id) REFERENCES templates(id) ON DELETE CASCADE
);

CREATE INDEX idx_members_user ON members(user_id, status);
CREATE INDEX idx_templates_owner ON templates(owner_id);
