# The build context is the repository root, not ./dashboard.
#
# `@dark-factory/ui` is a pnpm workspace package (ADR-0033) that lives
# outside this directory and is consumed as TypeScript source, so a context
# rooted at ./dashboard cannot see the code it needs — nor the workspace
# manifest and lockfile pnpm resolves it through. Compose passes
# `dockerfile: dashboard/Dockerfile` with `context: .`, and BuildKit reads
# the exclusions from Dockerfile.dockerignore beside this file rather than
# the root .dockerignore (which excludes ./dashboard, because the .NET image
# has no use for it).

FROM node:22-alpine AS build
WORKDIR /repo
RUN corepack enable

# Manifests first, so a source edit does not re-resolve the dependency tree.
# The workspace packages' package.json files have to be present before
# `pnpm install`, or the workspace link cannot be created.
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY packages/ui/package.json packages/ui/
COPY dashboard/package.json dashboard/
RUN pnpm install --frozen-lockfile

COPY packages/ui packages/ui
COPY dashboard dashboard
RUN pnpm --filter dashboard build

# Next's standalone output resolves the workspace and traces only the files
# actually reached, which sidesteps copying a pnpm node_modules tree whose
# symlinks point into a store that would not survive the copy.
FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0
RUN apk add --no-cache curl

COPY --from=build /repo/dashboard/.next/standalone ./
COPY --from=build /repo/dashboard/.next/static ./dashboard/.next/static
COPY --from=build /repo/dashboard/public ./dashboard/public

EXPOSE 3000
CMD ["node", "dashboard/server.js"]
