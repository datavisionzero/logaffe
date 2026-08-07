using System.Net;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The retention sweep, asked of the composition root that is supposed to run
/// it.
/// </summary>
/// <remarks>
/// What the sweep decides and what its statements take are asked elsewhere. What
/// is here is the one thing no registration can be read for: that the job is
/// started, that it reaches the act and the store through the scope it makes for
/// itself, and that its first pass happens on start rather than an hour later.
/// A pass that throws is logged and retried rather than ended
/// (<c>PeriodicService</c>), so a chain that does not resolve would be silent —
/// which is precisely why it is asked of a running installation.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class RetentionServiceTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    public void Dispose() => Directory.Delete(_volume, recursive: true);

    [Fact]
    public async Task Starting_the_installation_removes_what_fell_outside_its_window()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await SeedAsync(connectionString);

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        await using (var installation = new WebApplicationFactory<Program>())
        {
            using var client = installation.CreateClient();
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/health", TestContext.Current.CancellationToken))
                    .StatusCode);

            await WaitForTheSweepAsync(connectionString);
        }

        // And it took nothing else: the second entry is a day inside the same
        // seven-day window, and an installation that swept it would be losing
        // data on a timer.
        await using var connection = await OpenAsync(connectionString);
        Assert.Equal([2L], await IdsAsync(connection));
    }

    /// <summary>
    /// A project with a seven-day window, one entry eight days old and one a day
    /// old, written before the installation is up.
    /// </summary>
    private async Task SeedAsync(string connectionString)
    {
        var project = Project.Create("api", RetentionWindow.OfDays(7), Now.AddDays(-30));

        await using var context = new LogaffeDbContext(
            new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenAsync(connectionString);
        await StoreAsync(connection, 1, project.Id, Now.AddDays(-8));
        await StoreAsync(connection, 2, project.Id, Now.AddDays(-1));
    }

    private static async Task WaitForTheSweepAsync(string connectionString)
    {
        // The job is a background service, so the start of the installation is
        // not the end of the pass. Ten seconds is a long time for two statements
        // and short enough to fail rather than hang.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var connection = await OpenAsync(connectionString);
            if (!(await IdsAsync(connection)).Contains(1L))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The expired entry was still there ten seconds after the start.");
    }

    private static async Task StoreAsync(
        NpgsqlConnection connection, long id, Guid projectId, DateTimeOffset receivedAt)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into log_entry (
                id, project_id, event_time, receipt_time, level,
                message_template, rendered_message, message_truncated, exception_truncated)
            values (@id, @project_id, @at, @at, @level, @text, @text, false, false)
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("at", receivedAt);
        command.Parameters.AddWithValue("level", (short)Level.Information);
        command.Parameters.AddWithValue("text", "Handled /orders");

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<long>> IdsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "select id from log_entry order by id", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var ids = new List<long>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
