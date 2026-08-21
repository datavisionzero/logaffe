using Logaffe.Application.Operations;
using Logaffe.Domain.Projects;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Writes down what the ingestion path has counted since the last minute.
/// </summary>
/// <remarks>
/// <para>
/// A minute, because that is what a crash may cost and because the whole point
/// of counting in memory is that the delivery path does not write (ADR 0047). A
/// pass with nothing to write does not reach the database at all, which is the
/// ordinary state of a quiet installation.
/// </para>
/// <para>
/// Its own timer rather than a duty on the retention pass, which runs hourly:
/// one timer for both would make the tally an hour behind, and an hour behind is
/// the thing this was built to avoid — the row for the hour in progress would
/// only ever appear once it had stopped being the hour in progress.
/// </para>
/// <para>
/// Registered after the migrations, whose hosted service has finished before
/// this one starts: the first pass writes a table a migration may have been
/// about to create. A pass that throws hands its counts back to the counter and
/// the next minute is the retry.
/// </para>
/// </remarks>
public sealed class TallyService(
    IServiceScopeFactory scopeFactory,
    ILogger<TallyService> logger,
    TimeProvider clock) : PeriodicService(scopeFactory, logger, clock)
{
    protected override TimeSpan Interval => Tallying.FlushInterval;

    protected override string Name => "tally";

    protected override Task RunOnceAsync(
        IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<FlushTheTally>().ExecuteAsync(cancellationToken);
}
