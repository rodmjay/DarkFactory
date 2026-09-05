-- The spec graph (docs/adr/0016), the conversations it grows from
-- (docs/adr/0017), the server registry (docs/adr/0018, docs/adr/0019), and
-- the standards index shell (docs/adr/0023). Same provenance as
-- 0001_initial.sql: generated from DarkFactoryDbContext's model via
-- Database.GenerateCreateScript(), then hand-finished. This file, not the C#
-- model, is the source of truth.
--
-- Not created here (deliberately): `teams`, `team_revisions`,
-- `team_members`, `skills`, `skill_revisions`, `assignments` and
-- `runs.team_snapshot_id` are step 3c work (docs/adr/0028); `batches` and
-- `batch_items` land with the execution path (docs/adr/0029). The
-- `agent_policies` table sketched in the architecture brief is never
-- created — docs/adr/0028 retired it before it was built.
--
-- standards_index has no `embedding` column: pgvector is not provisioned
-- and embeddings are out of scope for this slice (docs/adr/0023). The table
-- exists so the shape is settled; the column is added when it is populated.

-- ---------------------------------------------------------------------------
-- Registry and standards (docs/adr/0018, docs/adr/0019, docs/adr/0023)
-- ---------------------------------------------------------------------------

CREATE TABLE servers (
    id text NOT NULL,
    org_id text NOT NULL,
    project_id text,
    name text NOT NULL,
    tier character varying(16) NOT NULL,
    domain text NOT NULL,
    convention_version text NOT NULL,
    manifest_json text NOT NULL,
    live_describe_json text,
    status character varying(16) NOT NULL,
    last_conformance_at timestamp with time zone,
    CONSTRAINT pk_servers PRIMARY KEY (id)
);

CREATE TABLE standards_index (
    id text NOT NULL,
    server_id text NOT NULL,
    project_id text,
    chunk_ref text NOT NULL,
    layer text NOT NULL,
    text text NOT NULL,
    source_ref text NOT NULL,
    CONSTRAINT pk_standards_index PRIMARY KEY (id)
);

-- ---------------------------------------------------------------------------
-- Conversations (docs/adr/0017)
-- ---------------------------------------------------------------------------

CREATE TABLE conversations (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    title text,
    created_by text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    status character varying(32) NOT NULL,
    CONSTRAINT pk_conversations PRIMARY KEY (id)
);

CREATE TABLE turns (
    id text NOT NULL,
    conversation_id text NOT NULL,
    seq integer NOT NULL,
    role character varying(16) NOT NULL,
    content text NOT NULL,
    payloads_json text,
    retrieval_ref text,
    token_usage integer,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_turns PRIMARY KEY (id)
);

-- ---------------------------------------------------------------------------
-- Provenance (docs/adr/0016: required on every revision, edge and snapshot)
-- ---------------------------------------------------------------------------

CREATE TABLE provenance (
    id text NOT NULL,
    conversation_id text,
    turn_id text,
    actor_type character varying(16) NOT NULL,
    actor_id text NOT NULL,
    approved_by text,
    at timestamp with time zone NOT NULL,
    CONSTRAINT pk_provenance PRIMARY KEY (id)
);

-- ---------------------------------------------------------------------------
-- The graph itself (docs/adr/0016)
-- ---------------------------------------------------------------------------

-- Stable identity only. The one and only mutation this table permits is the
-- one-way retired_at transition; content lives in spec_revisions.
CREATE TABLE spec_nodes (
    spec_id character varying(26) NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    kind text NOT NULL,
    layer text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    retired_at timestamp with time zone,
    CONSTRAINT pk_spec_nodes PRIMARY KEY (spec_id)
);

-- Content-addressed and immutable. The primary key is (spec_id, hash)
-- rather than hash alone: identical text under two different node
-- identities is two revisions, not one shared row, because a revision
-- belongs to a node's history. Immutability is enforced by the database,
-- not by convention — see the grants at the bottom of this file.
CREATE TABLE spec_revisions (
    spec_id character varying(26) NOT NULL,
    hash character varying(64) NOT NULL,
    content_json text NOT NULL,
    canonical_text text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    provenance_id text NOT NULL,
    CONSTRAINT pk_spec_revisions PRIMARY KEY (spec_id, hash)
);

-- Edges point at node identities, not revisions, so an edge survives its
-- endpoints being revised.
CREATE TABLE spec_edges (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    from_spec_id character varying(26) NOT NULL,
    to_spec_id character varying(26) NOT NULL,
    kind text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    provenance_id text NOT NULL,
    retired_at timestamp with time zone,
    CONSTRAINT pk_spec_edges PRIMARY KEY (id)
);

CREATE TABLE spec_snapshots (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    name text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    provenance_id text NOT NULL,
    CONSTRAINT pk_spec_snapshots PRIMARY KEY (id)
);

-- A snapshot is exactly this: a set of (spec_id, revision_hash) pairs. The
-- primary key is what makes "one pinned revision per node per snapshot"
-- unrepresentable-if-violated rather than merely checked.
CREATE TABLE snapshot_members (
    snapshot_id text NOT NULL,
    spec_id text NOT NULL,
    revision_hash text NOT NULL,
    CONSTRAINT pk_snapshot_members PRIMARY KEY (snapshot_id, spec_id)
);

-- ...and the topology as of the same instant. Pinning node revisions alone
-- would leave "which edges existed then" answerable only by comparing
-- timestamps against the snapshot's created_at, which quietly makes a
-- snapshot's meaning depend on clock resolution. Recorded membership makes
-- a snapshot self-describing and diff a pure set comparison.
CREATE TABLE snapshot_edges (
    snapshot_id text NOT NULL,
    edge_id text NOT NULL,
    CONSTRAINT pk_snapshot_edges PRIMARY KEY (snapshot_id, edge_id)
);

-- ---------------------------------------------------------------------------
-- Amendments and approvals (docs/adr/0017)
-- ---------------------------------------------------------------------------

CREATE TABLE amendments (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    conversation_id text NOT NULL,
    turn_id text,
    proposed_by text NOT NULL,
    status character varying(16) NOT NULL,
    diff_json text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_amendments PRIMARY KEY (id)
);

-- (target_type, target_id) is polymorphic across amendments and gates, so
-- it deliberately carries no foreign key.
CREATE TABLE approvals (
    id text NOT NULL,
    target_type character varying(16) NOT NULL,
    target_id text NOT NULL,
    decision character varying(16) NOT NULL,
    approved_by text NOT NULL,
    reason text,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_approvals PRIMARY KEY (id)
);

-- ---------------------------------------------------------------------------
-- Runs record what they were built against (docs/adr/0004, as amended)
-- ---------------------------------------------------------------------------

-- Both nullable: nothing seeds a run from a snapshot until step 3c's
-- df.work.create exists, and 0001's rows predate the columns entirely.
ALTER TABLE runs ADD COLUMN snapshot_id text;
ALTER TABLE runs ADD COLUMN amendment_ids text[];

-- ---------------------------------------------------------------------------
-- Referential integrity
-- ---------------------------------------------------------------------------

-- No ON DELETE CASCADE anywhere below. In an append-only graph a cascading
-- delete is not a convenience, it is a silent history loss: deleting one
-- provenance row would take every revision it justifies with it. Rows are
-- retired, never deleted, and the database should refuse anything else.

ALTER TABLE standards_index ADD CONSTRAINT fk_standards_index_servers FOREIGN KEY (server_id) REFERENCES servers (id);

ALTER TABLE conversations ADD CONSTRAINT fk_conversations_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE turns ADD CONSTRAINT fk_turns_conversations FOREIGN KEY (conversation_id) REFERENCES conversations (id);

ALTER TABLE provenance ADD CONSTRAINT fk_provenance_conversations FOREIGN KEY (conversation_id) REFERENCES conversations (id);
ALTER TABLE provenance ADD CONSTRAINT fk_provenance_turns FOREIGN KEY (turn_id) REFERENCES turns (id);

ALTER TABLE spec_nodes ADD CONSTRAINT fk_spec_nodes_projects FOREIGN KEY (project_id) REFERENCES projects (id);

ALTER TABLE spec_revisions ADD CONSTRAINT fk_spec_revisions_spec_nodes FOREIGN KEY (spec_id) REFERENCES spec_nodes (spec_id);
ALTER TABLE spec_revisions ADD CONSTRAINT fk_spec_revisions_provenance FOREIGN KEY (provenance_id) REFERENCES provenance (id);

ALTER TABLE spec_edges ADD CONSTRAINT fk_spec_edges_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE spec_edges ADD CONSTRAINT fk_spec_edges_spec_nodes_from FOREIGN KEY (from_spec_id) REFERENCES spec_nodes (spec_id);
ALTER TABLE spec_edges ADD CONSTRAINT fk_spec_edges_spec_nodes_to FOREIGN KEY (to_spec_id) REFERENCES spec_nodes (spec_id);
ALTER TABLE spec_edges ADD CONSTRAINT fk_spec_edges_provenance FOREIGN KEY (provenance_id) REFERENCES provenance (id);

ALTER TABLE spec_snapshots ADD CONSTRAINT fk_spec_snapshots_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE spec_snapshots ADD CONSTRAINT fk_spec_snapshots_provenance FOREIGN KEY (provenance_id) REFERENCES provenance (id);

ALTER TABLE snapshot_members ADD CONSTRAINT fk_snapshot_members_spec_snapshots FOREIGN KEY (snapshot_id) REFERENCES spec_snapshots (id);

ALTER TABLE snapshot_edges ADD CONSTRAINT fk_snapshot_edges_spec_snapshots FOREIGN KEY (snapshot_id) REFERENCES spec_snapshots (id);
ALTER TABLE snapshot_edges ADD CONSTRAINT fk_snapshot_edges_spec_edges FOREIGN KEY (edge_id) REFERENCES spec_edges (id);

ALTER TABLE amendments ADD CONSTRAINT fk_amendments_projects FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE amendments ADD CONSTRAINT fk_amendments_conversations FOREIGN KEY (conversation_id) REFERENCES conversations (id);
ALTER TABLE amendments ADD CONSTRAINT fk_amendments_turns FOREIGN KEY (turn_id) REFERENCES turns (id);

ALTER TABLE runs ADD CONSTRAINT fk_runs_spec_snapshots FOREIGN KEY (snapshot_id) REFERENCES spec_snapshots (id);

-- ---------------------------------------------------------------------------
-- Indexes
-- ---------------------------------------------------------------------------

CREATE INDEX ix_servers_org_id ON servers (org_id);
CREATE INDEX ix_standards_index_server_id_layer ON standards_index (server_id, layer);

CREATE INDEX ix_conversations_project_id ON conversations (project_id);
CREATE UNIQUE INDEX ix_turns_conversation_id_seq ON turns (conversation_id, seq);

CREATE INDEX ix_provenance_conversation_id ON provenance (conversation_id);
CREATE INDEX ix_provenance_turn_id ON provenance (turn_id);

CREATE INDEX ix_spec_nodes_project_id_layer ON spec_nodes (project_id, layer);

CREATE INDEX ix_spec_revisions_provenance_id ON spec_revisions (provenance_id);

CREATE INDEX ix_spec_edges_project_id_from_spec_id ON spec_edges (project_id, from_spec_id);
CREATE INDEX ix_spec_edges_project_id_to_spec_id ON spec_edges (project_id, to_spec_id);
CREATE INDEX ix_spec_edges_from_spec_id ON spec_edges (from_spec_id);
CREATE INDEX ix_spec_edges_to_spec_id ON spec_edges (to_spec_id);
CREATE INDEX ix_spec_edges_provenance_id ON spec_edges (provenance_id);

CREATE INDEX ix_snapshot_edges_edge_id ON snapshot_edges (edge_id);

CREATE INDEX ix_spec_snapshots_project_id_name ON spec_snapshots (project_id, name);
CREATE INDEX ix_spec_snapshots_provenance_id ON spec_snapshots (provenance_id);

CREATE INDEX ix_amendments_project_id_status ON amendments (project_id, status);
CREATE INDEX ix_amendments_conversation_id ON amendments (conversation_id);
CREATE INDEX ix_amendments_turn_id ON amendments (turn_id);

CREATE INDEX ix_approvals_target_type_target_id ON approvals (target_type, target_id);

CREATE INDEX ix_runs_snapshot_id ON runs (snapshot_id);

-- Drift repair: 0001 declared these two foreign keys but not the indexes
-- backing them, because the C# model at the time had no HasOne/WithMany for
-- them either (the relationships were added while fixing SaveChanges insert
-- ordering in step 2). Unindexed FKs make the referenced row's own
-- deletes and updates scan the child table.
CREATE INDEX ix_runs_work_item_id ON runs (work_item_id);
CREATE INDEX ix_audit_entries_run_id ON audit_entries (run_id);

-- ---------------------------------------------------------------------------
-- The application role (docs/adr/0016: append-only, enforced)
-- ---------------------------------------------------------------------------

-- "The application role cannot UPDATE revision rows" is only a real
-- guarantee if the application is not the table owner: a Postgres table
-- owner bypasses its own GRANT/REVOKE entirely, so revoking UPDATE from the
-- migrating role would be theatre. Hence a genuinely separate,
-- non-superuser, non-owning role that `factory` and `worker` connect as,
-- while `migrate` keeps connecting as the owner.
--
-- Created NOLOGIN here on purpose. A password does not belong in a
-- versioned migration file, so DarkFactory.Migrate grants LOGIN and sets
-- the password afterwards from configuration (AppRole.cs).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'darkfactory_app') THEN
        CREATE ROLE darkfactory_app NOLOGIN;
    END IF;
END
$$;

DO $$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO darkfactory_app', current_database());
END
$$;

GRANT USAGE ON SCHEMA public TO darkfactory_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO darkfactory_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO darkfactory_app;

-- Tables created by later migrations are covered without having to
-- remember to re-grant. The revocations below are NOT covered by this, so a
-- future append-only table must revoke explicitly.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO darkfactory_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO darkfactory_app;

-- Immutable after insert. Not "the code never updates these" — the code
-- *cannot*.
REVOKE UPDATE, DELETE ON spec_revisions FROM darkfactory_app;
REVOKE UPDATE, DELETE ON provenance FROM darkfactory_app;
REVOKE UPDATE, DELETE ON snapshot_members FROM darkfactory_app;
REVOKE UPDATE, DELETE ON snapshot_edges FROM darkfactory_app;
REVOKE UPDATE, DELETE ON spec_snapshots FROM darkfactory_app;
REVOKE UPDATE, DELETE ON approvals FROM darkfactory_app;
REVOKE UPDATE, DELETE ON audit_entries FROM darkfactory_app;

-- Append-only with one permitted mutation: the one-way retired_at
-- transition. UPDATE stays; DELETE does not. (Column-level grants could
-- pin this to retired_at exactly; that is a refinement, not a gap — every
-- writer of these tables goes through SpecGraphService.)
REVOKE DELETE ON spec_nodes FROM darkfactory_app;
REVOKE DELETE ON spec_edges FROM darkfactory_app;
REVOKE DELETE ON amendments FROM darkfactory_app;
REVOKE DELETE ON conversations FROM darkfactory_app;
REVOKE DELETE ON turns FROM darkfactory_app;
