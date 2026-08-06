using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

public sealed class CheckReadinessTests
{
    [Fact]
    public async Task An_unreachable_database_is_not_ready()
    {
        var probe = new StubProbe { CanConnect = false };

        Assert.Equal(Readiness.Unreachable, await new CheckReadiness(probe).ExecuteAsync(TestContext.Current.CancellationToken));

        // Nothing asks a database it cannot reach about its migrations.
        Assert.False(probe.WasAskedAboutMigrations);
    }

    [Fact]
    public async Task A_reachable_database_with_pending_migrations_is_not_ready() =>
        // During a long migration on a large installation this is the honest
        // answer, since nothing can be served yet.
        Assert.Equal(
            Readiness.Migrating,
            await new CheckReadiness(new StubProbe { CanConnect = true, HasPending = true })
                .ExecuteAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task Reachable_and_current_is_ready() =>
        Assert.Equal(
            Readiness.Ready,
            await new CheckReadiness(new StubProbe { CanConnect = true, HasPending = false })
                .ExecuteAsync(TestContext.Current.CancellationToken));

    private sealed class StubProbe : IDatabaseProbe
    {
        public bool CanConnect { get; init; }

        public bool HasPending { get; init; }

        public bool WasAskedAboutMigrations { get; private set; }

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CanConnect);

        public Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            WasAskedAboutMigrations = true;
            return Task.FromResult(HasPending);
        }
    }
}
