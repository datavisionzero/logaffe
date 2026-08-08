using Logaffe.Api.Cli;
using Logaffe.Api.Hosting;
using Logaffe.Api.Http;
using Logaffe.Api.Mcp;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Serilog;

// One binary, two jobs. A recognized verb runs host-locally and exits; anything
// else is the server.
if (Verbs.TryRead(args, out var verb))
{
    return await Verbs.RunAsync(verb, args);
}

// Built from the same settings the verbs are, so that the server and the
// command line in one binary read one configuration.
var builder = WebApplication.CreateBuilder(HostConfiguration.ForTheServer(args));

// Everything that is not in the database lives on the host volume, never inside
// the container image, so a container can be destroyed and recreated without
// losing anything.
var volumePath = HostConfiguration.VolumePath(builder.Configuration);

// logaffe does not log into itself — the failures worth diagnosing are the ones
// in which it could not record anything (ADR 0002). The file log is bounded:
// it rolls by size and keeps a fixed number of files, because an unbounded log
// on the same volume as the secrets is the most embarrassing possible way for a
// logging product to take its own installation down.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.WriteToLogaffeFile(volumePath));

builder.Services.AddLogaffeInfrastructure(builder.Configuration);

// The composition root is where the use cases are registered; the layers below
// know nothing about the container they are resolved from.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CheckReadiness>();
builder.Services.AddScoped<CheckTheKeyFits>();
builder.Services.AddScoped<AuthenticateToken>();

// The hottest path in the product, and the one the adoption barrier of
// `VISION.md` is measured on. It is the only use case both public endpoints'
// authentication stands in front of, and the only one that writes entries.
builder.Services.AddScoped<IngestBatch>();

// The claim, which is the whole reachable surface of an installation nobody
// owns. `Recover` is the other half of the same window and is registered by the
// command line rather than here, because it is host-local and never reachable
// over the network (ADR 0013).
builder.Services.AddScoped<OpenTheClaimWindow>();
builder.Services.AddScoped<CheckTheClaim>();
builder.Services.AddScoped<BeginEnrolment>();
builder.Services.AddScoped<ClaimTheInstallation>();

// The operator's door. Authenticating a session is the counterpart of
// authenticating a token — one credential a person carries, one a machine does —
// and everything the operator can do stands behind it.
builder.Services.AddScoped<SignIn>();
builder.Services.AddScoped<AuthenticateSession>();
builder.Services.AddScoped<SignOut>();

// The list, and the ways a session ends that are not a sign-out. With no email
// anywhere in the product the list is the only way the operator can ever notice
// a session that is not theirs (ADR 0015), which makes these a security surface
// rather than a convenience.
builder.Services.AddScoped<ListSessions>();
builder.Services.AddScoped<RevokeSession>();
builder.Services.AddScoped<EndEveryOtherSession>();
builder.Services.AddScoped<RemoveExpiredSessions>();

// The operator's own credentials. Each of these requires the password again,
// and two of them end every other session.
builder.Services.AddScoped<ChangePassword>();
builder.Services.AddScoped<IssueBackupCodes>();
builder.Services.AddScoped<BeginReEnrolment>();
builder.Services.AddScoped<ReEnrolTheSecondFactor>();

// The unit everything else hangs off. Nothing creates one implicitly — a token
// that names nothing admits nothing — so these four acts are the only way a
// project comes about, changes or ends.
builder.Services.AddScoped<CreateProject>();
builder.Services.AddScoped<ListProjects>();
builder.Services.AddScoped<ReadProject>();
builder.Services.AddScoped<RenameProject>();
builder.Services.AddScoped<ChangeRetentionWindow>();
builder.Services.AddScoped<CountEntriesOutsideWindow>();
builder.Services.AddScoped<DeleteProject>();

// The read the ingestion path exists for, and the one surface both consumers
// meet: the four MCP tools call exactly these and add no query behaviour of
// their own (`docs/querying.md`).
builder.Services.AddScoped<SearchEntries>();
builder.Services.AddScoped<CountEntries>();
builder.Services.AddScoped<ReadEntry>();

// The same filters, watched: the one request the interface repeats on its own,
// asking what has arrived on the receipt clock while the view it feeds keeps the
// order of events (ADR 0009).
builder.Services.AddScoped<TailEntries>();

// The other end of both of those: the window is what this reads, and a deleted
// project's entries are what it takes afterwards (ADR 0019). It is reachable
// from nowhere but the timer below — retention is not an act the operator
// triggers.
builder.Services.AddScoped<SweepExpiredEntries>();

// The operator's token acts. They are registered here and reachable from HTTP
// and the command line; what makes them unreachable over MCP is that the MCP
// adapter offers four read tools and nothing else (ADR 0018).
builder.Services.AddScoped<IssueIngestToken>();
builder.Services.AddScoped<ListIngestTokens>();
builder.Services.AddScoped<IssueAgentToken>();
builder.Services.AddScoped<RenameAgentToken>();
builder.Services.AddScoped<ListAgentTokens>();
builder.Services.AddScoped<RevokeToken>();
builder.Services.AddScoped<ReadTokenBack>();

// One for the installation and sealed on first use: a token refused because its
// identifier named no row is compared against this, so that the miss costs what
// a mismatch costs (ADR 0031).
builder.Services.AddSingleton<DummySecret>();

// Order is start order: the schema first, because the check below reads tables a
// migration may be about to create.
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<KeyFitsService>();

// Last, so that an installation about to refuse to start does not first arm a
// window it will never serve.
builder.Services.AddHostedService<ClaimWindowService>();

// After the migrations, whose service has finished before these start: the
// first pass reads a table a migration may have been about to create. Each has
// a timer of its own — one is a statement a day, the other is bounded portions
// over the largest table in the database.
builder.Services.AddHostedService<ExpiredSessionService>();
builder.Services.AddHostedService<RetentionService>();

builder.Services.AddLogaffeRequestSource(builder.Configuration);
builder.Services.AddLogaffeSessionAuthentication();
builder.Services.AddLogaffeAgentAuthentication();
builder.Services.AddLogaffeRateLimits();
builder.Services.AddLogaffeOpenApi();

// The second adapter over the reads above. It is registered beside them rather
// than inside them: what an agent may call is a fact about this composition
// root, and the four tools are the whole of it (ADR 0018).
builder.Services.AddLogaffeAgentTools();

var app = builder.Build();

// Before anything reads an address: the throttle and the session list both act
// on where a request came from, and behind a named proxy that is the forwarded
// value rather than the connection's.
app.UseForwardedHeaders();

app.UseSerilogRequestLogging();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealth();
app.MapClaim();
app.MapSessions();
app.MapOperator();
app.MapProjects();
app.MapEntries();
app.MapTokens();
app.MapIngest();
app.MapAgentTools();

// The single-page application is built by its own toolchain and copied into
// wwwroot at image build time; in development the Vite dev server serves it and
// this finds nothing.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

/// <summary>
/// Named so that a test can start this installation in its own process. Top
/// level statements produce a class that is otherwise unreachable, and asking a
/// running installation what its endpoints admit is the only way to say it —
/// reading the registrations back would be the test writing down what it just
/// wrote.
/// </summary>
public partial class Program;
