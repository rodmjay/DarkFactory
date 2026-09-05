using DarkFactory.Data;
using DarkFactory.Hosting;
using DarkFactory.Engine;
using DarkFactory.Engine.Agents;
using DarkFactory.Engine.StageHandlers;
using DarkFactory.Client;
using DarkFactory.Core;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("dark-factory-engine"))
    .WithTracing(tracing =>
    {
        tracing.AddConsoleExporter();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddDarkFactoryData(builder.Configuration);

// The worker runs the stage agents, so it needs a model gateway — chosen
// by exactly the same code as `factory` uses, so the two hosts cannot
// disagree about which model serves a run (docs/adr/0027).
builder.Services.AddModelGateway(builder.Configuration);
builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection(EngineOptions.SectionName));

// Intake and spec are no longer stages of a run (docs/adr/0003, as
// amended) — they are the conversation. Their handlers remain registered
// only so runs created before step 3c can still drain; nothing new ever
// enters at those stages.
builder.Services.AddScoped<IStageHandler, IntakeStageHandler>();
builder.Services.AddScoped<IStageHandler, SpecStageHandler>();

// Which handlers run plan/implement/verify/ship.
//
// `stub` exists for one reason: the engine's durability mechanics —
// checkpointing, leasing, resume-after-crash — have to be testable without
// a model provider or a workspace server, because a test that needed
// either would be testing them instead. It is opt-in, never a fallback:
// an unconfigured factory runs the real agents and fails honestly, rather
// than quietly fabricating artifacts.
var useStubHandlers = string.Equals(
    builder.Configuration["Engine:StageHandlers"], "stub", StringComparison.OrdinalIgnoreCase);

if (useStubHandlers)
{
    builder.Services.AddScoped<IStageHandler, PlanStageHandler>();
    builder.Services.AddScoped<IStageHandler, ImplementStageHandler>();
    builder.Services.AddScoped<IStageHandler, VerifyStageHandler>();
    builder.Services.AddScoped<IStageHandler, ShipStageHandler>();
}
else
{
    // The real agents (step 3d). Each calls a model through the recording
    // gateway, so every call leaves a model_calls row (docs/adr/0032).
    builder.Services.AddScoped<StageAgent>();
    builder.Services.AddScoped<IStageHandler, AgentPlanStageHandler>();
    builder.Services.AddScoped<IStageHandler, AgentImplementStageHandler>();
    builder.Services.AddScoped<IStageHandler, AgentVerifyStageHandler>();
    builder.Services.AddScoped<IStageHandler, AgentShipStageHandler>();
}

// The workspace server is reached over MCP, exactly as the registry
// reaches any other server (docs/adr/0001).
builder.Services.AddSingleton<IServerProbe>(sp =>
    new McpServerProbe(sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddScoped<GateService>();
builder.Services.AddScoped<RunStateMachine>();
builder.Services.AddHostedService<EngineWorker>();

var app = builder.Build();

if (useStubHandlers)
{
    // Loud on purpose. Anything that reaches `ship` in this mode produced
    // nothing real, and a quiet log line is not enough to stop someone
    // believing otherwise.
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DarkFactory.Engine")
        .LogWarning(
            "Engine:StageHandlers=stub — plan/implement/verify/ship are STUBS. No model is called, no code " +
            "is written, and no test is run. This mode exists for the durability tests; artifacts it " +
            "produces are fabricated.");
}

// Refuse to start against a schema this build doesn't understand (see
// SchemaGuard.cs and docs/adr/0008). Migrations run only from the
// `migrate` service/entrypoint (DarkFactory.Migrate), never here.
await SchemaGuard.EnsureSchemaUpToDateAsync(
    builder.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)!);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHealthChecks("/health/ready");

app.Run();
