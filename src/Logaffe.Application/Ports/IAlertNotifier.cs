using Logaffe.Domain.Alerts;

namespace Logaffe.Application.Ports;

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
}
