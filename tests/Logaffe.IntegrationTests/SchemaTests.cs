using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The schema against a real Postgres. EF Core owning the migrations is what
/// lets an upgrade be a pull and an up with no step for the operator to run, so
/// the thing worth proving is that they apply — and that applying them twice is
/// uneventful, because two containers starting at once is an ordinary event.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = ContextFor(connectionString))
        {
            Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = ContextFor(connectionString))
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Applying_twice_finds_nothing_to_do()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var first = ContextFor(connectionString);
        await MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);

        await using var second = ContextFor(connectionString);
        await MigratorFor(second).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await second.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_project_round_trips_with_its_retention_window()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        var project = Project.Create("orders-api", RetentionWindow.OfDays(14), Now);
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await reader.Projects.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(project.Id, stored.Id);
        Assert.Equal("orders-api", stored.Name);
        // The window is an int in the column and a window again on the way out.
        Assert.Equal(RetentionWindow.OfDays(14), stored.Retention);
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task Two_projects_cannot_share_a_name()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        context.Projects.Add(Project.Create("api", RetentionWindow.OfDays(7), Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Projects.Add(Project.Create("api", RetentionWindow.OfDays(7), Now));

        // Two projects called `api` is a trap for the operator who reaches for
        // one of them at three in the morning.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static SchemaMigrator MigratorFor(LogaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);
}
