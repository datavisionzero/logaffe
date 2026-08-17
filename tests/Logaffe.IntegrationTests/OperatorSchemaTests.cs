using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The account, its sessions and its backup codes against a real Postgres.
/// </summary>
/// <remarks>
/// Two things here are the database's own doing and can only be shown against
/// one: that a second account cannot be written, which is what makes the claim
/// atomic (ADR 0014), and that removing the account takes its sessions and codes
/// with it, which is what Host Recovery leans on (ADR 0013).
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class OperatorSchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Stands in for what the cipher produces from a TOTP secret. What makes
    /// these bytes unreadable is the key on the host volume, which is not this
    /// test's business.
    /// </summary>
    private static readonly byte[] Ciphertext = [1, 2, 3, 4];

    private const string Hash = "AQAAAAIAAYagAAAAE-not-a-real-hash";

    [Fact]
    public async Task An_operator_round_trips_with_their_second_factor()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var operators = new Operators(context);

        var claimed = await operators.TryClaimAsync(
            Enrolled(), TestContext.Current.CancellationToken);

        Assert.True(claimed);

        await using var reader = ContextFor(connectionString);
        var stored = await new Operators(reader).FindAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(Hash, stored.PasswordHash);
        Assert.Equal(Ciphertext, stored.EncryptedSecondFactorSecret);
        Assert.Equal(Now, stored.SecondFactorEnrolledAt);
        Assert.Equal(Now, stored.ClaimedAt);
    }

    [Fact]
    public async Task An_unclaimed_installation_holds_no_account()
    {
        await using var context = await MigratedAsync();
        var operators = new Operators(context);

        Assert.False(await operators.IsClaimedAsync(TestContext.Current.CancellationToken));
        Assert.Null(await operators.FindAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_second_claimant_loses_and_writes_nothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var winner = await MigratedAsync(connectionString);
        var theOperator = Enrolled();
        Assert.True(await new Operators(winner).TryClaimAsync(
            theOperator, TestContext.Current.CancellationToken));

        // The loser walked the whole flow and fails at the last step, which is
        // the price of the claim holding nothing until then (ADR 0014).
        await using var loser = ContextFor(connectionString);
        var second = Operator.Claim("AQAAAAIAAYagAAAAE-someone-else", Now.AddMinutes(1));
        var claimed = await new Operators(loser).TryClaimAsync(
            second, TestContext.Current.CancellationToken);

        Assert.False(claimed);

        await using var reader = ContextFor(connectionString);
        var stored = await reader.Operators.SingleAsync(TestContext.Current.CancellationToken);

        // Not a row of the loser's survives, and the account that is there is
        // the winner's.
        Assert.Equal(Hash, stored.PasswordHash);
    }

    [Fact]
    public async Task A_fresh_set_of_backup_codes_replaces_the_previous_one()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var operators = new Operators(context);
        var theOperator = Enrolled();
        var first = BackupCode.MintSet(theOperator.Id, Now);
        await operators.TryClaimAsync(theOperator, TestContext.Current.CancellationToken);
        await operators.ReplaceBackupCodesAsync(
            first.Stored, TestContext.Current.CancellationToken);

        var second = BackupCode.MintSet(theOperator.Id, Now.AddDays(1));
        await operators.ReplaceBackupCodesAsync(
            second.Stored, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await new Operators(reader)
            .ListBackupCodesAsync(TestContext.Current.CancellationToken);

        // Nothing of the previous set survives it (ADR 0032).
        Assert.Equal(BackupCode.SetSize, stored.Count);
        Assert.All(stored, code => Assert.False(code.Matches(first.Shown[0])));
        Assert.Contains(stored, code => code.Matches(second.Shown[0]));
    }

    [Fact]
    public async Task A_spent_code_stays_visibly_spent()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var operators = new Operators(context);
        var theOperator = Enrolled();
        var minted = BackupCode.MintSet(theOperator.Id, Now);
        await operators.TryClaimAsync(theOperator, TestContext.Current.CancellationToken);
        await operators.ReplaceBackupCodesAsync(
            minted.Stored, TestContext.Current.CancellationToken);

        var codes = await operators.ListBackupCodesAsync(TestContext.Current.CancellationToken);
        var spent = codes.Single(code => code.Matches(minted.Shown[0]));
        spent.ConsumeAt(Now.AddHours(3));
        await operators.RecordConsumptionAsync(spent, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await new Operators(reader)
            .ListBackupCodesAsync(TestContext.Current.CancellationToken);

        // Consumed by a timestamp rather than by a deletion, so how many remain
        // is a filtered count (ADR 0032).
        Assert.Equal(BackupCode.SetSize, stored.Count);
        Assert.Equal(BackupCode.SetSize - 1, stored.Count(code => !code.IsSpent));
        Assert.Equal(Now.AddHours(3), stored.Single(code => code.IsSpent).UsedAt);
    }

    [Fact]
    public async Task A_session_round_trips_and_records_where_it_was_last_used()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var theOperator = await ClaimedInto(context);
        var sessions = new Sessions(context);
        var secret = SessionSecret.Mint();
        var session = Session.Start(theOperator.Id, secret, "203.0.113.7", Now);

        await sessions.AddAsync(session, TestContext.Current.CancellationToken);
        session.WasUsedAt(Now.AddDays(2), "198.51.100.4");
        await sessions.RecordUseAsync(session, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));

        // The row is the session: the browser holds the secret, the database
        // holds its hash, and nothing else connects them.
        Assert.True(stored.Matches(secret));
        Assert.Equal(Now, stored.StartedAt);
        Assert.Equal(Now.AddDays(2), stored.LastUsedAt);
        Assert.Equal("198.51.100.4", stored.LastSeenFrom);
    }

    [Fact]
    public async Task Ending_all_others_leaves_the_one_that_asked()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var theOperator = await ClaimedInto(context);
        var sessions = new Sessions(context);
        var kept = Session.Start(theOperator.Id, SessionSecret.Mint(), "203.0.113.7", Now);
        await sessions.AddAsync(kept, TestContext.Current.CancellationToken);
        await sessions.AddAsync(
            Session.Start(theOperator.Id, SessionSecret.Mint(), "198.51.100.4", Now),
            TestContext.Current.CancellationToken);

        await sessions.RemoveEveryOtherAsync(kept, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(kept.Id, stored.Id);
    }

    [Fact]
    public async Task A_session_nobody_touched_for_thirty_days_is_swept_up()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var theOperator = await ClaimedInto(context);
        var sessions = new Sessions(context);
        var live = Session.Start(theOperator.Id, SessionSecret.Mint(), "203.0.113.7", Now);
        await sessions.AddAsync(live, TestContext.Current.CancellationToken);
        await sessions.AddAsync(
            Session.Start(
                theOperator.Id, SessionSecret.Mint(), "198.51.100.4", Now.AddDays(-31)),
            TestContext.Current.CancellationToken);

        await sessions.RemoveExpiredAsync(Now, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(live.Id, stored.Id);
    }

    [Fact]
    public async Task Removing_the_account_takes_the_sessions_and_the_codes()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var operators = new Operators(context);
        var theOperator = Enrolled();
        await operators.TryClaimAsync(theOperator, TestContext.Current.CancellationToken);
        await operators.ReplaceBackupCodesAsync(
            BackupCode.MintSet(theOperator.Id, Now).Stored,
            TestContext.Current.CancellationToken);
        await new Sessions(context).AddAsync(
            Session.Start(theOperator.Id, SessionSecret.Mint(), "203.0.113.7", Now),
            TestContext.Current.CancellationToken);

        await operators.RemoveAsync(theOperator, TestContext.Current.CancellationToken);

        // Host Recovery returns the installation to unclaimed rather than
        // resetting anything, and the cascade is the database's rather than a
        // step the command has to remember (ADR 0013).
        await using var reader = ContextFor(connectionString);
        Assert.False(await new Operators(reader).IsClaimedAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.BackupCodes.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Operator> ClaimedInto(LogaffeDbContext context)
    {
        var theOperator = Enrolled();
        await new Operators(context).TryClaimAsync(
            theOperator, TestContext.Current.CancellationToken);

        return theOperator;
    }

    /// <summary>
    /// An account with a second factor. The claim writes the password alone
    /// (ADR 0041); what these tests are about is the row once an enrolment has
    /// filled the other two columns in.
    /// </summary>
    private static Operator Enrolled()
    {
        var theOperator = Operator.Claim(Hash, Now);
        theOperator.EnrolSecondFactor(Ciphertext, Now);

        return theOperator;
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
