using Logaffe.Application.Operations;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Removes the entries that have outlived their project's window.
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

    protected override Task RunOnceAsync(
        IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<SweepExpiredEntries>().ExecuteAsync(cancellationToken);
}
