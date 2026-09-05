-- docs/adr/0016 (as amended): an amendment records who, when, and *why*,
-- per change. The "why" was being lost, in two different ways, and the two
-- need different recoveries.
--
-- Creates and revises: not lost, buried. SpecDiffTranslator serialized the
-- rationale into each element's ContentJson as {"Text":…,"Rationale":…}, so
-- it did reach diff_json — just nowhere anything looked. Recoverable from
-- the amendment itself, with no dependence on any other row.
--
-- Retires and both edge kinds: genuinely lost. Those internal records had
-- no rationale field and no ContentJson, so the value was dropped when the
-- wire document was translated, before the amendment was written.
-- Recoverable only from what the model actually said, which survives in
-- turns.payloads_json.
--
-- Both passes are idempotent and only ever fill a null: a rationale already
-- present is never overwritten, because the stored value is the one that
-- was approved and this migration is archaeology, not authority.

-- ---------------------------------------------------------------------------
-- Pass 1 — lift the buried rationale out of ContentJson
-- ---------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION df_lift_rationale_from_content(elements jsonb)
RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
    SELECT COALESCE(jsonb_agg(
        CASE
            WHEN element ? 'Rationale' AND element->'Rationale' <> 'null'::jsonb THEN element
            WHEN element->>'ContentJson' IS NULL THEN element
            ELSE element || jsonb_build_object(
                'Rationale', COALESCE(((element->>'ContentJson')::jsonb)->'Rationale', 'null'::jsonb))
        END
        ORDER BY ordinality), '[]'::jsonb)
    FROM jsonb_array_elements(COALESCE(elements, '[]'::jsonb)) WITH ORDINALITY AS t(element, ordinality);
$$;

-- ---------------------------------------------------------------------------
-- Pass 2 — recover the dropped rationale from what the model said
-- ---------------------------------------------------------------------------

-- Merges rationale positionally from the wire payload's array into the
-- internal one. Positional because the internal element has no id to match
-- on — a retire has only a spec_id and an edge_add has none at all — and
-- the two arrays were built from each other in order, one element at a
-- time, so index is the correspondence and not a guess.
CREATE OR REPLACE FUNCTION df_merge_rationale_by_position(internal jsonb, wire jsonb)
RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
    SELECT COALESCE(jsonb_agg(
        CASE
            WHEN element ? 'Rationale' AND element->'Rationale' <> 'null'::jsonb THEN element
            ELSE element || jsonb_build_object(
                'Rationale', COALESCE(wire->(ordinality::int - 1)->'rationale', 'null'::jsonb))
        END
        ORDER BY ordinality), '[]'::jsonb)
    FROM jsonb_array_elements(COALESCE(internal, '[]'::jsonb)) WITH ORDINALITY AS t(element, ordinality);
$$;

-- The spec_diff payload for an amendment, out of whichever turn carried it.
--
-- Matched on the amendment id inside the payload rather than on
-- amendments.turn_id: that column holds the *user* turn that prompted the
-- proposal, while the payload is on the *assistant* turn that answered it.
-- Both casings are accepted because payloads written before the wire-shape
-- fix used the serializer's default naming.
CREATE OR REPLACE FUNCTION df_wire_diff_for_amendment(amendment_id text)
RETURNS jsonb LANGUAGE sql STABLE AS $$
    SELECT COALESCE(payload->'diff', payload->'Diff')
    FROM turns t,
         LATERAL jsonb_array_elements(
             CASE WHEN t.payloads_json IS NULL THEN '[]'::jsonb ELSE t.payloads_json::jsonb END) AS payload
    WHERE COALESCE(payload->>'amendment_id', payload->>'AmendmentId') = amendment_id
      AND COALESCE(payload->'diff', payload->'Diff') IS NOT NULL
    LIMIT 1;
$$;

-- ---------------------------------------------------------------------------
-- Apply
-- ---------------------------------------------------------------------------

-- A CTE rather than a LATERAL in the UPDATE's FROM: the row being updated
-- is not in scope for a lateral subquery there, so referencing it is an
-- error rather than a self-join.
--
-- The LIKE guard is not decoration. diff_json is text, so an unparseable
-- value would fail the cast and take the whole migration — and therefore
-- every later one — down with it, over a row this backfill has nothing to
-- say about anyway.
WITH source AS (
    SELECT a.id,
           a.diff_json::jsonb AS diff,
           df_wire_diff_for_amendment(a.id) AS wire
    FROM amendments a
    WHERE a.diff_json LIKE '{%'
)
UPDATE amendments a
SET diff_json = jsonb_build_object(
        'Creates',     df_merge_rationale_by_position(
                           df_lift_rationale_from_content(s.diff->'Creates'), s.wire->'creates'),
        'Revises',     df_merge_rationale_by_position(
                           df_lift_rationale_from_content(s.diff->'Revises'), s.wire->'revises'),
        'Retires',     df_merge_rationale_by_position(s.diff->'Retires',     s.wire->'retires'),
        'EdgeAdds',    df_merge_rationale_by_position(s.diff->'EdgeAdds',    s.wire->'edge_adds'),
        'EdgeRetires', df_merge_rationale_by_position(s.diff->'EdgeRetires', s.wire->'edge_retires')
    )::text
FROM source s
WHERE s.id = a.id AND jsonb_typeof(s.diff) = 'object';

-- The functions exist for this migration and are not part of the schema the
-- application queries. Leaving them behind would be leaving three
-- undocumented functions in the public schema for a one-off.
DROP FUNCTION df_lift_rationale_from_content(jsonb);
DROP FUNCTION df_merge_rationale_by_position(jsonb, jsonb);
DROP FUNCTION df_wire_diff_for_amendment(text);
