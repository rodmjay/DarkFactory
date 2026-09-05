# The reference workspace server, for the `workspace-demo` Compose service.
#
# In production a workspace server is never a Compose service — it runs
# wherever the customer's code lives, and the factory reaches it over the
# network (see README's "From another repo"). This image exists so the
# end-to-end demo works on any machine without depending on a
# host.docker.internal route that a host firewall may block.

FROM node:22-slim AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

# The .NET SDK, because the workspace this serves is a copy of a .NET
# repository and `verify` runs its real test command inside this container.
# A workspace server needs whatever toolchain the code it hosts is built
# with — that is not a demo concession, it is what a workspace server is.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# Node, for the server itself; git, because df.vcs.* is most of what a
# workspace server does and ship branches and commits.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates curl gnupg \
 && mkdir -p /etc/apt/keyrings \
 && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key \
      | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
 && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_22.x nodistro main" \
      > /etc/apt/sources.list.d/nodesource.list \
 && apt-get update && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

# Only .NET 10 runtimes may be present on a developer machine; the copied
# repo targets net8.0 and its test projects roll forward.
ENV DOTNET_ROLL_FORWARD=LatestMajor
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

WORKDIR /app
COPY --from=build /app/node_modules node_modules/
COPY --from=build /app/dist dist/
COPY package.json ./
COPY docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

ENV SEED_DIR=/seed
ENV WORKSPACE_DIR=/workspace
EXPOSE 8931

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
