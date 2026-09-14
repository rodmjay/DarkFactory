# 0013 — One owner, nothing left unmerged

## Instruction (verbatim)
> just own all sessions and all code for dark factory i dont want to leave anything not merged thats working

## Commit hash
This report's commit, on `main` after `5381932`.

## What changed
- **One checkout, one branch.** `/home/rodmjay/dev/darkfactory` is on `main`, level with `origin/main`. Removed after confirming each was clean and fully in `main`: the worktrees `darkfactory-main`, `darkfactory-spec-strategies` and `darkfactory-drones-intake`, and the branches `spec-strategies`, `screens-step4` and `drones-intake` (`git branch -d`, which refuses unmerged work). No stashes; GitHub has only `main`.
- **The other live session** (`darkfactory-0e`, author of ADR-0039/0040) was told this session owns the code. It confirmed it has nothing pending and will not commit or push again.
- **Deploys now run from the main checkout,** which has its own `.env`. No live container mounted a removed worktree; `workspace-demo` mounts this checkout, now on `main`.

## Verified how
- `git branch --no-merged main` → empty · `git branch -r --no-merged origin/main` → empty · `git stash list` → empty · `git worktree list` → one entry.
- `docker inspect` bind mounts of every live container → none under a removed path.
- `./scripts/check-stack.sh` → run before this commit (result in the commit message).

## Decisions I made that weren't specified
- Kept nothing "just in case". Every removed branch was an ancestor of `main`.

## Things I was wrong about
- Nothing new.

## What I did not do and why
- **Dark Factory's integration inside Moonbeam** (`moonbeam-mcp/df/`, the corpus and standards servers) is committed on `moonbeam-mcp` `main` but not pushed. Six of its eight unpushed commits are this work; two are other Moonbeam sessions' work that a push would publish too. That repo is not this one, so Rod decides.
- `~/dev/df-clean-verify` is a Sep 5 verification clone: fully in `main`, with only three untracked `.log` files. Left in place; nothing in it is unmerged.
- The Moonbeam repos' own uncommitted game and spec work belongs to the live Moonbeam sessions; untouched.

## Next
Deploy from `/home/rodmjay/dev/darkfactory`, migrate first:
`docker compose -p darkfactory -f docker-compose.yml up --build --no-deps --exit-code-from migrate migrate`, then `… up -d --build --no-deps factory worker dashboard`.
