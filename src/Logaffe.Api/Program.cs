using System.Text.Json;
using System.Text.Json.Serialization;
using Logaffe.Api.Cli;
using Logaffe.Api.Hosting;
using Logaffe.Api.Http;
using Logaffe.Api.Mcp;
using Logaffe.Application.Operations;
using Logaffe.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

// Serilog says what is wrong with Serilog here and nowhere else: a file sink
// that cannot open its file writes to SelfLog and carries on, so a volume that
// is read-only or full would cost the log ADR 0002 puts everything in without a
// word anywhere. Standard error, which is the container's own log and where the
// command line already speaks — and before the verbs, because they write to that
// same file and are the ones that promise the operator it holds the detail.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// One binary, two jobs. A recognized verb runs host-locally and exits; no
// arguments is the server.
if (Verbs.TryRead(args, out var verb))
{
    return await Verbs.RunAsync(verb, args);
}

// A word that was meant as a verb and is not one stops here. Serving instead
// would be the worst available answer: it succeeds, it reports itself healthy,
// and whatever the command was going to do — restore an installation, say — has
// silently not happened.
if (Verbs.WasMeantAsAVerb(args))
{
    await Console.Error.WriteLineAsync(Verbs.NotAVerb(args[0]));
    return Verbs.Usage;
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

// Nothing in this product reads the ASP.NET Core key ring today: the session
// cookie carries a secret checked against a row (`SessionCookie`), both
// authentication schemes are handlers of our own, and there is no antiforgery,
// no session state and no MVC. The framework builds the ring anyway, and left
// alone it builds it inside the container, under a home directory that goes
// when the container does — and says so on every start:
//
//   [WRN] Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys'
//         that may not be persisted outside of the container.
//
// Which is a warning about losing something nobody is using. It is put on the
// volume rather than silenced because the day some feature does reach for it —
// antiforgery is the likely one — a ring that resets on every upgrade would
// fail in a way nobody would connect to this, and the warning that would have
// said so has been on the screen all along, meaning nothing.
//
// Under `keys/` deliberately: that is where the volume keeps material of this
// kind, it is what a backup carries (ADR 0024), and it is the prefix a restore
// writes back readable by its owner alone.
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(volumePath, "keys", "data-protection")))
    // The ring is discriminated by application name, which otherwise defaults to
    // the content root path — a path this happens to run from, not a fact about
    // the installation.
    .SetApplicationName("logaffe");

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

// The other public write, and a far quieter one: a handful of machines posting
// one reading a minute each. It is registered beside the deliveries because it
// is the same shape of act behind the same shape of door — a token that names
// what it may write to, and nothing that reads.
builder.Services.AddScoped<IngestSample>();

// The claim, which is the whole reachable surface of an installation nobody
// owns. `Recover` is the other half of the same guard and is registered by the
// command line rather than here, because it is host-local and never reachable
// over the network (ADR 0013).
builder.Services.AddSingleton(HostConfiguration.Claim(builder.Configuration));
builder.Services.AddScoped<OpenTheClaim>();
builder.Services.AddScoped<CheckTheClaim>();
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
// and the three that touch the second factor end every other session.
builder.Services.AddScoped<ChangePassword>();
builder.Services.AddScoped<IssueBackupCodes>();
builder.Services.AddScoped<CheckTheSecondFactor>();
builder.Services.AddScoped<BeginEnrolment>();
builder.Services.AddScoped<EnrolTheSecondFactor>();
builder.Services.AddScoped<TurnOffTheSecondFactor>();

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

// The heading a project is listed under, which carries a name and nothing else
// (ADR 0039). Nothing here reads across the projects a group holds: a query
// names one project, and a group is not one.
builder.Services.AddScoped<CreateGroup>();
builder.Services.AddScoped<ListGroups>();
builder.Services.AddScoped<RenameGroup>();
builder.Services.AddScoped<DeleteGroup>();
builder.Services.AddScoped<MoveProjectToGroup>();

// The machine a project runs on, which is the group's shape pointed at
// hardware: a row with an identity that survives its rename, and a relation that
// hangs off that identity. Nothing here is a scope — no query takes a host, and
// naming two projects onto one machine does not make them askable together.
builder.Services.AddScoped<CreateHost>();
builder.Services.AddScoped<ListHosts>();
builder.Services.AddScoped<RenameHost>();
builder.Services.AddScoped<DeleteHost>();
builder.Services.AddScoped<PutProjectOnHost>();
builder.Services.AddScoped<ChangeSampleRetention>();

// The read the ingestion path exists for, and the one surface both consumers
// meet: the MCP tools call exactly these and add no query behaviour of
// their own (`docs/querying.md`).
builder.Services.AddScoped<SearchEntries>();
builder.Services.AddScoped<CountEntries>();
builder.Services.AddScoped<ReadEntry>();

// The fifth read, and the one that is not inside a single project: what a
// machine reported about itself. The band above the operator's entries and the
// agent's tool are this one act, which is what keeps them from becoming two
// views of the same machine (`docs/querying.md`).
builder.Services.AddScoped<ReadSamples>();

// The same filters, watched: the one request the interface repeats on its own,
// asking what has arrived on the receipt clock while the view it feeds keeps the
// order of events (ADR 0009).
builder.Services.AddScoped<TailEntries>();

// The other end of both of those: the window is what this reads, and a deleted
// project's entries are what it takes afterwards (ADR 0019). It is reachable
// from nowhere but the timer below — retention is not an act the operator
// triggers.
builder.Services.AddScoped<SweepExpiredEntries>();

// The same concern over the sample tables, on the same timer: one window for the
// whole installation, and the samples a deleted host left behind.
builder.Services.AddScoped<SweepExpiredSamples>();

// The operator's token acts. They are registered here and reachable from HTTP
// and the command line; the agent token's stay unreachable over MCP, because an
// agent that could issue one would grant itself the kind and the flag the
// operator withheld (ADR 0046).
builder.Services.AddScoped<IssueIngestToken>();
builder.Services.AddScoped<ListIngestTokens>();
builder.Services.AddScoped<IssueHostToken>();
builder.Services.AddScoped<ListHostTokens>();
builder.Services.AddScoped<IssueAgentToken>();
builder.Services.AddScoped<RenameAgentToken>();
builder.Services.AddScoped<ListAgentTokens>();
builder.Services.AddScoped<RevokeToken>();
builder.Services.AddScoped<ReadTokenBack>();

// One for the installation and sealed on first use: a token refused because its
// identifier named no row is compared against this, so that the miss costs what
// a mismatch costs (ADR 0031).
builder.Services.AddSingleton<DummySecret>();

// Order is start order. This one is first because what it has to say is about
// where everything after it is written.
builder.Services.AddHostedService<FileLogService>();

// Then the schema, because the check below reads tables a migration may be about
// to create.
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<KeyFitsService>();

// Last, so that an installation about to refuse to start does not first open a
// claim it will never serve, or draw a secret nobody will be able to use.
builder.Services.AddHostedService<ClaimService>();

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

// The one closed set that crosses the operator's surface — which kind an agent
// token is — travels as the name it is called by rather than as a number, the
// same choice the MCP answers make for the same reason: a number would put a
// mapping into the checked-in contract, into the web client and into whatever
// script an operator writes, and nothing would keep the three of them true.
// Integers are not accepted alongside, so a kind that is not one of the two is
// refused at the door rather than stored as a row nobody can read.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));

// The second adapter over the reads above. It is registered beside them rather
// than inside them: what an agent may call is a fact about this composition
// root, and which of the tools a given agent is offered is a fact about the
// token it presented (ADR 0046).
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
app.MapGroups();
app.MapEntries();
app.MapHosts();
app.MapTokens();
app.MapIngest();
app.MapSamples();
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
