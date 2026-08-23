using Logaffe.Domain.Alerts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure.Alerts;

/// <summary>
/// Where a notification points, built from the address the deployment says this
/// installation is reachable at.
/// </summary>
/// <remarks>
/// <para>
/// The link is the better half of an alert (ADR 0049): what the operator wants
/// at three in the morning is the view with its filters and the band above it,
/// not a line of what a service said. So it lands where the alert is legible —
/// the flooding hour on that project, that hour's errors for the one about
/// failing, the machine's own screen for the disk — rather than at the front
/// door.
/// </para>
/// <para>
/// <b>It is deployment configuration, and it is the only place in this product
/// that needs to be.</b> Everywhere else the address comes free from
/// <c>X-Forwarded-Host</c> and <c>X-Forwarded-Proto</c>, because the delivery
/// snippet and an agent's configuration are composed inside a request. An alert
/// has no request behind it, and remembering the address off whatever last
/// called would make the link one an outsider could choose by sending a header —
/// arriving in a notification the operator trusts and taps.
/// </para>
/// <para>
/// <b>An installation that has not been told sends the alert without a link</b>,
/// rather than with a link to a container port, and a value that is not an
/// address is the same case rather than a start that fails: an alert with no
/// link is worth having, and the sentence saying so is in the file log where the
/// operator's other startup complaints are.
/// </para>
/// </remarks>
public sealed class AlertLinks(Uri? publicAddress)
{
    /// <summary>What the compose file maps <c>LOGAFFE_PUBLIC_URL</c> into.</summary>
    public const string PublicUrlKey = "Logaffe:PublicUrl";

    private readonly Uri? _address = publicAddress;

    /// <summary>
    /// The address as configured, complaining once about a value that is not
    /// one.
    /// </summary>
    public static AlertLinks From(IConfiguration configuration, ILogger<AlertLinks> logger)
    {
        var configured = configuration[PublicUrlKey]?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return new AlertLinks(null);
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning(
                "{Key} is set to something that is not an absolute http or https address, "
                + "so alerts will be sent without a link. See docs/deployment.md.",
                PublicUrlKey);

            return new AlertLinks(null);
        }

        // A trailing slash, so that a path under a proxy survives having a
        // route appended to it.
        return new AlertLinks(
            address.AbsolutePath.EndsWith('/') ? address : new Uri(address + "/"));
    }

    /// <summary>The installation itself, which is where a test notification points.</summary>
    public Uri? Home => _address;

    /// <summary>
    /// Where this alert lands, or <c>null</c> on an installation that has not
    /// been told its address.
    /// </summary>
    public Uri? For(Alert alert)
    {
        if (_address is null)
        {
            return null;
        }

        return alert switch
        {
            // The machine's own screen, where the filesystem readings the
            // condition was decided on are drawn.
            Alert.StoreFillingUp filling => At($"settings/hosts/{filling.HostId}"),

            // A span wide enough to hold the silence and what came before it: an
            // hour of an empty project is a screen that says nothing, and what
            // the operator is looking for is the last thing that arrived.
            Alert.ProjectGoneQuiet quiet => At(
                $"project/{quiet.ProjectId}?range={(quiet.Hours < 23 ? "1d" : "1w")}"),

            // The hour it fired on, exactly (`docs/alerts.md`).
            Alert.ProjectFlooding flood => At(
                $"project/{flood.ProjectId}"
                + $"?from={Instant(flood.Hour)}&until={Instant(flood.Hour.AddHours(1))}"),

            // The same hour, narrowed to what the condition actually counted.
            // The level is a threshold rather than a selection
            // (`docs/querying.md`), so `Error` is Error and Fatal — which is the
            // tally's second number exactly, and therefore the entries behind
            // the figure in the notification rather than a wider view of them.
            Alert.ProjectFailing failing => At(
                $"project/{failing.ProjectId}"
                + $"?from={Instant(failing.Hour)}&until={Instant(failing.Hour.AddHours(1))}"
                + "&minimumLevel=Error"),

            _ => null,
        };
    }

    private static string Instant(DateTimeOffset at) =>
        Uri.EscapeDataString(at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

    private Uri At(string route) => new(_address!, route);
}
