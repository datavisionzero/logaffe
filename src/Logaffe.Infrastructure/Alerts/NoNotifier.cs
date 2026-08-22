using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure.Alerts;

/// <summary>
/// The notifier of an installation that has none: the alert is decided, its
/// silence is recorded, and one line goes into the installation's own log
/// instead of out of the installation.
/// </summary>
/// <remarks>
/// <para>
/// A condition that fires with nowhere to send it is a real state and not a
/// placeholder — an operator switches a condition on before they configure a
/// notifier, or clears the notifier afterwards — and the line is what makes that
/// legible instead of silent. It goes to the file log rather than into logaffe
/// itself (ADR 0002), like every other thing the installation says about its own
/// running.
/// </para>
/// <para>
/// <b>It carries what an alert carries and nothing more</b>: a name, a
/// condition and the numbers behind it. The rule about what leaves the
/// installation is a property of <see cref="Alert"/> rather than of whoever
/// renders it (ADR 0049), and that holds here too — there is nothing in hand
/// that could have come out of an entry.
/// </para>
/// </remarks>
public sealed class NoNotifier(ILogger<NoNotifier> logger) : IAlertNotifier
{
    public Task SendAsync(Alert alert, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "The {Condition} condition fired for {Subject} and this installation "
            + "has no notifier configured: {Alert}",
            alert.Condition,
            alert.SubjectName,
            alert);

        return Task.CompletedTask;
    }
}
