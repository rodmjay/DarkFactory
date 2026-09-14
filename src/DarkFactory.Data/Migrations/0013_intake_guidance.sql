-- docs/adr/0037: the project owner's standing decisions about an import —
-- "live scope only; deferred documents produce no nodes" — recorded once
-- on the intake and shown to every extraction of it, so the decision is
-- data rather than something each prompt, or each person, has to remember.

ALTER TABLE intakes ADD COLUMN guidance text;
