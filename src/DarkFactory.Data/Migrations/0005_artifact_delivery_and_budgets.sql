-- Three things 3d needs before any agent is written:
--   * artifacts served over HTTP, so a workspace server can fetch a patch
--     (docs/adr/0004's "by reference" stops being local-only here);
--   * token usage per stage attempt, so a member's budget can be enforced
--     (docs/adr/0028);
--   * steering events (docs/adr/0015) reach the next stage.
--
-- The last of those needs no schema: a steer is an ordinary row in `events`,
-- which already exists and is already the run's durable log.

-- ---------------------------------------------------------------------------
-- Artifacts get a content type
-- ---------------------------------------------------------------------------

-- A ChangeSet is a unified diff and a PR body is markdown. A workspace
-- server piping a patch into `git apply` needs it delivered as text, not
-- wrapped in a JSON string it has to unwrap first — so how to serve a body
-- has to travel with the body.
ALTER TABLE artifacts ADD COLUMN content_type character varying(64) NOT NULL DEFAULT 'application/json';

-- ---------------------------------------------------------------------------
-- Token usage per stage attempt (docs/adr/0027, docs/adr/0028)
-- ---------------------------------------------------------------------------

-- One row per attempt, not a running total on the run. A retry that burned
-- half the budget before failing is precisely what a budget should catch,
-- and a counter that only advanced on success would never see it.
CREATE TABLE stage_usages (
    id text NOT NULL,
    run_id text NOT NULL,
    stage character varying(32) NOT NULL,
    attempt integer NOT NULL,
    team_member_id text,
    deployment text,
    input_tokens integer NOT NULL,
    output_tokens integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_stage_usages PRIMARY KEY (id)
);

ALTER TABLE stage_usages ADD CONSTRAINT fk_stage_usages_runs
    FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE stage_usages ADD CONSTRAINT fk_stage_usages_team_members
    FOREIGN KEY (team_member_id) REFERENCES team_members (id);

CREATE INDEX ix_stage_usages_run_id ON stage_usages (run_id);
-- Backs the budget question: "what has this member spent on this run?"
CREATE INDEX ix_stage_usages_run_id_team_member_id ON stage_usages (run_id, team_member_id);

-- ---------------------------------------------------------------------------
-- Grants
-- ---------------------------------------------------------------------------

-- 0002's ALTER DEFAULT PRIVILEGES grants the application role the usual
-- four on tables created after it; revocations never carry forward, so they
-- are spelled out. A usage row is a meter reading: it is written once and
-- then only ever read, and a spend record that can be edited is not one.
REVOKE UPDATE, DELETE ON stage_usages FROM darkfactory_app;
