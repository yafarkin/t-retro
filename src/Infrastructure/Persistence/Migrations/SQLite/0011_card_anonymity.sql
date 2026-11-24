ALTER TABLE cards ADD COLUMN anonymous INTEGER NOT NULL DEFAULT 0 CHECK (anonymous IN (0, 1));
ALTER TABLE command_deduplication ADD COLUMN anonymous_card INTEGER NOT NULL DEFAULT 0 CHECK (anonymous_card IN (0, 1));
