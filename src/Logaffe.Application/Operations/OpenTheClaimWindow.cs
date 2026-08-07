using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// Writes the installation's first run, which is the instant the claim window
/// hangs off.
/// </summary>
/// <remarks>
/// It runs on every start and writes on exactly one of them. That is what makes
/// a restart not extend the window (<c>docs/setup.md</c>): the deadline belongs
/// to the installation rather than to the process, so nobody gains anything by
/// forcing one. Which start does the writing is the store's to decide, and it
/// decides it the way the claim itself is decided — by the row that is already
/// there (ADR 0034).
/// </remarks>
public sealed class OpenTheClaimWindow(IInstallation installation, TimeProvider clock)
{
    /// <summary>The window as it stands after this start, whoever wrote it.</summary>
    public Task<ClaimWindow> ExecuteAsync(CancellationToken cancellationToken) =>
        installation.OpenClaimWindowAsync(clock.GetUtcNow(), cancellationToken);
}
