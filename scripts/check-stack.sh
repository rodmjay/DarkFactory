#!/usr/bin/env bash
#
# Builds and starts every Compose service from an empty volume and waits for
# all of them to report healthy.
#
# This is the integration test that parallel work on one repository is
# otherwise missing. Each session builds and runs the part it is changing;
# nobody builds the whole stack, so a service one session retires a
# dependency from stays broken on main until someone unrelated trips over
# it — which is exactly how `docker compose up --build` came to fail while
# every test suite stayed green.
#
# It builds with --no-cache off but from an empty volume on purpose: a
# migration that only applies to a database that already has the previous
# schema is a migration that works on your machine.
#
# Usage:  scripts/check-stack.sh [--keep]
#           --keep   leave the stack running afterwards
# Exit:   0 if every service reaches healthy, 1 otherwise.

set -uo pipefail

cd "$(dirname "$0")/.."

KEEP=0
[ "${1:-}" = "--keep" ] && KEEP=1

TIMEOUT_SECONDS=${STACK_TIMEOUT_SECONDS:-300}

say()  { printf '\n\033[1m== %s\033[0m\n' "$1"; }
fail() { printf '\033[31mFAIL  %s\033[0m\n' "$1" >&2; }
pass() { printf '\033[32mPASS  %s\033[0m\n' "$1"; }

cleanup() {
    if [ "$KEEP" -eq 0 ]; then
        docker compose down -v >/dev/null 2>&1 || true
    else
        printf '\nStack left running (--keep). `docker compose down -v` when finished.\n'
    fi
}

# Every service Compose knows about, so adding one to docker-compose.yml
# automatically adds it to this check rather than requiring someone to
# remember a second list.
SERVICES=$(docker compose config --services | sort)
EXPECTED=$(echo "$SERVICES" | wc -l | tr -d ' ')

say "Services under test ($EXPECTED)"
echo "$SERVICES" | sed 's/^/  /'

say "Starting from an empty volume"
docker compose down -v >/dev/null 2>&1 || true

if ! docker compose build 2>&1 | tail -20; then
    fail "docker compose build failed — see the output above."
    cleanup
    exit 1
fi

up_log=$(mktemp)
if ! docker compose up -d > "$up_log" 2>&1; then
    tail -20 "$up_log"

    # A port collision is the machine, not the checkout, and saying "the
    # stack does not come up from a clean checkout" would send someone
    # looking for a bug that is not there. Every host publish is
    # configurable precisely because a developer machine runs several
    # stacks at once.
    if grep -qiE 'port is already allocated|address already in use|ports are not available' "$up_log"; then
        printf '\n\033[33mThis is a host port collision, not a problem with the code.\033[0m\n' >&2
        grep -oiE '(0\.0\.0\.0|127\.0\.0\.1):[0-9]+' "$up_log" | sort -u | sed 's/^/  in use: /' >&2
        printf 'Set the matching override in .env and re-run:\n' >&2
        printf '  POSTGRES_PORT  FACTORY_PORT  DASHBOARD_PORT  WORKSPACE_DEMO_PORT\n' >&2
    fi

    rm -f "$up_log"
    fail "docker compose up failed — see the output above."
    cleanup
    exit 1
fi
rm -f "$up_log"

say "Waiting for health checks (up to ${TIMEOUT_SECONDS}s)"

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
while [ "$(date +%s)" -lt "$deadline" ]; do
    # A one-shot service that has exited 0 is as good as healthy: `migrate`
    # is supposed to finish, and waiting for it to report healthy would wait
    # forever.
    settled=$(docker compose ps -a --format '{{.Service}} {{.Status}}' \
        | grep -cE 'healthy|Exited \(0\)' || true)

    if [ "$settled" -ge "$EXPECTED" ]; then
        break
    fi

    # Fail fast on a container that has given up rather than burning the
    # whole timeout waiting for something that is never coming back.
    if docker compose ps -a --format '{{.Service}} {{.Status}}' | grep -qE 'Exited \([1-9]'; then
        break
    fi

    sleep 5
done

# ---------------------------------------------------------------------------
# The path a developer takes, which the container path does not cover
# ---------------------------------------------------------------------------

# Two paths reach the same build and only one of them was checked: the image
# runs the Dockerfile's CMD, and a developer runs `pnpm start`. Putting this
# in a second script beside this one would recreate exactly the problem this
# script exists for — two gates, each covering the path its author takes.
#
# It costs about five seconds. That was worth measuring rather than assuming,
# because a check slow enough to skip is worse than a hole.
check_web() {
    command -v pnpm >/dev/null 2>&1 || { say "Web (skipped — pnpm not installed)"; return 0; }
    [ -f dashboard/package.json ] || return 0

    say "Web: the path a developer takes"

    local log port
    log=$(mktemp)
    port=${WEB_SMOKE_PORT:-14999}

    if ! pnpm --filter dashboard build > "$log" 2>&1; then
        tail -15 "$log"; rm -f "$log"
        fail "pnpm --filter dashboard build"
        return 1
    fi

    PORT="$port" pnpm --filter dashboard start > "$log" 2>&1 &
    local pid=$!

    local ok=1
    for _ in $(seq 1 30); do
        if curl -fsS -o /dev/null "http://localhost:$port/api/health" 2>/dev/null; then ok=0; break; fi
        kill -0 "$pid" 2>/dev/null || break
        sleep 1
    done

    kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null

    if [ "$ok" -ne 0 ]; then
        tail -15 "$log"; rm -f "$log"
        fail "pnpm start never served /api/health on port $port"
        return 1
    fi

    # Answering 200 is not the same as being correct, and this is the case
    # that proves it: with output:"standalone", `next start` serves the
    # ordinary build sitting beside the standalone one and returns 200 while
    # telling you the configuration is unsupported. A health check alone
    # goes green on a command Next says does not work.
    if grep -qiE 'does not work with|is not supported|deprecated and will be removed' "$log"; then
        printf '\n'; grep -iE 'does not work with|is not supported|deprecated and will be removed' "$log" | sed 's/^/        /'
        rm -f "$log"
        fail "pnpm start answered, but reported its own configuration unsupported"
        return 1
    fi

    # Answering with a clean log is still not the same as being correct.
    # The failure this fix repaired serves markup with no CSS and no JS —
    # a 200, a quiet server, and a blank white page. So follow one
    # stylesheet the page actually asks for and confirm it resolves with
    # content behind it.
    local page css asset_status asset_bytes
    page=$(curl -fsS "http://localhost:$port/" 2>/dev/null || true)
    css=$(printf '%s' "$page" | grep -oE '/_next/static/[^"]+\.css' | head -1)

    if [ -z "$css" ]; then
        rm -f "$log"
        fail "the page referenced no stylesheet — served markup with no assets"
        return 1
    fi

    asset_status=$(curl -s -o /tmp/df-asset -w '%{http_code}' "http://localhost:$port$css" 2>/dev/null || echo 000)
    asset_bytes=$(wc -c < /tmp/df-asset 2>/dev/null || echo 0)
    rm -f /tmp/df-asset

    if [ "$asset_status" != "200" ] || [ "$asset_bytes" -lt 100 ]; then
        rm -f "$log"
        fail "$css returned $asset_status, $asset_bytes bytes — the server answers but serves nothing"
        return 1
    fi

    rm -f "$log"
    pass "pnpm build + start — /api/health, no configuration warning, assets resolve ($asset_bytes bytes)"
    return 0
}

web_failed=0
check_web || web_failed=1

say "Result"
docker compose ps -a --format 'table {{.Service}}\t{{.Status}}'
echo

failed=$web_failed
for service in $SERVICES; do
    status=$(docker compose ps -a --format '{{.Service}} {{.Status}}' | grep "^$service " | cut -d' ' -f2- || true)

    if [ -z "$status" ]; then
        fail "$service never started"
        failed=1
    elif echo "$status" | grep -qE 'healthy|Exited \(0\)'; then
        pass "$service — $status"
    else
        fail "$service — ${status:-(no status)}"
        docker compose logs "$service" --no-log-prefix --tail=15 2>&1 | sed 's/^/        /'
        failed=1
    fi
done

cleanup

if [ "$failed" -ne 0 ]; then
    printf '\n\033[31mThe stack does not come up from a clean checkout.\033[0m\n' >&2
    exit 1
fi

printf '\n\033[32mEvery service builds and comes up healthy from an empty volume.\033[0m\n'
