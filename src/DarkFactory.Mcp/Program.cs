using DarkFactory.Data;
using DarkFactory.Mcp.Endpoints;
using DarkFactory.Mcp.Hubs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Traces export to the console in v1. The OTLP exporter is wired but stays
// disabled unless OTEL_EXPORTER_OTLP_ENDPOINT is set, so switching a hosted
// deployment over to a real collector is a config change, not a code change.
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("dark-factory-mcp"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddConsoleExporter();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddDarkFactoryData(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddSignalR();
builder.Services.AddScoped<DarkFactory.Data.IRunEventBroadcaster, SignalRRunEventBroadcaster>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();

var app = builder.Build();

// Refuse to start against a schema this build doesn't understand (see
// SchemaGuard.cs and docs/adr/0008). Migrations run only from the
// `migrate` service/entrypoint (DarkFactory.Migrate), never here.
await SchemaGuard.EnsureSchemaUpToDateAsync(
    builder.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)!);

// Liveness: the process is up. Deliberately does not check dependencies —
// that's what /health/detailed (a factory-managed feature, not container
// plumbing) is for; see the demo walkthrough in README.md.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Readiness: dependencies (Postgres) are reachable. Used by `docker compose`
// health checks so `factory`/`worker` only report healthy once the database
// is actually usable.
app.MapHealthChecks("/health/ready");

app.MapMcp("/mcp");
app.MapHub<RunHub>("/hubs/runs");
app.MapWebhookEndpoints();

app.Run();
