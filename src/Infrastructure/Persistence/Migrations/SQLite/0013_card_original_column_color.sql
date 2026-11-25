ALTER TABLE cards ADD COLUMN original_column_color TEXT;

UPDATE cards
SET original_column_color = (
    SELECT color FROM columns
    WHERE columns.meeting_id = cards.meeting_id AND columns.id = cards.column_id
);
