# Builds the `factory` (DarkFactory.Mcp), `worker` (DarkFactory.Engine), and
# `migrate` (DarkFactory.Migrate) Compose services from one image
# (docs/adr/0008, README's Compose services section: "worker (engine, same
# image, different entrypoint)"). Build context is the repo root; see
# docker-compose.yml.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DarkFactory.sln ./
COPY src/DarkFactory.Core/DarkFactory.Core.csproj src/DarkFactory.Core/
COPY src/DarkFactory.Contracts/DarkFactory.Contracts.csproj src/DarkFactory.Contracts/
COPY src/DarkFactory.Data/DarkFactory.Data.csproj src/DarkFactory.Data/
COPY src/DarkFactory.Client/DarkFactory.Client.csproj src/DarkFactory.Client/
COPY src/DarkFactory.Foundry/DarkFactory.Foundry.csproj src/DarkFactory.Foundry/
COPY src/DarkFactory.Anthropic/DarkFactory.Anthropic.csproj src/DarkFactory.Anthropic/
COPY src/DarkFactory.Hosting/DarkFactory.Hosting.csproj src/DarkFactory.Hosting/
COPY src/DarkFactory.Migrate/DarkFactory.Migrate.csproj src/DarkFactory.Migrate/
COPY src/DarkFactory.Mcp/DarkFactory.Mcp.csproj src/DarkFactory.Mcp/
COPY src/DarkFactory.Engine/DarkFactory.Engine.csproj src/DarkFactory.Engine/
RUN dotnet restore src/DarkFactory.Migrate/DarkFactory.Migrate.csproj \
 && dotnet restore src/DarkFactory.Mcp/DarkFactory.Mcp.csproj \
 && dotnet restore src/DarkFactory.Engine/DarkFactory.Engine.csproj

COPY src/ src/
# DarkFactory.Contracts embeds contracts/schemas/describe.schema.json as a
# linked resource, so the published schemas are part of the compile, not
# just documentation sitting beside it (see DescribeSchema.cs).
COPY contracts/ contracts/
RUN dotnet publish src/DarkFactory.Migrate/DarkFactory.Migrate.csproj -c Release -o /app/migrate --no-restore \
 && dotnet publish src/DarkFactory.Mcp/DarkFactory.Mcp.csproj -c Release -o /app/mcp --no-restore \
 && dotnet publish src/DarkFactory.Engine/DarkFactory.Engine.csproj -c Release -o /app/engine --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# curl backs the Compose HEALTHCHECK for both the factory and worker
# services (both host a /health endpoint; see docker-compose.yml).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/migrate /app/migrate
COPY --from=build /app/mcp /app/mcp
COPY --from=build /app/engine /app/engine

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# `factory` (default). `worker` overrides CMD with `command:` in
# docker-compose.yml to run /app/engine instead — ENTRYPOINT stays just
# `dotnet` so Compose's `command:` fully replaces which .dll runs rather
# than appending to it.
ENTRYPOINT ["dotnet"]
CMD ["/app/mcp/DarkFactory.Mcp.dll"]
