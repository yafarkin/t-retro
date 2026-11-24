ALTER TABLE meetings ADD COLUMN voting_enabled INTEGER NOT NULL DEFAULT 1 CHECK (voting_enabled IN (0, 1));
