-- docs/adr/0032: every gateway call writes a usage fact row.
--
-- This replaces `stage_usages`, added in 0005 hours before the decision.
-- That table was a running total per stage attempt — adequate for
-- enforcing a cap and useless for anything else, because by the time usage
-- is summed into a total, every dimension that would explain it has been
-- thrown away. Keeping both would mean two answers to "what did this run
-- cost", which is worse than either.

DROP TABLE IF EXISTS stage_usages;

CREATE TABLE model_calls (
    id text NOT NULL,

    -- identity
    org_id text NOT NULL,
    project_id character varying(64),
    run_id text,
    batch_id text,
    -- A pipeline stage, or a non-stage point such as 'conversation'. Not an
    -- enum: the conversation is no longer a stage (docs/adr/0003, as
    -- amended) but its calls still belong in this table.
    stage_id text,
    task_id text,
    attempt integer NOT NULL,
    team_member_id text,
    persona_id text,
    -- The role the caller asked for. model_family is what answered.
    deployment text NOT NULL,
    provider text NOT NULL,
    model_family text NOT NULL,

    -- inputs. Cached input is stored as its own column rather than as a
    -- fraction of the total, because it is billed at a different rate and a
    -- cost model built on totals alone is wrong in the flattering direction.
    input_tokens_uncached integer NOT NULL,
    input_tokens_cached integer NOT NULL,
    cache_write_tokens integer NOT NULL,
    context_pack_ref text,
    skill_revisions text[] NOT NULL DEFAULT '{}',
    prompt_template_version text,
    thinking_preset text,

    -- outputs. thinking_tokens is a portion of output_tokens, not an
    -- addition to it.
    output_tokens integer NOT NULL,
    thinking_tokens integer NOT NULL,
    latency_ms bigint NOT NULL,
    cost numeric,

    -- outcome, completed by the caller once it knows one. These are what
    -- make this a fact table rather than a meter: tokens alone rank the
    -- cheapest model best at everything, while tokens beside "did it work
    -- first time" is what makes docs/adr/0022's verifiability criterion
    -- measurable.
    artifact_valid_first_try boolean,
    retried boolean,
    steered boolean,
    stage_result text,

    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_model_calls PRIMARY KEY (id)
);

-- Nullable, because not every call has a run: a conversational turn
-- (docs/adr/0017) is a model call with no run, no stage and no batch, and
-- inventing values for it would poison every per-dimension query.
ALTER TABLE model_calls ADD CONSTRAINT fk_model_calls_runs
    FOREIGN KEY (run_id) REFERENCES runs (id);
ALTER TABLE model_calls ADD CONSTRAINT fk_model_calls_team_members
    FOREIGN KEY (team_member_id) REFERENCES team_members (id);

CREATE INDEX ix_model_calls_run_id ON model_calls (run_id);
-- The budget question: what has this member spent on this run?
CREATE INDEX ix_model_calls_run_id_team_member_id ON model_calls (run_id, team_member_id);
-- The roll-ups that feed the dashboard and billing.
CREATE INDEX ix_model_calls_org_id_created_at ON model_calls (org_id, created_at);
CREATE INDEX ix_model_calls_project_id_created_at ON model_calls (project_id, created_at);

-- ---------------------------------------------------------------------------
-- Grants
-- ---------------------------------------------------------------------------

-- Append-only, with one exception. The gateway writes the row; the caller
-- later completes the outcome columns it could not know at call time, so
-- UPDATE has to stay. DELETE does not: a spend record that can be removed
-- is not a spend record, and this table feeds billing.
--
-- (Column-level grants could pin the UPDATE to the four outcome columns
-- exactly. That is a refinement worth making when there is a second writer;
-- today every writer is RecordingModelGateway.)
REVOKE DELETE ON model_calls FROM darkfactory_app;
