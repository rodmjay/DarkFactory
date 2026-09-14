-- docs/adr/0038: a connection is watched, and heals.
--
-- Until now a server's status was whatever registration observed, forever:
-- both Moonbeam servers read "Conformant" four days after anyone had last
-- asked them anything. These columns are what the health monitor records on
-- every check, so "is it up" is an answer from the last thirty seconds
-- rather than from registration day.

ALTER TABLE servers ADD COLUMN last_checked_at timestamp with time zone;
ALTER TABLE servers ADD COLUMN last_seen_at timestamp with time zone;
-- The first missed check of the current run of misses; null while answering.
-- Set on the first miss, not when the server is declared unreachable, so the
-- outage is dated from when it began rather than from when it was believed.
ALTER TABLE servers ADD COLUMN unreachable_since timestamp with time zone;
ALTER TABLE servers ADD COLUMN consecutive_failures integer NOT NULL DEFAULT 0;
ALTER TABLE servers ADD COLUMN last_error text;
ALTER TABLE servers ADD COLUMN next_check_at timestamp with time zone;
ALTER TABLE servers ADD COLUMN healed_at timestamp with time zone;
ALTER TABLE servers ADD COLUMN heal_count integer NOT NULL DEFAULT 0;

-- The monitor's poll: which live servers are due.
CREATE INDEX ix_servers_next_check_at ON servers (next_check_at) WHERE removed_at IS NULL;

-- docs/adr/0038: an intake can be pulled from a corpus server rather than
-- submitted inline. Where each source came from, and the hash it had then,
-- is what drift is later measured against.
ALTER TABLE intakes ADD COLUMN source_server_id text;
ALTER TABLE intakes ADD CONSTRAINT fk_intakes_servers
    FOREIGN KEY (source_server_id) REFERENCES servers (id);

ALTER TABLE intake_sources ADD COLUMN origin_id text;
ALTER TABLE intake_sources ADD COLUMN origin_sha256 character varying(64);
ALTER TABLE intake_sources ADD COLUMN origin_updated text;
