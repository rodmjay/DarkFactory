-- Corpus intake (docs/adr/0037): importing specifications that already
-- exist as prose, by extracting a draft per source document, asking about
-- its holes, and proposing it as an ordinary amendment once they are
-- filled.
--
-- None of these tables is the spec graph. A draft is deliberately mutable
-- — it is overwritten by each extraction until it is proposed — and the
-- graph's append-only guarantees (0002) begin at the amendment, exactly
-- where they begin for a conversation. Grants come from the default
-- privileges set in 0002.

CREATE TABLE intakes (
    id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    name text NOT NULL,
    conversation_id text NOT NULL,
    -- The corpus as submitted, stored once as an artifact: after intake the
    -- factory is authoritative, and the originals may change or vanish.
    corpus_ref text NOT NULL,
    corpus_sha256 character varying(64) NOT NULL,
    created_by text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_intakes PRIMARY KEY (id)
);

CREATE TABLE intake_sources (
    id text NOT NULL,
    intake_id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    seq integer NOT NULL,
    source_ref text NOT NULL,
    title text NOT NULL,
    content text NOT NULL,
    content_sha256 character varying(64) NOT NULL,
    status character varying(16) NOT NULL,
    draft_json text,
    draft_revision integer NOT NULL,
    context_pack_ref text,
    failure text,
    amendment_id text,
    created_at timestamp with time zone NOT NULL,
    extracted_at timestamp with time zone,
    CONSTRAINT pk_intake_sources PRIMARY KEY (id)
);

CREATE TABLE intake_questions (
    id text NOT NULL,
    intake_id text NOT NULL,
    source_id text NOT NULL,
    project_id character varying(64) NOT NULL,
    org_id text NOT NULL,
    kind character varying(32) NOT NULL,
    question text NOT NULL,
    quote text,
    affects_json text NOT NULL,
    status character varying(16) NOT NULL,
    answer text,
    answered_by text,
    answered_at timestamp with time zone,
    raised_in_revision integer NOT NULL,
    -- Null on an answered question means the draft does not reflect the
    -- answer yet, which is the state that blocks a proposal.
    incorporated_in_revision integer,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_intake_questions PRIMARY KEY (id)
);

ALTER TABLE intakes ADD CONSTRAINT fk_intakes_projects
    FOREIGN KEY (project_id) REFERENCES projects (id);
ALTER TABLE intakes ADD CONSTRAINT fk_intakes_conversations
    FOREIGN KEY (conversation_id) REFERENCES conversations (id);
ALTER TABLE intake_sources ADD CONSTRAINT fk_intake_sources_intakes
    FOREIGN KEY (intake_id) REFERENCES intakes (id);
ALTER TABLE intake_sources ADD CONSTRAINT fk_intake_sources_amendments
    FOREIGN KEY (amendment_id) REFERENCES amendments (id);
ALTER TABLE intake_questions ADD CONSTRAINT fk_intake_questions_intakes
    FOREIGN KEY (intake_id) REFERENCES intakes (id);
ALTER TABLE intake_questions ADD CONSTRAINT fk_intake_questions_intake_sources
    FOREIGN KEY (source_id) REFERENCES intake_sources (id);

CREATE INDEX ix_intakes_project_id ON intakes (project_id);

-- A corpus names each document once; two sources with one ref would be two
-- drafts of the same text with nothing to say which is meant.
CREATE UNIQUE INDEX ix_intake_sources_intake_id_source_ref ON intake_sources (intake_id, source_ref);
CREATE UNIQUE INDEX ix_intake_sources_intake_id_seq ON intake_sources (intake_id, seq);

CREATE INDEX ix_intake_questions_intake_id_status ON intake_questions (intake_id, status);
CREATE INDEX ix_intake_questions_source_id ON intake_questions (source_id);
