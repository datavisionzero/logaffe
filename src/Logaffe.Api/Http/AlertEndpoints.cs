using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Projects;
using Microsoft.AspNetCore.RateLimiting;

namespace Logaffe.Api.Http;

/// <summary>
/// Which of the four conditions this installation is switched on for.
/// </summary>
/// <remarks>
/// All four every time, because they are one setting with four parts. There is
/// no threshold beside any of them and no per-project variation of one: what the
/// operator adjusts is these four switches and the mute on a project
/// (<c>docs/alerts.md</c>).
/// </remarks>
public sealed record AlertSwitchesRequest(
    bool FillingUp, bool GoneQuiet, bool Flooding, bool Failing);

/// <summary>
/// The machine logaffe itself runs on, and the mount holding its database.
/// </summary>
/// <param name="HostId">
/// One of the installation's hosts, or <c>null</c> to name none — which takes
/// the mount with it.
/// </param>
/// <param name="Mount">
/// One of the mounts that host reports, picked rather than typed
/// (<c>docs/metrics.md</c>). Read past when no host is named.
/// </param>
public sealed record InstallationHostRequest(Guid? HostId, string? Mount);

/// <summary>
/// The one place this installation's notifications go.
/// </summary>
/// <param name="AccessToken">
/// The token to seal, <c>null</c> to keep whatever is already sealed, or the
/// empty string for a topic that needs none — which is what most self-hosters
/// are on. The three are distinct because a screen cannot show a secret it is
/// about to overwrite: correcting a topic is not re-typing a token.
/// </param>
public sealed record NotifierRequest(string? Server, string? Topic, string? AccessToken);

/// <summary>
/// The notifier as the alerts area shows it, which is a server and a topic and
/// not a secret.
/// </summary>
/// <param name="HasAccessToken">
/// Whether a token is sealed against it. The token itself is a read of its own,
/// for the reason every other secret in this product is (ADR 0022).
/// </param>
public sealed record NotifierResponse(string Server, string Topic, bool HasAccessToken);

/// <summary>The token itself, which is here only because it was asked for.</summary>
public sealed record NotifierTokenResponse(string Token);

/// <summary>How the notification the operator asked for ended.</summary>
/// <remarks>
/// The one send in this product that answers. Everything else about alerting
/// fails silently by design — a failed send is one line in the installation's own
/// file log, with no retry and no queue — which is exactly why a notifier nobody
/// has proved is one that gets discovered broken on the night it was needed.
/// </remarks>
public sealed record TestNotificationResponse(NotifierProof Proof);

/// <summary>
/// The condition about the disk: what the mount last said, or what stands
/// between this installation and knowing.
/// </summary>
/// <param name="Blindness">
/// Why it cannot be evaluated, and <c>none</c> when nothing is in the way. A
/// condition switched on and blind says so where it is switched on: an operator
/// who thinks a disk is being watched when it is not is worse off than one who
/// was never offered the switch.
/// </param>
/// <param name="Percent">
/// How full the named mount is, or <c>null</c> when there is no reading to say
/// it with.
/// </param>
/// <param name="FirstThreshold">
/// The per cent this first says something at, and <paramref name="SecondThreshold"/>
/// the worse one it says the second thing at. They are the product's numbers and
/// not an installation's, and they are carried so that the screen states them
/// rather than repeating them from memory.
/// </param>
public sealed record StoreConditionResponse(
    Blindness Blindness,
    Guid? HostId,
    string? HostName,
    string? Mount,
    int? Percent,
    int FirstThreshold,
    int SecondThreshold);

/// <summary>
/// One project and how long its silence has to last before anything is said.
/// </summary>
public sealed record ToleratedSilenceResponse(Guid ProjectId, string Name, int ToleratedHours);

/// <summary>
/// What "gone quiet" works out to in this installation, at both ends of it.
/// </summary>
/// <param name="Busiest">
/// The project noticed soonest, or <c>null</c> when nothing here can fire yet.
/// </param>
/// <param name="Quietest">The project noticed latest.</param>
/// <param name="WithoutAFortnight">
/// How many projects have too little history to fire any rate condition,
/// however they behave.
/// </param>
public sealed record QuietConditionResponse(
    ToleratedSilenceResponse? Busiest,
    ToleratedSilenceResponse? Quietest,
    int WithoutAFortnight,
    int Multiple,
    int LeastToleratedHours,
    int BaselineDays);

/// <summary>
/// The condition about a flood, which has no installation-specific number to
/// state: what it compares against is the project's own median for that hour of
/// the day, worked out when the hour closes.
/// </summary>
public sealed record FloodConditionResponse(int Multiple, long Floor, int BaselineDays);

/// <summary>
/// The condition about a project failing, which has no installation-specific
/// number either: what it compares against is the project's own median of
/// entries at <c>Error</c> or above for that hour of the day.
/// </summary>
/// <param name="ConsecutiveHours">
/// How many closed hours in a row have to hold before anything is said, which is
/// the whole of what separates this from the flood condition and is what buys
/// the latency the screen states.
/// </param>
public sealed record FailureConditionResponse(
    int Multiple, long Floor, int BaselineDays, int ConsecutiveHours);

/// <summary>When one condition last fired about one subject.</summary>
/// <remarks>
/// It is the only history there is. There is no alert list, nothing to
/// acknowledge and nothing to dismiss — an alert leaves the installation and
/// does not accumulate on a screen (<c>docs/ui.md</c>).
/// </remarks>
public sealed record LastFiredResponse(
    Guid SubjectId, string Subject, AlertCondition Condition, DateTimeOffset At);

/// <summary>The whole of the alerts area, in one read.</summary>
public sealed record AlertSettingsResponse(
    NotifierResponse? Notifier,
    AlertSwitchesRequest Switches,
    StoreConditionResponse Store,
    QuietConditionResponse Quiet,
    FloodConditionResponse Flood,
    FailureConditionResponse Failure,
    IEnumerable<LastFiredResponse> Fired);

/// <summary>
/// The operator's alerting acts, reached over HTTP and over nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no tool beside any of these.</b> Every other operator surface in
/// this product has a counterpart on the administering half of MCP; this one has
/// none, and the absence is the decision rather than an omission — neither kind
/// of agent token reaches the notifier, the switches or the mute
/// (<c>docs/mcp.md</c>, <c>docs/alerts.md</c>). Whether that changes is a change
/// to that document.
/// </para>
/// <para>
/// <b>The area is one read.</b> The switch, what it currently works out to and
/// whether it can see are the same sentence on the screen, so they arrive
/// together — the exception being the access token, which is asked for
/// separately because it is a secret and not a setting (ADR 0022).
/// </para>
/// <para>
/// <b>Nothing here is a list of alerts.</b> What the history route answers is
/// when each condition last fired for each subject, one row per pair, because
/// that is the whole of what the installation keeps: an alert leaves and does
/// not accumulate.
/// </para>
/// </remarks>
public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlerts(this IEndpointRouteBuilder endpoints)
    {
        var operatorSurface = endpoints
            .MapGroup("/alerts")
            .RequireAuthorization()
            .RequireRateLimiting(PublicRateLimits.Operator);

        operatorSurface.MapGet(string.Empty, async (
                ReadTheAlertSettings read,
                ReadTheNotifier notifier,
                CancellationToken cancellationToken) =>
            {
                var settings = await read.ExecuteAsync(cancellationToken);
                var where = await notifier.ExecuteAsync(cancellationToken);

                return Results.Ok(Shown(settings, where));
            })
            .WithName("ReadAlertSettings")
            .WithSummary("The switches, what each of them currently does, and what last fired.")
            .Produces<AlertSettingsResponse>();

        operatorSurface.MapPut("/switches", async (
                AlertSwitchesRequest request,
                ChangeTheAlertSwitches switches,
                CancellationToken cancellationToken) =>
            {
                // Switching one on while there is no notifier is allowed. It is a
                // real state and a legible one — the alert costs one line in the
                // installation's own log — and refusing it here would mean an
                // operator could not decide what to watch before deciding where
                // to be told about it.
                await switches.ExecuteAsync(
                    new AlertSwitches(
                        request.FillingUp,
                        request.GoneQuiet,
                        request.Flooding,
                        request.Failing),
                    cancellationToken);

                return Results.NoContent();
            })
            .WithName("ChangeAlertSwitches")
            .WithSummary("Switches the four conditions on, or off.")
            .Produces(StatusCodes.Status204NoContent);

        operatorSurface.MapPut("/host", async (
                InstallationHostRequest request,
                NameTheInstallationHost name,
                CancellationToken cancellationToken) =>
                // The pair goes together: a mount without a machine is a string,
                // and a machine without a mount does not say which of its
                // filesystems the database is on.
                await name.ExecuteAsync(request.HostId, request.Mount, cancellationToken) switch
                {
                    NameTheInstallationHostOutcome.Named => Results.NoContent(),
                    NameTheInstallationHostOutcome.NoSuchHost => Results.NotFound(),
                    _ => NotAMount(),
                })
            .WithName("NameTheInstallationHost")
            .WithSummary("Says which machine this installation sits on, and on which mount.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        operatorSurface.MapNotifier();

        return endpoints;
    }

    private static void MapNotifier(this IEndpointRouteBuilder endpoints)
    {
        var notifier = endpoints.MapGroup("/notifier");

        notifier.MapPut(string.Empty, async (
                NotifierRequest request,
                ChangeTheNotifier change,
                CancellationToken cancellationToken) =>
            {
                // The two halves are refused separately, because a screen
                // taking them from a person names the box that is wrong.
                if (!Notifier.IsServer(request.Server))
                {
                    return NotAServer();
                }

                if (!Notifier.IsTopic(request.Topic))
                {
                    return NotATopic();
                }

                await change.ExecuteAsync(
                    request.Server, request.Topic, request.AccessToken, cancellationToken);

                return Results.NoContent();
            })
            .WithName("ChangeTheNotifier")
            .WithSummary("Names the one place this installation's notifications go.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        notifier.MapDelete(string.Empty, async (
                ChangeTheNotifier change,
                CancellationToken cancellationToken) =>
            {
                // The switches are left where they are. An operator who clears a
                // notifier while a condition is on has an alert that costs one
                // line in the installation's own log, which is a real state and
                // is said where the switch is.
                await change.ClearAsync(cancellationToken);

                return Results.NoContent();
            })
            .WithName("ClearTheNotifier")
            .WithSummary("Takes the notifier away, and the sealed token with it.")
            .Produces(StatusCodes.Status204NoContent);

        notifier.MapGet("/token", async (
                ReadTheNotifier read,
                CancellationToken cancellationToken) =>
            {
                // The read-back ADR 0022 exists for, and it is a route of its own
                // for the reason a token's is: a screen showing which server this
                // installation notifies through has not read a secret.
                var where = await read.ExecuteAsync(cancellationToken);

                return where?.AccessToken is { } token
                    ? Results.Ok(new NotifierTokenResponse(token))
                    : Results.NotFound();
            })
            .WithName("ReadTheNotifierToken")
            .WithSummary("The access token this installation publishes with, in the clear.")
            .Produces<NotifierTokenResponse>()
            .Produces(StatusCodes.Status404NotFound);

        notifier.MapPost("/test", async (
                SendATestNotification send,
                CancellationToken cancellationToken) =>
            {
                // Nothing about it is stored. What a notifier did five minutes
                // ago is not evidence about what it will do tonight, and the
                // answer is on the screen of the person who pressed it.
                var proof = await send.ExecuteAsync(cancellationToken);

                return Results.Ok(new TestNotificationResponse(proof));
            })
            .WithName("SendATestNotification")
            .WithSummary("Sends the shape a real alert has, and says how it went.")
            .Produces<TestNotificationResponse>();
    }

    private static AlertSettingsResponse Shown(TheAlertSettings settings, TheNotifier? notifier) =>
        new(
            notifier is null
                ? null
                : new NotifierResponse(
                    notifier.Server, notifier.Topic, notifier.AccessToken is not null),
            new AlertSwitchesRequest(
                settings.Switches.FillingUp,
                settings.Switches.GoneQuiet,
                settings.Switches.Flooding,
                settings.Switches.Failing),
            Shown(settings.Host, settings.Store),
            new QuietConditionResponse(
                Shown(settings.Quiet.Busiest),
                Shown(settings.Quiet.Quietest),
                settings.Quiet.WithoutAFortnight,
                Quiet.Multiple,
                Quiet.LeastTolerated,
                Baseline.Days),
            new FloodConditionResponse(Flood.Multiple, Flood.Floor, Baseline.Days),
            new FailureConditionResponse(
                Failure.Multiple, Failure.Floor, Baseline.Days, Failure.ConsecutiveHours),
            settings.Fired.Select(fired => new LastFiredResponse(
                fired.SubjectId, fired.Subject, fired.Condition, fired.At)));

    /// <remarks>
    /// The mount is echoed from the installation's own row rather than from the
    /// reading, so that a mount the host has stopped reporting is still on the
    /// screen it has to be corrected on.
    /// </remarks>
    private static StoreConditionResponse Shown(InstallationHost? host, StoreFullness store) =>
        new(
            store.Blindness,
            host?.HostId,
            store.Blindness is Blindness.None ? store.HostName : null,
            host?.Mount.Value,
            store.Blindness is Blindness.None ? store.Percent : null,
            StoreFullness.FirstThreshold,
            StoreFullness.SecondThreshold);

    private static ToleratedSilenceResponse? Shown(ToleratedSilence? tolerance) =>
        tolerance is null
            ? null
            : new ToleratedSilenceResponse(
                tolerance.ProjectId, tolerance.Name, tolerance.ToleratedHours);

    private static IResult NotAMount() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["mount"] =
            [
                "A mount is one of the filesystems that host reports, and an "
                + $"absolute path of at most {MountPath.MaxLength} characters.",
            ],
        });

    private static IResult NotAServer() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["server"] =
            [
                "An ntfy server is an absolute http or https address, without a "
                + "query, a fragment or credentials in it.",
            ],
        });

    private static IResult NotATopic() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["topic"] =
            [
                "A topic is at most "
                + $"{Notifier.TopicMaxLength} letters, digits, hyphens and underscores.",
            ],
        });
}
