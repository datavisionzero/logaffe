using Logaffe.Api.Cli;
using Logaffe.Api.Hosting;
using Logaffe.Api.Http;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Serilog;

// One binary, two jobs. A recognized verb runs host-locally and exits; anything
// else is the server.
if (Verbs.TryRead(args, out var verb))
{
    return await Verbs.RunAsync(verb);
}

var builder = WebApplication.CreateBuilder(args);

// Everything that is not in the database lives on the host volume, never inside
// the container image, so a container can be destroyed and recreated without
// losing anything.
var volumePath = builder.Configuration["Logaffe:VolumePath"]
    ?? throw new InvalidOperationException("Logaffe:VolumePath is not configured.");

// logaffe does not log into itself — the failures worth diagnosing are the ones
// in which it could not record anything (ADR 0002). The file log is bounded:
// it rolls by size and keeps a fixed number of files, because an unbounded log
// on the same volume as the secrets is the most embarrassing possible way for a
// logging product to take its own installation down.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(volumePath, "logs", "logaffe-.log"),
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 32L * 1024 * 1024,
        retainedFileCountLimit: 14,
        shared: true));

builder.Services.AddLogaffeInfrastructure(builder.Configuration);

// The composition root is where the use cases are registered; the layers below
// know nothing about the container they are resolved from.
builder.Services.AddScoped<CheckReadiness>();
builder.Services.AddScoped<CheckTheKeyFits>();

// Order is start order: the schema first, because the check below reads tables a
// migration may be about to create.
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<KeyFitsService>();

builder.Services.AddLogaffeOpenApi();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapOpenApi();
app.MapHealth();

// The single-page application is built by its own toolchain and copied into
// wwwroot at image build time; in development the Vite dev server serves it and
// this finds nothing.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;
