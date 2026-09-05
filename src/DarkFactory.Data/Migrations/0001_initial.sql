-- Initial schema. Generated once from DarkFactoryDbContext's model via
-- Database.GenerateCreateScript() (with EFCore.NamingConventions'
-- snake_case convention applied) to guarantee it matches exactly what the
-- C# model expects to query against, then hand-finished with foreign keys.
-- This file — not the C# model — is the source of truth going forward: see
-- docs/adr/0008-durable-orchestration.md and MigrationRunner.cs. Applied by
-- DarkFactory.Migrate, never by `factory` or `worker` at startup
-- (SchemaGuard.cs refuses to start against a schema this build doesn't
-- expect).

CREATE TABLE projects (
    id character varying(64) NOT NULL,
    org_id character varying(64) NOT NULL,
    name character varying(128) NOT NULL,
    workspace_mcp_url character varying(512) NOT NULL,
    workspace_name text,
    workspace_root text,
    stack_hints text[] NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_projects PRIMARY KEY (id)
);

CREATE TABLE work_items (
    id text NOT NULL,
    org_id text NOT NULL,
    project_id text NOT NULL,
    input text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_work_items PRIMARY KEY (id)
);

CREATE TABLE runs (
    id text NOT NULL,
    org_id text NOT NULL,
    project_id text NOT NULL,
    work_item_id text NOT NULL,
    current_stage character varying(32) NOT NULL,
    status character varying(32) NOT NULL,
    leased_by character varying(128),
    lease_expires_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT pk_runs PRIMARY KEY (id)
);

CREATE TABLE stage_checkpoints (
    id text NOT NULL,
    run_id text NOT NULL,
    stage character varying(32) NOT NULL,
    status character varying(32) NOT NULL,
    artifact_ref text,
    attempt integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_stage_checkpoints PRIMARY KEY (id)
);

CREATE TABLE artifacts (
    id text NOT NULL,
    org_id text NOT NULL,
    project_id text NOT NULL,
    run_id text NOT NULL,
    type text NOT NULL,
    content_json text NOT NULL,
    sha256 character varying(64) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_artifacts PRIMARY KEY (id)
);

CREATE TABLE gates (
    id text NOT NULL,
    run_id text NOT NULL,
    kind character varying(32) NOT NULL,
    status character varying(32) NOT NULL,
    reason text,
    created_at timestamp with time zone NOT NULL,
    resolved_at timestamp with time zone,
    CONSTRAINT pk_gates PRIMARY KEY (id)
);

CREATE TABLE events (
    id text NOT NULL,
    run_id text NOT NULL,
    type character varying(64) NOT NULL,
    data_json text,
    created_at timestamp with time zone NOT NULL,
    published_at timestamp with time zone,
    CONSTRAINT pk_events PRIMARY KEY (id)
);

CREATE TABLE audit_entries (
    id text NOT NULL,
    org_id text NOT NULL,
    project_id text NOT NULL,
    run_id text NOT NULL,
    stage_id text NOT NULL,
    target_server text NOT NULL,
    tool_name text NOT NULL,
    input_ref text,
    success boolean NOT NULL,
    failure_class character varying(32),
    duration_ms bigint NOT NULL,
    cost_usd numeric,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_audit_entries PRIMARY KEY (id)
);

CREATE INDEX ix_artifacts_run_id ON artifacts (run_id);
CREATE INDEX ix_audit_entries_project_id_run_id ON audit_entries (project_id, run_id);
CREATE INDEX ix_events_published_at ON events (published_at) WHERE published_at IS NULL;
CREATE INDEX ix_events_run_id_created_at ON events (run_id, created_at);
CREATE INDEX ix_gates_run_id ON gates (run_id);
CREATE UNIQUE INDEX ix_projects_org_id_name ON projects (org_id, name);
CREATE UNIQUE INDEX ix_projects_org_id_workspace_mcp_url ON projects (org_id, workspace_mcp_url);
CREATE INDEX ix_runs_project_id ON runs (project_id);
CREATE INDEX ix_runs_status_lease_expires_at ON runs (status, lease_expires_at);
CREATE INDEX ix_stage_checkpoints_run_id ON stage_checkpoints (run_id);
CREATE UNIQUE INDEX ix_stage_checkpoints_run_id_stage_attempt ON stage_checkpoints (run_id, stage, attempt);
CREATE INDEX ix_work_items_project_id ON work_items (project_id);

-- Referential integrity (hand-added; GenerateCreateScript doesn't know
-- about relationships the C# model never declared via HasOne/WithMany).
ALTER TABLE work_items ADD CONSTRAINT fk_work_items_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE runs ADD CONSTRAINT fk_runs_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE runs ADD CONSTRAINT fk_runs_work_items FOREIGN KEY (work_item_id) REFERENCES work_items (id);
ALTER TABLE stage_checkpoints ADD CONSTRAINT fk_stage_checkpoints_runs FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE artifacts ADD CONSTRAINT fk_artifacts_runs FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE gates ADD CONSTRAINT fk_gates_runs FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE events ADD CONSTRAINT fk_events_runs FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE audit_entries ADD CONSTRAINT fk_audit_entries_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE audit_entries ADD CONSTRAINT fk_audit_entries_runs FOREIGN KEY (run_id) REFERENCES runs (id);
