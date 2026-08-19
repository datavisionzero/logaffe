using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Removes the samples that have outlived the installation's window, and the
/// samples of hosts that no longer exist.
/// </summary>
/// <remarks>
/// <para>
/// It runs on the retention job's pass rather than a timer of its own: it is the
/// same concern on the same clock, and it costs one statement per host against
/// tables three orders of magnitude smaller than the entries. A third timer
/// would be a third thing to reason about for a pass that is over before the
/// hour's entry work has warmed up.
/// </para>
/// <para>
/// <b>The window is the installation's, not a host's.</b> There is no reason to
/// keep one machine's numbers longer than another's, so this part of the pass
/// asks nothing about which host it is walking except its identity.
/// </para>
/// <para>
/// <b>It also takes what a deleted host left.</b> A host goes at once and its
/// samples follow in the background, and this is that background: the walk over
/// the hosts cannot reach them, because the row that named them is gone. They
/// are removed whole rather than by a window, which is the same thing said with
/// a window of nothing.
/// </para>
/// </remarks>
public sealed class SweepExpiredSamples(
    IHosts hosts, ISamples samples, IInstallation installation, TimeProvider clock)
{
    /// <summary>
    /// How many samples one statement removes.
    /// </summary>
    /// <remarks>
    /// Smaller than the entry sweep's portion and for the opposite reason: a
    /// day of one host is 1 440 rows, so a portion in the tens of thousands
    /// would be one statement that always takes everything and never says
    /// anything about how it is going.
    /// </remarks>
    public const int Portion = 5_000;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var window = await installation.ReadSampleRetentionAsync(cancellationToken);
        var receivedBefore = clock.GetUtcNow() - window.Duration;

        var live = await hosts.ListAsync(cancellationToken);

        foreach (var host in live)
        {
            await RemoveAsync(host.Id, receivedBefore, cancellationToken);
        }

        var known = live.Select(host => host.Id).ToHashSet();
        var abandoned = await samples.HostsWithSamplesAsync(cancellationToken);

        foreach (var hostId in abandoned.Where(id => !known.Contains(id)))
        {
            await RemoveAsync(hostId, DateTimeOffset.MaxValue, cancellationToken);
        }
    }

    private async Task RemoveAsync(
        Guid hostId, DateTimeOffset receivedBefore, CancellationToken cancellationToken)
    {
        int removed;
        do
        {
            removed = await samples.RemoveReceivedBeforeAsync(
                hostId, receivedBefore, Portion, cancellationToken);
        }
        while (removed == Portion);
    }
}
