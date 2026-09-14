-- docs/adr/0023, as amended by docs/adr/0036: the factory ingests a
-- standards server's documents through df.standards.list and get, and
-- retrieves from its own copy. The table has existed since 0002 and has
-- never held a row; these are the columns ingest actually needs.

ALTER TABLE standards_index ADD COLUMN title text;
-- The server's own `updated` for the document: what a later ingest compares.
ALTER TABLE standards_index ADD COLUMN updated text;
ALTER TABLE standards_index ADD COLUMN ingested_at timestamp with time zone;

-- One row per standard per server. chunk_ref is the standard's id; a whole
-- document is one chunk until a corpus outgrows that (ADR-0023).
CREATE UNIQUE INDEX ix_standards_index_server_id_chunk_ref ON standards_index (server_id, chunk_ref);
