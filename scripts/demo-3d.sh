#!/usr/bin/env bash
#
# The step 3d acceptance demo, end to end, writing its evidence to
# docs/evidence/3d/.
#
#   register workspace-demo -> converse -> propose -> approve -> run -> ship
#
# Every stage is a live model call, so this costs real tokens and needs
# ANTHROPIC_API_KEY set in .env. It brings the stack up from an empty
# volume each time: the demo asserts on what a run produced, and a run
# starting from someone else's leftover state proves nothing.
#
# The pass condition is the one from the architecture brief:
#   - the PR body carries every spec id the run implemented, and the
#     snapshot id it was built against
#   - no spec id from outside the snapshot appears in the diff
#
# Usage:  scripts/demo-3d.sh
# Exit:   0 if both checks pass, 1 otherwise.

set -euo pipefail

cd "$(dirname "$0")/.."

EVIDENCE=docs/evidence/3d
# Matches docker-compose.yml's configurable host publish.
FACTORY_PORT=${FACTORY_PORT:-$(grep -E '^FACTORY_PORT=' .env 2>/dev/null | cut -d= -f2)}
FACTORY_PORT=${FACTORY_PORT:-5100}
FACTORY=http://localhost:${FACTORY_PORT}/mcp
WORKSPACE=http://workspace-demo:8931/mcp

# The whole solution's tests need a Docker socket the workspace container
# does not have (Testcontainers), so the demo team verifies with a suite
# that genuinely runs in there. A red suite still parks the run — this
# narrows what is tested, not whether failure is caught.
TEST_COMMAND='dotnet test tests/DarkFactory.Mcp.Tests --nologo'

say() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
die() { printf '\033[31m%s\033[0m\n' "$1" >&2; exit 1; }

call() {
  curl -s --max-time 600 -X POST "$FACTORY" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"$1\",\"arguments\":$2}}" \
  | sed -n 's/^data: //p'
}

# Unwraps one MCP tool result, failing loudly on a tool error rather than
# letting a null flow onward and fail somewhere less informative.
unwrap() {
  python3 -c "
import sys, json
d = json.load(sys.stdin)['result']
text = d['content'][0]['text']
if d.get('isError'):
    sys.stderr.write('tool error: ' + text + '\n'); sys.exit(1)
print(text)"
}

psql_() { docker compose exec -T postgres psql -U darkfactory -d darkfactory "$@"; }

# ---------------------------------------------------------------------------

[ -f .env ] || die ".env not found. Copy .env.example and set ANTHROPIC_API_KEY."
grep -qE '^ANTHROPIC_API_KEY=.+' .env || die "ANTHROPIC_API_KEY is empty in .env. Every stage here is a live model call."

mkdir -p "$EVIDENCE"

# The services this demo actually uses. The dashboard is deliberately not
# among them: 3d never opens it, building it doubles the setup time, and
# coupling an acceptance check to an unrelated UI build means a broken
# front end fails a run that had nothing to do with it.
SERVICES="postgres migrate factory worker workspace-demo"

say "Bringing the stack up from an empty volume"
docker compose down -v >/dev/null 2>&1 || true
docker compose up -d --build $SERVICES >/dev/null
for _ in $(seq 1 60); do
  healthy=$(docker compose ps --format '{{.Service}} {{.Status}}' | grep -c healthy || true)
  [ "$healthy" -ge 4 ] && break
  sleep 5
done
docker compose ps --format 'table {{.Service}}\t{{.Status}}'

say "Registering the project against workspace-demo"
PROJECT=$(call df.projects.register "{\"workspace_mcp_url\":\"$WORKSPACE\"}" | unwrap \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "project: $PROJECT"

# The command the team verifies with. Set explicitly rather than left to
# the stack-hint default, for the reason at the top of this script.
psql_ -qc "UPDATE teams SET test_command='$TEST_COMMAND' WHERE project_id='$PROJECT';"

say "Conversing (live model)"
CONVERSATION=$(call df.conversations.start "{\"project_id\":\"$PROJECT\",\"title\":\"health endpoint\"}" | unwrap \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")

TURN=$(call df.conversations.turn "$(python3 -c "
import json
print(json.dumps({
  'conversation_id': '$CONVERSATION',
  'message': ('Add a rule: the factory must expose a GET /health/detailed endpoint that '
              'reports database connectivity. Keep it to one small spec node. If that is '
              'clear enough, propose the amendment now.'),
}))")" | unwrap)

echo "$TURN" > "$EVIDENCE/conversation-turn.json"
AMENDMENT=$(echo "$TURN" | python3 -c "import sys,json; print(json.load(sys.stdin)['amendmentId'] or '')")
[ -n "$AMENDMENT" ] || die "The architect did not propose an amendment. See $EVIDENCE/conversation-turn.json."
echo "amendment: $AMENDMENT"

say "Approving, and seeding the run"
call df.specs.approve "{\"amendment_id\":\"$AMENDMENT\"}" | unwrap > "$EVIDENCE/amendment.json"
RUN=$(call df.work.create "{\"project_id\":\"$PROJECT\",\"amendment_ids\":[\"$AMENDMENT\"]}" | unwrap \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "run: $RUN"

say "Waiting for the run (every stage is a live model call)"
for i in $(seq 1 90); do
  state=$(psql_ -Atc "SELECT current_stage||' '||status FROM runs WHERE id='$RUN';")
  printf '  [%02d] %s\n' "$i" "$state"
  case "$state" in *Completed|*Failed|*Cancelled) break;; esac
  sleep 20
done

STATUS=$(psql_ -Atc "SELECT status FROM runs WHERE id='$RUN';")
SNAPSHOT=$(psql_ -Atc "SELECT snapshot_id FROM runs WHERE id='$RUN';")

# ---------------------------------------------------------------------------
# Evidence
# ---------------------------------------------------------------------------

say "Writing evidence to $EVIDENCE"

psql_ -Atc "SELECT content_json FROM artifacts WHERE run_id='$RUN' AND type='PrBody';" \
  > "$EVIDENCE/pr-body.md"

psql_ -Atc "SELECT content_json FROM artifacts WHERE run_id='$RUN' AND type='TestReport';" \
  > "$EVIDENCE/test-report.json"

psql_ -Atc "SELECT content_json FROM artifacts WHERE run_id='$RUN' AND type='Patch';" \
  > "$EVIDENCE/changeset.patch"

psql_ -c "SELECT stage_id, attempt, deployment, provider, model_family,
                 input_tokens_uncached, input_tokens_cached, cache_write_tokens,
                 output_tokens, thinking_tokens, latency_ms,
                 artifact_valid_first_try, retried, stage_result
          FROM model_calls WHERE run_id='$RUN' OR run_id IS NULL
          ORDER BY created_at;" > "$EVIDENCE/model-calls.txt"

psql_ -Atc "SELECT stage||' '||status||' attempt='||attempt FROM stage_checkpoints
            WHERE run_id='$RUN' ORDER BY created_at;" > "$EVIDENCE/stages.txt"

# ---------------------------------------------------------------------------
# The pass condition
# ---------------------------------------------------------------------------

say "Acceptance checks"

SNAPSHOT_SPECS=$(psql_ -Atc "SELECT spec_id FROM snapshot_members WHERE snapshot_id='$SNAPSHOT';")

python3 - "$EVIDENCE" "$RUN" "$SNAPSHOT" "$STATUS" <<PY | tee "$EVIDENCE/acceptance.txt"
import re, sys, pathlib

evidence, run, snapshot, status = sys.argv[1:5]
snapshot_specs = set("""$SNAPSHOT_SPECS""".split())

patch = pathlib.Path(evidence, "changeset.patch").read_text()
body = pathlib.Path(evidence, "pr-body.md").read_text()
in_patch = set(re.findall(r"[0-9A-HJKMNP-TV-Z]{26}", patch))

checks = []
checks.append(("run reached ship and completed", status == "Completed", status))
checks.append((
    "PR body carries every spec id the run implemented",
    all(s in body for s in snapshot_specs) and bool(snapshot_specs),
    ", ".join(sorted(snapshot_specs)) or "(none)"))
checks.append(("PR body carries the snapshot id", snapshot in body, snapshot))
checks.append((
    "every spec id in the snapshot appears in the diff",
    not (snapshot_specs - in_patch),
    ", ".join(sorted(snapshot_specs - in_patch)) or "none missing"))
checks.append((
    "no spec id outside the snapshot appears in the diff",
    not (in_patch - snapshot_specs),
    ", ".join(sorted(in_patch - snapshot_specs)) or "none foreign"))

print(f"run       {run}")
print(f"snapshot  {snapshot}")
print()
for label, ok, detail in checks:
    print(f"{'PASS' if ok else 'FAIL'}  {label}\n        {detail}")

sys.exit(0 if all(ok for _, ok, _ in checks) else 1)
PY
