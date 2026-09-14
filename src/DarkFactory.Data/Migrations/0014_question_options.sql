-- docs/adr/0041: a question is a decision, so it carries the paths open to
-- whoever answers it — 2 to 4 options, each with its consequence, as JSON
-- in contracts/schemas/decision.schema.json's option shape. Null until an
-- extraction or a paths suggestion supplies them.

ALTER TABLE intake_questions ADD COLUMN options_json text;
