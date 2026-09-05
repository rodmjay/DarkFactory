#!/bin/sh
set -eu

# The demo works on a *copy*. /seed is the developer's actual repository,
# bind-mounted read-only; the factory branches, writes files and commits
# during a run, and none of that belongs in someone's working tree. Copying
# on start also means every `docker compose up` begins from a clean,
# predictable state rather than from whatever the last demo left behind.

if [ ! -d "$SEED_DIR" ]; then
    echo "expected the seed repository bind-mounted at $SEED_DIR" >&2
    exit 1
fi

echo "seeding $WORKSPACE_DIR from $SEED_DIR ..."
rm -rf "$WORKSPACE_DIR"
mkdir -p "$WORKSPACE_DIR"

# Skip the heavy, regenerable directories. Copying node_modules and bin/obj
# would turn a two-second startup into a minute for no benefit — the demo
# needs the source, not the build output.
#
# --no-same-owner on the way out: without it the copy keeps the host user's
# uid, and git — running as root in here — refuses to touch a repository it
# does not own ("detected dubious ownership"). The copy is genuinely this
# container's, so it should be owned by this container.
tar -C "$SEED_DIR" \
    --exclude=./.git \
    --exclude=node_modules \
    --exclude=bin \
    --exclude=obj \
    --exclude=.next \
    -cf - . | tar -C "$WORKSPACE_DIR" --no-same-owner -xf -

# A fresh repository, not the developer's history: the demo may commit and
# branch freely, and nothing it does can be pushed anywhere by accident
# because there is no remote.
cd "$WORKSPACE_DIR"
git init -q -b main
git config user.email "demo@darkfactory.invalid"
git config user.name "Dark Factory Demo"
git add -A
git commit -q -m "Seed the demo workspace"

echo "workspace ready at $WORKSPACE_DIR"
exec node /app/dist/index.js --http "${PORT:-8931}" --root "$WORKSPACE_DIR"
