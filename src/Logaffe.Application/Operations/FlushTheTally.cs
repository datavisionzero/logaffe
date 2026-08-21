using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Writes down what the ingestion path has counted since the last time, and
/// starts the counter again from nothing.
/// </summary>
/// <remarks>
/// <para>
/// Once a minute (<see cref="Domain.Projects.Tallying.FlushInterval"/>), which
/// is what makes the tally one small write a minute instead of a write per batch
/// on the hottest path in the product. A minute is also the whole of what a
/// crash costs.
/// </para>
/// <para>
/// A pass with nothing to write does not touch the database at all, which is the
/// ordinary state of an installation whose projects are quiet.
/// </para>
/// <para>
/// A pass that throws is not this act's problem. <c>PeriodicService</c> logs it
/// and the next minute is the retry — and because the write is one transaction,
/// what it threw on stored nothing, so the counts go back to where they came
/// from rather than being lost the way a restart loses them.
/// </para>
/// </remarks>
public sealed class FlushTheTally(RunningTally running, ITallies tallies)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var increments = running.Take();
        if (increments.Count == 0)
        {
            return;
        }

        try
        {
            await tallies.AddAsync(increments, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Cancellation is excluded on purpose: it is the installation
            // stopping, and there is no next pass to hand these to.
            running.PutBack(increments);
            throw;
        }
    }
}
