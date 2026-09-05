-- The server registry (docs/adr/0018, docs/conventions/describe.md).
-- 0002 created `servers` as a shape-only placeholder; this migration makes
-- it the real registry: a URL to be idempotent on, a stored manifest-vs-live
-- diff, and per-capability conformance results.
--
-- `servers` is empty at this point in the project's life (nothing has ever
-- written to it — registration is what this migration enables), so the new
-- NOT NULL columns are added without a backfill. If that were not true,
-- url would have to arrive nullable and be populated first.

-- ---------------------------------------------------------------------------
-- servers
-- ---------------------------------------------------------------------------

ALTER TABLE servers ADD COLUMN url text NOT NULL;
ALTER TABLE servers ADD COLUMN registered_at timestamp with time zone NOT NULL;

-- The disagreement between the static manifest and the live describe, as
-- observed at registration. Stored, not recomputed: what the dashboard
-- shows has to be what registration actually saw. NULL means they agreed.
ALTER TABLE servers ADD COLUMN manifest_diff_json text;

-- Removal is a retirement. A server's conformance history is an audit
-- record — that this server once failed df.exec.run stays true after
-- someone removes it — so a hard delete would either erase that history or
-- orphan it, and cascading it away is exactly what must not happen.
-- Removed servers are excluded from df.servers.list; re-registering the
-- same (org_id, url) revives the row.
ALTER TABLE servers ADD COLUMN removed_at timestamp with time zone;

-- Registration is idempotent by (org_id, url). Making that a unique index
-- rather than a check in the service means two concurrent registrations of
-- the same URL conflict at the database instead of silently producing two
-- rows that then disagree about the server's status.
CREATE UNIQUE INDEX ix_servers_org_id_url ON servers (org_id, url);

-- ---------------------------------------------------------------------------
-- conformance_results
-- ---------------------------------------------------------------------------

-- One row per declared capability per pass, never a single boolean on the
-- server: "conformance failed" is not actionable, "df.exec.run failed and
-- the rest passed" is. conformance_run_id groups one pass so history stays
-- readable across re-registrations.
CREATE TABLE conformance_results (
    id text NOT NULL,
    server_id text NOT NULL,
    conformance_run_id text NOT NULL,
    capability text NOT NULL,
    status character varying(16) NOT NULL,
    detail text,
    duration_ms bigint NOT NULL,
    checked_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_conformance_results PRIMARY KEY (id)
);

ALTER TABLE conformance_results ADD CONSTRAINT fk_conformance_results_servers
    FOREIGN KEY (server_id) REFERENCES servers (id);

CREATE INDEX ix_conformance_results_server_id_conformance_run_id
    ON conformance_results (server_id, conformance_run_id);

-- ---------------------------------------------------------------------------
-- Grants
-- ---------------------------------------------------------------------------

-- ALTER DEFAULT PRIVILEGES in 0002 already grants the application role
-- SELECT/INSERT/UPDATE/DELETE on tables created afterwards, so
-- conformance_results needs no GRANT here. It does need the revocation: a
-- conformance result is a record of what was observed at a point in time,
-- and rewriting one would falsify the audit trail the dashboard renders.
-- (0002's defaults deliberately do not carry revocations forward — see the
-- note there and in README.md.)
REVOKE UPDATE, DELETE ON conformance_results FROM darkfactory_app;
