using System.Net;
using System.Net.Http.Json;
using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The sweep that removes sessions gone thirty days untouched, asked of the
/// composition root that is supposed to run it.
/// </summary>
/// <remarks>
/// What the act does is a unit test's business. What is here is the one thing no
/// registration can be read for: that the job is actually started, and that its
/// first pass happens on start rather than an interval later — an installation
/// brought up after a long stop cleans up without waiting a day for permission.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ExpiredSessionSweepTests(PostgresFixture postgres) : IDisposable
{
    private const string TheirPassword = "a passphrase they typed";

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-volume-").FullName;

    public void Dispose() => Directory.Delete(_volume, recursive: true);

    [Fact]
    public async Task Starting_the_installation_removes_what_went_thirty_days_untouched()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        Environment.SetEnvironmentVariable("Logaffe__VolumePath", _volume);

        Guid expired;
        Guid live;

        await using (var installation = new WebApplicationFactory<Program>())
        {
            live = await ClaimAsync(installation, connectionString);
            expired = await SeedExpiredSessionAsync(connectionString);
        }

        // The next start of the same installation, which is where the pass runs.
        await using (var installation = new WebApplicationFactory<Program>())
        {
            using var client = installation.CreateClient();
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/health", TestContext.Current.CancellationToken))
                    .StatusCode);

            await WaitForTheSweepAsync(connectionString, expired);
        }

        // And it took nothing else with it: the session the claim handed out is
        // a day old, not thirty.
        await using var context = ContextFor(connectionString);
        Assert.Equal(
            [live],
            await context.Sessions.Select(session => session.Id)
                .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The row the sweep is meant to find: a session whose last use is older
    /// than the sliding lifetime, written straight into the table because no act
    /// can produce one.
    /// </summary>
    private static async Task<Guid> SeedExpiredSessionAsync(string connectionString)
    {
        await using var context = ContextFor(connectionString);

        var theOperator = await context.Operators.SingleAsync(
            TestContext.Current.CancellationToken);

        var expired = Session.Start(
            theOperator.Id,
            SessionSecret.Mint(),
            "203.0.113.7",
            DateTimeOffset.UtcNow - Session.SlidingLifetime - TimeSpan.FromDays(1));

        context.Sessions.Add(expired);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return expired.Id;
    }

    private static async Task WaitForTheSweepAsync(string connectionString, Guid expired)
    {
        // The job is a background service, so the start of the installation is
        // not the end of the pass. Ten seconds is a long time for one statement
        // and short enough to fail rather than hang.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var context = ContextFor(connectionString);

            if (!await context.Sessions.AnyAsync(
                session => session.Id == expired, TestContext.Current.CancellationToken))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The expired session was still there ten seconds after the start.");
    }

    /// <summary>Claims the installation, and answers the session that left.</summary>
    private static async Task<Guid> ClaimAsync(
        WebApplicationFactory<Program> installation, string connectionString)
    {
        using var client = installation.CreateClient();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);

        var enrolment = await ReadAsync<Enrolment>(await client.PostAsync(
            "/claim/enrolment", null, TestContext.Current.CancellationToken));

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                ticket = enrolment.Ticket,
                secondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                backupCode = enrolment.BackupCodes[0],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);

        await using var context = ContextFor(connectionString);

        return (await context.Sessions.SingleAsync(TestContext.Current.CancellationToken)).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode);

            return (await response.Content.ReadFromJsonAsync<T>(
                TestContext.Current.CancellationToken))!;
        }
    }

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private sealed record Enrolment(
        string SecondFactorSecret,
        string EnrolmentUri,
        IReadOnlyList<string> BackupCodes,
        string Ticket);
}
