#!/usr/bin/env bash
#
# Refuses to let a credential reach a push.
#
# Two scopes, because they answer different questions:
#
#   working tree  — exactly the files that *could* be committed (tracked,
#                   plus untracked ones git is not ignoring). Ignored files
#                   are deliberately out of scope: .env and dashboard/.next
#                   both contain real keys on a working machine and neither
#                   can reach a push, so flagging them trains people to
#                   ignore this script. .env gets its own checks below,
#                   which ask the question that actually matters.
#
#   history       — every commit reachable from any branch. Deleting a key
#                   in a later commit does not remove it; the object is
#                   still there and `git push` publishes all of them. The
#                   only useful time to find a key in history is before the
#                   first push.
#
# Every run self-tests: a synthetic credential is planted where the scanner
# must see it, and the run fails if it does not. A secret scanner that
# silently stops matching — wrong flags, missing image, a config change —
# looks exactly like a clean repository, which is the worst failure mode a
# check of this kind can have (docs/TESTING.md).
#
# Uses gitleaks: the host binary if present, otherwise the official image,
# since Docker is already a dependency here and a scanner that needs
# installing first is a scanner that gets skipped.
#
# Usage:  scripts/check-secrets.sh [--tree-only|--history-only]
# Exit:   0 clean, 1 findings, 2 the scan could not be trusted.

set -uo pipefail

cd "$(dirname "$0")/.."
REPO=$PWD

MODE=${1:-all}
IMAGE=zricethezav/gitleaks:latest

# Inside the repo rather than /tmp on purpose. This tree already has to sit
# somewhere Docker is allowed to share, because workspace-demo bind-mounts
# it; /tmp carries no such guarantee and fails with "mounts denied" on a
# default Docker Desktop.
STAGING=$REPO/.secret-scan-tmp

say()  { printf '\n\033[1m== %s\033[0m\n' "$1"; }
fail() { printf '\033[31mFAIL  %s\033[0m\n' "$1" >&2; }
pass() { printf '\033[32mPASS  %s\033[0m\n' "$1"; }

cleanup() { rm -rf "$STAGING"; }
trap cleanup EXIT

# --redact so a finding never prints the credential it found. A scanner that
# echoes secrets into a terminal, a CI log, or a chat transcript has widened
# the exposure rather than reported it.
gitleaks_run() {
    if command -v gitleaks >/dev/null 2>&1; then
        gitleaks "$@" --no-banner --redact
    else
        # A worktree's .git is a file pointing into the main checkout's .git by
        # absolute path. Mount that too, at the same path, or git inside the
        # container finds no repository and the history scan reads nothing —
        # and reports that as a pass.
        local common
        common=$(cd "$(git rev-parse --git-common-dir)" && pwd)
        docker run --rm -v "$REPO:/repo" -v "$common:$common" -w /repo "$IMAGE" "$@" --no-banner --redact
    fi
}

if ! command -v gitleaks >/dev/null 2>&1 && ! docker info >/dev/null 2>&1; then
    fail "neither gitleaks nor a running Docker daemon — cannot scan."
    printf '  Install gitleaks, or start Docker and re-run.\n' >&2
    exit 2
fi

if ! command -v gitleaks >/dev/null 2>&1; then
    docker image inspect "$IMAGE" >/dev/null 2>&1 || docker pull -q "$IMAGE" >/dev/null 2>&1 || true
fi

status=0

# ---------------------------------------------------------------------------
# .env — asked directly, not left to a pattern match
# ---------------------------------------------------------------------------
#
# A scanner answers "does this look like a credential". These three answer
# "can the file holding the credentials be committed", which is the question
# with a yes/no answer and no false positives.

say "Environment files"

if git ls-files --error-unmatch .env >/dev/null 2>&1; then
    fail ".env is tracked — it holds a real key on every developer machine."
    status=1
else
    pass ".env is not tracked"
fi

if git check-ignore -q .env; then
    pass ".env is ignored by .gitignore"
else
    fail ".env is not ignored — the next 'git add -A' commits it."
    status=1
fi

# Not "is it tracked now" but "was it ever added", which is the question for
# a repository that is about to be published for the first time.
env_adds=$(git log --all --oneline --diff-filter=A -- '.env' '**/.env' 2>/dev/null)
if [ -n "$env_adds" ]; then
    fail ".env appears in history — it was committed once and is still in the objects."
    printf '%s\n' "$env_adds" | sed 's/^/        /' >&2
    status=1
else
    pass ".env has never been committed on any branch"
fi

# ---------------------------------------------------------------------------
# Working tree — the committable set, plus the canary
# ---------------------------------------------------------------------------

if [ "$MODE" != "--history-only" ]; then
    say "Working tree (files that could be committed)"

    cleanup
    mkdir -p "$STAGING/tree" "$STAGING/canary"

    # -z / --null throughout: paths in this repo are tame, but a scanner
    # that skips a file because of a space in its name fails silently.
    if ! git ls-files --cached --others --exclude-standard -z \
        | tar -c --null -T - -f - 2>/dev/null \
        | tar -x -C "$STAGING/tree" 2>/dev/null; then
        fail "could not assemble the committable file set"
        exit 2
    fi

    count=$(find "$STAGING/tree" -type f | wc -l)
    printf '  %s files\n' "$count"

    # Must match the vendor's documented key format exactly: the 'sk-ant-'
    # prefix is not enough on its own, and neither is length. gitleaks wants
    # the 'api03-' segment, 93 high-entropy characters, and the trailing
    # 'AA'. A canary missing any of those is never flagged, the self-test
    # passes vacuously, and the check becomes decoration — which is how this
    # was first written and what the self-test caught.
    #
    # Assembled from parts and randomised per run: a complete key written
    # literally here would be found by the scan of this very file, and the
    # canary would report itself forever.
    canary="sk-ant-"
    canary+="api03-"
    canary+="$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 93)AA"
    printf 'ANTHROPIC_API_KEY=%s\n' "$canary" > "$STAGING/canary/planted.env"

    if gitleaks_run detect --no-git --source .secret-scan-tmp/canary >/dev/null 2>&1; then
        fail "self-test: the scanner did not flag a planted credential."
        printf '  The scan cannot be trusted, so a clean result below means nothing.\n' >&2
        printf '  Check the gitleaks version, flags, and any .gitleaks.toml.\n' >&2
        exit 2
    fi
    pass "self-test: scanner flags a planted credential"

    if gitleaks_run detect --no-git --source .secret-scan-tmp/tree; then
        pass "no credentials in committable files"
    else
        fail "gitleaks found credentials in files that could be committed"
        status=1
    fi

    cleanup
fi

# ---------------------------------------------------------------------------
# History
# ---------------------------------------------------------------------------

if [ "$MODE" != "--tree-only" ]; then
    say "History (every commit on every branch)"
    history_log=$(mktemp)
    if gitleaks_run detect --source . --log-opts="--all" > "$history_log" 2>&1; then history_rc=0; else history_rc=$?; fi
    cat "$history_log"
    scanned=$(grep -oE '[0-9]+ commits scanned' "$history_log" | grep -oE '^[0-9]+' | tail -1)
    rm -f "$history_log"
    commits=$(git rev-list --all --count)

    if [ "$history_rc" -eq 0 ] && [ "${scanned:-0}" -eq 0 ] && [ "$commits" -gt 0 ]; then
        # "No leaks" about zero commits has checked nothing. Run from a
        # worktree, that is what this scan used to report — as a pass.
        fail "the history scan read 0 of $commits commits — it checked nothing"
        status=1
    elif [ "$history_rc" -eq 0 ]; then
        pass "no credentials in any commit ($scanned scanned)"
    else
        fail "gitleaks found credentials in history"
        printf '\033[33mDeleting the file in a later commit does not fix this — the object is\n' >&2
        printf 'still there and push publishes it. Rotate the credential first, then\n' >&2
        printf 'rewrite history if this repository has not been published yet.\033[0m\n' >&2
        status=1
    fi
fi

echo
if [ "$status" -ne 0 ]; then
    fail "secret scan failed"
    exit 1
fi

pass "no credentials in committable files or in history"
