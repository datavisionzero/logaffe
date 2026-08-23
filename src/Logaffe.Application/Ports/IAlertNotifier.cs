using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Ports;

/// <summary>How a notification the operator asked for ended.</summary>
/// <remarks>
/// It exists for the test send and for nothing else. A condition's alert is
/// composed by a background pass with nobody on the other end of anything, so
/// there is no one to answer — its failure is a line in the file log (ADR 0002)
/// and the whole of what happens next. The operator pressing the button is
/// standing there, and the answer is the point of the act.
/// </remarks>
public enum NotifierProof
{
    /// <summary>The notifier took it.</summary>
    Sent,

    /// <summary>
    /// This installation has no notifier, so there was nowhere to send it.
    /// </summary>
    NoNotifier,

    /// <summary>
    /// The server answered and said no: a token that is wrong or missing, or a
    /// topic this one is not allowed to publish to. The address is right and the
    /// credential is not.
    /// </summary>
    Refused,

    /// <summary>
    /// It could not be reached at all, or it answered with a fault of its own.
    /// A server that is not there, a name that does not resolve, a certificate
    /// that is not accepted, or a request that took too long.
    /// </summary>
    Unreachable,
}

/// <summary>
/// Where an alert goes: one notifier for the installation, answered outside
/// these layers by whatever the operator configured.
/// </summary>
/// <remarks>
/// <para>
/// It takes an <see cref="Alert"/> and nothing else, which is where ADR 0049
/// stops being a rule and becomes a shape: there is no overload carrying a
/// message, no second argument for context, and nothing an entry could be
/// threaded through. A notification is the one thing in this product that
/// travels outward on its own, to a service the operator does not run and this
/// project cannot harden, and log content is untrusted text.
/// </para>
/// <para>
/// <b>It does not throw and it does not queue.</b> A notifier that cannot be
/// reached costs one line in the installation's own file log (ADR 0002) and
/// nothing else — no retry, no second attempt on the next pass, and nothing that
/// arrives an hour later in a batch about things that are no longer true. That
/// is the contract rather than the adapter's habit: the pass that decides has
/// other projects to evaluate, and a throw would take them with it.
/// </para>
/// </remarks>
public interface IAlertNotifier
{
    Task SendAsync(Alert alert, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the operator's own notification — the shape a real alert has,
    /// belonging to no condition — and says how it went.
    /// </summary>
    /// <remarks>
    /// A notifier nobody has ever proved is a notifier that gets discovered
    /// broken on the night it was needed, and unlike everything else in this
    /// product a wrong value here fails silently by design, because a failed
    /// send is one line in a log file. This is the one place that silence is
    /// broken, so it answers rather than logging — and it throws no more than
    /// <see cref="SendAsync"/> does.
    /// </remarks>
    Task<NotifierProof> SendTestAsync(CancellationToken cancellationToken);
}
