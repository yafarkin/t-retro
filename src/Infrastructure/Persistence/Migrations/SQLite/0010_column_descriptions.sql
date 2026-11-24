ALTER TABLE columns ADD COLUMN description TEXT CHECK (length(description) <= 400);
ALTER TABLE template_columns ADD COLUMN description TEXT CHECK (length(description) <= 400);
