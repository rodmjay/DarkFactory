-- The project's standing team (docs/adr/0028) and the storage that makes
-- "what did the model see?" answerable (docs/adr/0023).
--
-- Minimal on purpose. docs/adr/0028's `team_revisions`, `skills`,
-- `skill_revisions` and `runs.team_snapshot_id` are step 3d work: a run
-- recording its team snapshot only means something once teams can change,
-- and nothing changes them yet.

-- ---------------------------------------------------------------------------
-- Teams (docs/adr/0028) — replaces the never-built `agent_policies`
-- ---------------------------------------------------------------------------

CREATE TABLE teams (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    name text NOT NULL,
    is_active boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_teams PRIMARY KEY (id)
);

CREATE TABLE team_members (
    id text NOT NULL,
    team_id text NOT NULL,
    role text NOT NULL,
    -- A Foundry deployment name (docs/adr/0027), never a vendor model id.
    -- Which model that resolves to is a Foundry concern, which is what
    -- makes swapping it a config change rather than a migration.
    deployment text NOT NULL,
    fallback_deployment text,
    token_budget integer,
    capabilities_json text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_team_members PRIMARY KEY (id)
);

CREATE TABLE assignments (
    id text NOT NULL,
    team_id text NOT NULL,
    point text NOT NULL,
    team_member_id text NOT NULL,
    CONSTRAINT pk_assignments PRIMARY KEY (id)
);

ALTER TABLE teams ADD CONSTRAINT fk_teams_projects
    FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE team_members ADD CONSTRAINT fk_team_members_teams
    FOREIGN KEY (team_id) REFERENCES teams (id);
ALTER TABLE assignments ADD CONSTRAINT fk_assignments_teams
    FOREIGN KEY (team_id) REFERENCES teams (id);
ALTER TABLE assignments ADD CONSTRAINT fk_assignments_team_members
    FOREIGN KEY (team_member_id) REFERENCES team_members (id);

CREATE INDEX ix_teams_project_id ON teams (project_id);

-- docs/adr/0028 leaves multiple concurrent teams open, and the schema
-- allows it — but v1 resolves a conversation against "the project's active
-- team", and that phrase has to have exactly one answer. A partial unique
-- index makes a second active team impossible to insert rather than merely
-- discouraged in a comment.
CREATE UNIQUE INDEX ux_teams_project_id_active ON teams (project_id) WHERE is_active;

CREATE UNIQUE INDEX ix_team_members_team_id_role ON team_members (team_id, role);

-- An assignment map with two answers for "who plans?" is not a map.
CREATE UNIQUE INDEX ix_assignments_team_id_point ON assignments (team_id, point);
CREATE INDEX ix_assignments_team_member_id ON assignments (team_member_id);

-- ---------------------------------------------------------------------------
-- Artifacts can belong to a conversation (docs/adr/0023)
-- ---------------------------------------------------------------------------

-- A ContextPack — everything the model was shown for one turn — is stored
-- as an artifact so it inherits content addressing and the
-- factory://artifacts/{id} ref scheme (docs/adr/0004) rather than needing a
-- second store. But a conversation is no longer part of a run at all
-- (docs/adr/0003, as amended), so run_id has to become optional.
ALTER TABLE artifacts ALTER COLUMN run_id DROP NOT NULL;
ALTER TABLE artifacts ADD COLUMN conversation_id text;

ALTER TABLE artifacts ADD CONSTRAINT fk_artifacts_conversations
    FOREIGN KEY (conversation_id) REFERENCES conversations (id);

CREATE INDEX ix_artifacts_conversation_id ON artifacts (conversation_id);

-- Exactly one owner, enforced here rather than trusted to callers.
-- Dropping the NOT NULL above would otherwise make an artifact belonging to
-- nothing at all perfectly legal, and an orphaned artifact is invisible to
-- every query that goes looking for one.
ALTER TABLE artifacts ADD CONSTRAINT ck_artifacts_exactly_one_owner
    CHECK ((run_id IS NULL) <> (conversation_id IS NULL));

-- ---------------------------------------------------------------------------
-- Grants
-- ---------------------------------------------------------------------------

-- 0002's ALTER DEFAULT PRIVILEGES already grants the application role
-- SELECT/INSERT/UPDATE/DELETE on tables created after it, so the three new
-- tables need no GRANT. They do need their revocations spelled out: default
-- privileges deliberately do not carry revocations forward (see 0002 and
-- README.md).
--
-- team_members and assignments stay mutable — retargeting a role to a
-- different deployment is exactly the config change docs/adr/0022 wants to
-- be cheap. Nothing here is append-only, so only DELETE is withheld, and
-- only where losing the row would strip a run of the reason it behaved as
-- it did.
REVOKE DELETE ON teams FROM darkfactory_app;
REVOKE DELETE ON team_members FROM darkfactory_app;
