using Logaffe.Application.Operations;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Removes the entries that have outlived their project's window, and the
/// samples that have outlived the installation's.
/// </summary>
/// <remarks>
/// <para>
/// Hourly, though the window is measured in days. A daily pass would take a
/// whole day of a project in one burst on the largest table in the database,
/// and index churn under continuous insert-and-delete is the part of this
/// design ADR 0023 expects to need attention. An hourly pass takes a
/// twenty-fourth of that, and a pass with nothing to do costs one index probe
/// per project — an installation holds on the order of 10 to 30 of them.
/// </para>
/// <para>
/// It is its own timer rather than a second duty on the session sweep's. The
/// two are not the same shape: that one is a statement a day, and this one is
/// bounded portions paced against the entry table, so one timer for both would
/// make the interval of one the interval of the other and put them in the same
/// failure.
/// </para>
/// <para>
/// <b>The samples ride on this pass rather than a timer of their own</b>, which
/// is the opposite of the paragraph above and for a reason that does not
/// contradict it: the session sweep is a different shape of work, while this is
/// the same concern — a window, counted from receipt, swept in bounded portions.
/// The sample tables are three orders of magnitude smaller than the entries, so
/// their part of the pass is over before the entries' has warmed up, and a third
/// timer would be a third thing to reason about for it.
/// </para>
/// <para>
/// <b>The tally rides on it too</b>, for the same reason and with less of a case
/// to answer: it is one statement across a table of a few hundred thousand rows,
/// against a period of its own rather than any project's window (ADR 0047).
/// </para>
/// <para>
/// The samples and the tally go second and third because the entries are the
/// pass that matters: an hour that ran late has already spent its time where the
/// rows are.
/// </para>
/// <para>
/// Registered after the migrations, whose hosted service has finished before
/// this one is started: the first pass reads a table a migration may have been
/// about to create.
/// </para>
/// </remarks>
public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionService> logger,
    TimeProvider clock) : PeriodicService(scopeFactory, logger, clock)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override string Name => "retention";

    protected override async Task RunOnceAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<SweepExpiredEntries>()
            .ExecuteAsync(cancellationToken);

        await services.GetRequiredService<SweepExpiredSamples>()
            .ExecuteAsync(cancellationToken);

        await services.GetRequiredService<SweepExpiredTallies>()
            .ExecuteAsync(cancellationToken);
    }
}
