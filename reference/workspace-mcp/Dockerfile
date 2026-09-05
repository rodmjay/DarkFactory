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

FROM node:22-slim AS runtime
# git, because df.vcs.* is most of what a workspace server is for, and the
# demo's ship stage branches and commits.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

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
