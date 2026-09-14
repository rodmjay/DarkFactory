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

# ---------------------------------------------------------------------------
# Its own Compose project — never the one a developer is running
# ---------------------------------------------------------------------------
#
# This script starts and ends with `docker compose down -v`, which deletes
# volumes. Under the default project name — the checkout's directory — that
# was the developer's live stack, and on 2026-09-13 the live stack's volume
# held the only registration of a customer project. Running the gate that
# CLAUDE.md requires before every commit to main would have deleted it.
#
# So the check gets its own project, its own volume and its own host ports,
# and can run beside a live stack without touching any of it. The ports are
# the defaults plus 20000, overridable, because a collision with them is the
# machine, not the code.
export COMPOSE_PROJECT_NAME="${CHECK_STACK_PROJECT:-darkfactory-check}"
if [ "$COMPOSE_PROJECT_NAME" = "darkfactory" ] || [ "$COMPOSE_PROJECT_NAME" = "$(basename "$PWD")" ]; then
    fail "refusing to run as Compose project '$COMPOSE_PROJECT_NAME': that is the project a developer runs, and this script deletes its volumes."
    exit 1
fi
export POSTGRES_PORT="${CHECK_STACK_POSTGRES_PORT:-25432}"
export FACTORY_PORT="${CHECK_STACK_FACTORY_PORT:-25100}"
export DASHBOARD_PORT="${CHECK_STACK_DASHBOARD_PORT:-23000}"
export WORKSPACE_DEMO_PORT="${CHECK_STACK_WORKSPACE_DEMO_PORT:-28931}"

cleanup() {
    if [ "$KEEP" -eq 0 ]; then
        docker compose down -v >/dev/null 2>&1 || true
    else
        printf '\nStack left running (--keep) as project %s. `docker compose -p %s down -v` when finished.\n' \
            "$COMPOSE_PROJECT_NAME" "$COMPOSE_PROJECT_NAME"
    fi
}

# ---------------------------------------------------------------------------
# Credentials, before anything expensive
# ---------------------------------------------------------------------------
#
# First because it takes about four seconds, and because it is the one
# failure here that nothing else can compensate for: a stack that comes up
# perfectly with a key in its history is not publishable. Aborting rather
# than folding into the summary at the end, so the message is the last thing
# on screen instead of being buried under five minutes of Docker output.
#
# scripts/hooks/pre-push runs the same script, for anyone who has enabled it
# (git config core.hooksPath scripts/hooks). Both, deliberately: the hook is
# opt-in per clone and so cannot be relied on, and this script is the gate
# the README already requires before committing to main.
say "Credentials"
if [ ! -f "$(dirname "$0")/check-secrets.sh" ]; then
    # Not skipped when absent. Skipping is how a gate disappears: the run
    # stays green, the missing scan looks identical to a clean one, and the
    # first anyone knows is after a push.
    fail "scripts/check-secrets.sh is missing — nothing scanned for credentials."
    exit 1
fi
if ! bash "$(dirname "$0")/check-secrets.sh" | sed 's/^/  /'; then
    printf '\n\033[31mStopping before the stack check — fix this first.\033[0m\n' >&2
    exit 1
fi

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
    # Docker Desktop only bind-mounts paths it has been given access to, and
    # workspace-demo mounts the repo. A checkout outside those paths fails
    # with a message about the daemon rather than about the checkout.
    if grep -qi 'mounts denied' "$up_log"; then
        printf '\n\033[33mThis is Docker file sharing, not a problem with the code.\033[0m\n' >&2
        printf 'workspace-demo bind-mounts this repository, so the checkout has to sit under a\n' >&2
        printf 'path Docker is allowed to share (Docker Desktop -> Resources -> File Sharing).\n' >&2
    fi

    if grep -qiE 'port is already allocated|address already in use|ports are not available' "$up_log"; then
        printf '\n\033[33mThis is a host port collision, not a problem with the code.\033[0m\n' >&2
        # :0 is dropped because the daemon's message reads
        # "exposing port TCP 0.0.0.0:5432 -> 127.0.0.1:0", and listing the
        # target side as a port in use sends people looking for a conflict
        # on a port that does not exist.
        grep -oiE '(0\.0\.0\.0|127\.0\.0\.1):[0-9]+' "$up_log" \
            | grep -v ':0$' | sort -u | sed 's/^/  in use: /' >&2
        printf 'Set the matching override and re-run:\n' >&2
        printf '  CHECK_STACK_POSTGRES_PORT  CHECK_STACK_FACTORY_PORT  CHECK_STACK_DASHBOARD_PORT  CHECK_STACK_WORKSPACE_DEMO_PORT\n' >&2
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

    # A clean checkout has no node_modules, and this check is the one thing
    # in the repo whose whole purpose is to work from a clean checkout. An
    # error message telling the reader to run pnpm install is honest and
    # still the wrong answer: the gate knows what it needs, so it installs
    # it — the same reason the test fixture builds the reference server
    # rather than telling you to.
    if [ ! -d node_modules ] || [ ! -d dashboard/node_modules ]; then
        printf '  installing workspace dependencies (first run in this checkout)\n'
        if ! pnpm install --frozen-lockfile > "$log" 2>&1; then
            tail -15 "$log"; rm -f "$log"
            fail "pnpm install --frozen-lockfile"
            return 1
        fi
    fi

    if ! pnpm --filter dashboard build > "$log" 2>&1; then
        tail -15 "$log"; rm -f "$log"
        fail "pnpm --filter dashboard build"
        return 1
    fi

    # A server left on this port by an earlier run answers the health check
    # for this build and serves none of its files — a 500 on every asset.
    # That is how an orphaned next-server was found on 2026-09-14, a day
    # after the run that started it. Refuse rather than test the wrong server.
    if curl -s -o /dev/null "http://localhost:$port/" 2>/dev/null; then
        rm -f "$log"
        fail "port $port is already serving something (ss -ltnp | grep :$port) — stop it or set WEB_SMOKE_PORT"
        return 1
    fi

    # Its own process group, so stopping it stops next-server too. Killing
    # pnpm alone left next-server running: the asset check below passed only
    # because that orphan was still answering, and it held the port for every
    # later run.
    if command -v setsid >/dev/null 2>&1; then
        PORT="$port" setsid pnpm --filter dashboard start > "$log" 2>&1 &
    else
        PORT="$port" pnpm --filter dashboard start > "$log" 2>&1 &
    fi
    local pid=$!
    stop_web() { kill -- -"$pid" 2>/dev/null || kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; }

    local ok=1
    for _ in $(seq 1 30); do
        if curl -fsS -o /dev/null "http://localhost:$port/api/health" 2>/dev/null; then ok=0; break; fi
        kill -0 "$pid" 2>/dev/null || break
        sleep 1
    done

    if [ "$ok" -ne 0 ]; then
        stop_web
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
        stop_web
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
    # -L, because a developer's browser follows redirects and `/` is one:
    # it sends the reader to step 1 of the flow. Reading the redirect's own
    # body instead would scrape whatever stub the framework put there, which
    # is not a page anybody sees.
    local page css asset_status asset_bytes
    page=$(curl -fsSL "http://localhost:$port/" 2>/dev/null || true)
    css=$(printf '%s' "$page" | grep -oE '/_next/static/[^"]+\.css' | head -1)

    if [ -z "$css" ]; then
        stop_web
        rm -f "$log"
        fail "the page referenced no stylesheet — served markup with no assets"
        return 1
    fi

    asset_status=$(curl -s -o /tmp/df-asset -w '%{http_code}' "http://localhost:$port$css" 2>/dev/null || echo 000)
    asset_bytes=$(wc -c < /tmp/df-asset 2>/dev/null || echo 0)
    rm -f /tmp/df-asset

    # Stopped before judging, so no outcome below leaves it on the port.
    stop_web

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
