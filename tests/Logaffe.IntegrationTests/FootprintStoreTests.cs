using Logaffe.Domain.Hosts;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Host = Logaffe.Domain.Hosts.Host;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The two things the footprint reads that only a database can answer: what the
/// store says it occupies, and the machine an installation names itself onto.
/// </summary>
/// <remarks>
/// The size is a catalogue function rather than arithmetic, so what is asked
/// here is that it is the current database and a real number. The relation is a
/// column with a set-null on it, and what is asked of that is the behaviour the
/// schema is carrying rather than an act: deleting the machine has to leave the
/// installation on none rather than being refused.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class FootprintStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task What_the_installation_holds_is_a_real_number()
    {
        await using var context = await MigratedAsync();

        var held = await new StoreFootprint(context)
            .HeldBytesAsync(TestContext.Current.CancellationToken);

        // A fresh installation is a schema and its catalogue, which is
        // megabytes rather than nothing — and the point of the number is that it
        // is the disk's answer and not a sum of rows.
        Assert.True(held > 0, $"the store said it holds {held} bytes");
    }

    [Fact]
    public async Task An_installation_names_no_host_until_it_is_told_one()
    {
        await using var context = await MigratedAsync();

        // Every installation, until the operator decides they want the disk
        // read. It is not a degraded state and nothing is written to say so.
        Assert.Null(await new Installation(context)
            .ReadHostAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_machine_and_the_mount_are_written_and_read_as_a_pair()
    {
        await using var context = await MigratedAsync();
        var host = await HostAsync(context, "db");
        var installation = new Installation(context);

        await installation.RecordHostAsync(
            new InstallationHost(host, MountPath.Create("/var/lib/postgresql")),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new InstallationHost(host, MountPath.Create("/var/lib/postgresql")),
            await installation.ReadHostAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Naming_a_host_leaves_the_sample_window_where_it_was()
    {
        await using var context = await MigratedAsync();
        var host = await HostAsync(context, "db");
        var installation = new Installation(context);

        var before = await installation.ReadSampleRetentionAsync(
            TestContext.Current.CancellationToken);
        await installation.RecordHostAsync(
            new InstallationHost(host, MountPath.Create("/")),
            TestContext.Current.CancellationToken);

        // The two settings share one row, and writing the row for the first time
        // must not quietly decide the other one.
        Assert.Equal(
            before,
            await installation.ReadSampleRetentionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_the_machine_leaves_the_installation_on_none()
    {
        await using var context = await MigratedAsync();
        var host = await HostAsync(context, "db");
        var installation = new Installation(context);

        await installation.RecordHostAsync(
            new InstallationHost(host, MountPath.Create("/")),
            TestContext.Current.CancellationToken);

        await new Hosts(context).RemoveAsync(
            await context.Hosts.SingleAsync(
                h => h.Id == host, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        // The set-null, not an act remembering to do it: deleting a machine is
        // never refused by this row, and what is left behind names nothing.
        context.ChangeTracker.Clear();
        Assert.Null(await installation.ReadHostAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Naming_none_clears_the_machine_that_was_named()
    {
        await using var context = await MigratedAsync();
        var host = await HostAsync(context, "db");
        var installation = new Installation(context);

        await installation.RecordHostAsync(
            new InstallationHost(host, MountPath.Create("/")),
            TestContext.Current.CancellationToken);
        await installation.RecordHostAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(await installation.ReadHostAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> HostAsync(LogaffeDbContext context, string name)
    {
        var host = Host.Create(name, Now.AddDays(-1));
        context.Hosts.Add(host);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return host.Id;
    }

    private async Task<LogaffeDbContext> MigratedAsync()
    {
        var context = new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>()
                .UseNpgsql(await postgres.CreateDatabaseAsync())
                .Options);

        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        return context;
    }
}
