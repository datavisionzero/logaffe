using Logaffe.Application.Operations;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Removes the sessions that went thirty days untouched.
/// </summary>
/// <remarks>
/// <para>
/// Once a day, because that is the resolution the thing being swept has: a
/// session's deadline is thirty days out and moves forward on every use, so a
/// row removed a few hours after it expired is removed at exactly the moment it
/// matters — which is never, since an expired session already admits nothing.
/// </para>
/// <para>
/// It is registered after the migrations, whose hosted service has finished
/// before this one is started: the first pass reads a table that a migration may
/// have been about to create.
/// </para>
/// </remarks>
public sealed class ExpiredSessionService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredSessionService> logger,
    TimeProvider clock) : PeriodicService(scopeFactory, logger, clock)
{
    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override string Name => "expired session";

    protected override Task RunOnceAsync(
        IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<RemoveExpiredSessions>().ExecuteAsync(cancellationToken);
}
