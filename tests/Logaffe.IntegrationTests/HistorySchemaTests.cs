using Logaffe.Domain.History;
using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The history table against a real Postgres.
/// </summary>
/// <remarks>
/// Three things about it are the database's own doing. The key is a bigint it
/// assigns, and the order it assigns them in is the order the page is read and
/// resumed in — a cursor over ids nothing here chooses. A row outlives the thing
/// it names, because there is no foreign key on the subject. And it does not
/// outlive the person, because there is one on the actor: Host Recovery removes
/// every identity (ADR 0058) and the rows go with them.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class HistorySchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_page_is_newest_first_and_resumes_where_the_last_one_ended()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var who = await AnAdministratorIn(context);
        var history = new Histories(context);

        foreach (var name in new[] { "first", "second", "third" })
        {
            await history.RecordAsync(
                Change.Of(
                    who, Now, Subject.Project, Guid.CreateVersion7(), name, Act.Created),
                TestContext.Current.CancellationToken);
        }

        await using var reader = ContextFor(connectionString);
        var stored = new Histories(reader);

        var page = await stored.ListAsync(before: null, 2, TestContext.Current.CancellationToken);
        Assert.Equal(["third", "second"], page.Select(change => change.SubjectName));

        // The three were written inside one second, so nothing but the key can
        // put them in order — which is why the cursor is the key.
        var rest = await stored.ListAsync(page[^1].Id, 2, TestContext.Current.CancellationToken);
        Assert.Equal(["first"], rest.Select(change => change.SubjectName));
    }

    [Fact]
    public async Task A_row_outlives_the_thing_it_names()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var who = await AnAdministratorIn(context);

        // A project that never existed stands in for one that has been deleted:
        // *who deleted orders-api* is the question the table exists to answer,
        // and a foreign key would take the answer away with the project.
        await new Histories(context).RecordAsync(
            Change.Of(
                who, Now, Subject.Project, Guid.CreateVersion7(), "orders-api", Act.Removed),
            TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Histories(reader).ListAsync(
                before: null, 100, TestContext.Current.CancellationToken));

        Assert.Equal("orders-api", stored.SubjectName);
        Assert.Equal(Act.Removed, stored.Act);
    }

    [Fact]
    public async Task Removing_every_identity_takes_the_history_with_it()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var who = await AnAdministratorIn(context);

        await new Histories(context).RecordAsync(
            Change.Of(
                who, Now, Subject.Project, Guid.CreateVersion7(), "orders-api", Act.Created),
            TestContext.Current.CancellationToken);

        await new Identities(context).RemoveEveryIdentityAsync(
            TestContext.Current.CancellationToken);

        // What Host Recovery leaves is an installation with nobody in it, and a
        // history naming people who are gone would be a list nobody can read.
        await using var reader = ContextFor(connectionString);
        Assert.Empty(await reader.History.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<User> AnAdministratorIn(LogaffeDbContext context)
    {
        var user = User.Bootstrap("The Administrator", "administrator@example.com", Now);
        user.ActivateWith("$argon2id$v=19$m=19456,t=2,p=1$not-a-real-hash");
        await new Identities(context).TryAddAsync(user, TestContext.Current.CancellationToken);

        return user;
    }

    private async Task<LogaffeDbContext> MigratedAsync(string? connectionString = null)
    {
        var context = ContextFor(connectionString ?? await postgres.CreateDatabaseAsync());
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return context;
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);
}
