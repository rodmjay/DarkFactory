using DarkFactory.Data;
using DarkFactory.Hosting;
using DarkFactory.Engine;
using DarkFactory.Engine.StageHandlers;
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

builder.Services.AddScoped<IStageHandler, IntakeStageHandler>();
builder.Services.AddScoped<IStageHandler, SpecStageHandler>();
builder.Services.AddScoped<IStageHandler, PlanStageHandler>();
builder.Services.AddScoped<IStageHandler, ImplementStageHandler>();
builder.Services.AddScoped<IStageHandler, VerifyStageHandler>();
builder.Services.AddScoped<IStageHandler, ShipStageHandler>();
builder.Services.AddScoped<GateService>();
builder.Services.AddScoped<RunStateMachine>();
builder.Services.AddHostedService<EngineWorker>();

var app = builder.Build();

// Refuse to start against a schema this build doesn't understand (see
// SchemaGuard.cs and docs/adr/0008). Migrations run only from the
// `migrate` service/entrypoint (DarkFactory.Migrate), never here.
await SchemaGuard.EnsureSchemaUpToDateAsync(
    builder.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)!);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHealthChecks("/health/ready");

app.Run();
