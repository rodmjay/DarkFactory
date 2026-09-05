using System.Text.Json;
using DarkFactory.Client;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Hosting;
using DarkFactory.Mcp;
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

// The handshake and registry (docs/adr/0018, docs/conventions/describe.md).
// The probe is a singleton because it holds no per-request state; it opens
// a fresh connection per call by design (see McpServerProbe).
builder.Services.AddSingleton<IServerProbe>(sp =>
    new McpServerProbe(sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<FactoryDescribe>();

// docs/adr/0027: all inference goes through IModelGateway, and
// ModelGatewayRegistration is the only place that decides which
// implementation that is. Nothing above the interface — not the
// conversation service, not the engine, not a test — learns which provider
// served a call.
builder.Services.AddModelGateway(builder.Configuration);

var app = builder.Build();

// The factory validates other servers' handshakes against the published
// schema, so it validates its own the same way, at startup, and refuses to
// start if it fails. A factory serving an invalid df.describe while
// rejecting servers for exactly that is not a contract, it's a double
// standard — and this catches it in one second rather than in a customer's
// registration attempt.
var factoryDescribe = app.Services.GetRequiredService<FactoryDescribe>().Build();
var selfCheck = DescribeSchema.Validate(JsonSerializer.Serialize(factoryDescribe));
if (!selfCheck.IsValid)
{
    throw new InvalidOperationException(
        "The factory's own df.describe response does not validate against " +
        $"contracts/schemas/describe.schema.json: {selfCheck.Summarize()}");
}

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
app.MapArtifactEndpoints();

app.Run();
