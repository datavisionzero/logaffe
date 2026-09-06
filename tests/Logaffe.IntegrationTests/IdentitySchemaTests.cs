using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The identity table, its sessions and its backup codes against a real
/// Postgres.
/// </summary>
/// <remarks>
/// Three things here are the database's own doing and can only be shown against
/// one: that two spellings of one address cannot both be written, which is the
/// last line under a normalization done above it (ADR 0052); that the check
/// constraints refuse an agent that administers or a user with no address; and
/// that removing an identity takes its sessions and codes with it, which is what
/// Host Recovery leans on (ADR 0058).
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class IdentitySchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Stands in for what the cipher produces from a TOTP secret. What makes
    /// these bytes unreadable is the key on the host volume, which is not this
    /// test's business.
    /// </summary>
    private static readonly byte[] Ciphertext = [1, 2, 3, 4];

    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$not-a-real-hash";

    private const string Address = "Somebody@Example.com";

    [Fact]
    public async Task A_user_round_trips_with_their_second_factor()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);

        Assert.True(await identities.TryAddAsync(
            Enrolled(), TestContext.Current.CancellationToken));

        await using var reader = ContextFor(connectionString);
        var stored = await new Identities(reader)
            .FindByEmailAsync("somebody@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(Address, stored.Email);
        Assert.Equal("somebody@example.com", stored.NormalizedEmail);
        Assert.Equal(Hash, stored.PasswordHash);
        Assert.Equal(UserState.Active, stored.State);
        Assert.True(stored.Administrator);
        Assert.Equal(Ciphertext, stored.EncryptedSecondFactorSecret);
        Assert.Equal(Now, stored.SecondFactorEnrolledAt);
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task An_installation_that_was_never_bootstrapped_holds_no_identity()
    {
        await using var context = await MigratedAsync();

        Assert.False(await new Identities(context).AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_spellings_of_one_address_cannot_both_be_written()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var first = await MigratedAsync(connectionString);
        Assert.True(await new Identities(first).TryAddAsync(
            Enrolled(), TestContext.Current.CancellationToken));

        // The unique index over the normalized form is the last line, under a
        // normalization that already happened above it (ADR 0052).
        await using var second = ContextFor(connectionString);
        var clash = User.Invite("Somebody Else", "SOMEBODY@example.COM", false, Now.AddDays(1));

        Assert.False(await new Identities(second).TryAddAsync(
            clash, TestContext.Current.CancellationToken));

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Identities(reader).ListUsersAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Address, stored.Email);
    }

    [Fact]
    public async Task An_agent_belongs_to_a_user_and_the_table_says_so()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);
        var owner = Enrolled();
        await identities.TryAddAsync(owner, TestContext.Current.CancellationToken);

        var agent = Agent.Create("the terminal agent", owner.Id, Now);
        Assert.True(await identities.TryAddAsync(agent, TestContext.Current.CancellationToken));

        await using var reader = ContextFor(connectionString);
        var stored = Assert.IsType<Agent>(
            await new Identities(reader).FindAsync(agent.Id, TestContext.Current.CancellationToken));

        Assert.Equal(IdentityKind.Agent, stored.Kind);
        Assert.Equal(owner.Id, stored.OwnerId);
        Assert.False(stored.Administrator);

        // Agents are not users: they carry none of a user's columns, and the
        // list of people does not have them in it.
        Assert.Single(
            await new Identities(reader).ListUsersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_active_administrators_are_counted()
    {
        await using var context = await MigratedAsync();
        var identities = new Identities(context);
        await identities.TryAddAsync(Enrolled(), TestContext.Current.CancellationToken);

        var second = User.Invite("Somebody Else", "else@example.com", true, Now);
        await identities.TryAddAsync(second, TestContext.Current.CancellationToken);

        // Invited counts as not yet arrived, which is what keeps "there is
        // always one active administrator" from being satisfied by somebody who
        // has never signed in (ADR 0052).
        Assert.Equal(
            1,
            await identities.CountActiveAdministratorsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_fresh_set_of_backup_codes_replaces_the_previous_one()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);
        var user = Enrolled();
        var first = BackupCode.MintSet(user.Id, Now);
        await identities.TryAddAsync(user, TestContext.Current.CancellationToken);
        await identities.ReplaceBackupCodesAsync(
            user.Id, first.Stored, TestContext.Current.CancellationToken);

        var second = BackupCode.MintSet(user.Id, Now.AddDays(1));
        await identities.ReplaceBackupCodesAsync(
            user.Id, second.Stored, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await new Identities(reader)
            .ListBackupCodesAsync(user.Id, TestContext.Current.CancellationToken);

        // Nothing of the previous set survives it (ADR 0057).
        Assert.Equal(BackupCode.SetSize, stored.Count);
        Assert.All(stored, code => Assert.False(code.Matches(first.Shown[0])));
        Assert.Contains(stored, code => code.Matches(second.Shown[0]));
    }

    [Fact]
    public async Task One_user_s_codes_are_not_another_s()
    {
        await using var context = await MigratedAsync();
        var identities = new Identities(context);
        var user = Enrolled();
        var other = User.Invite("Somebody Else", "else@example.com", false, Now);
        await identities.TryAddAsync(user, TestContext.Current.CancellationToken);
        await identities.TryAddAsync(other, TestContext.Current.CancellationToken);

        await identities.ReplaceBackupCodesAsync(
            user.Id, BackupCode.MintSet(user.Id, Now).Stored,
            TestContext.Current.CancellationToken);
        await identities.ReplaceBackupCodesAsync(
            other.Id, BackupCode.MintSet(other.Id, Now).Stored,
            TestContext.Current.CancellationToken);

        // Replacing one set leaves the other where it was, which is the whole of
        // what "wholesale" has to mean once there is more than one person.
        Assert.Equal(
            BackupCode.SetSize,
            (await identities.ListBackupCodesAsync(
                user.Id, TestContext.Current.CancellationToken)).Count);
        Assert.Equal(
            BackupCode.SetSize,
            (await identities.ListBackupCodesAsync(
                other.Id, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_spent_code_stays_visibly_spent()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);
        var user = Enrolled();
        var minted = BackupCode.MintSet(user.Id, Now);
        await identities.TryAddAsync(user, TestContext.Current.CancellationToken);
        await identities.ReplaceBackupCodesAsync(
            user.Id, minted.Stored, TestContext.Current.CancellationToken);

        var codes = await identities.ListBackupCodesAsync(
            user.Id, TestContext.Current.CancellationToken);
        var spent = codes.Single(code => code.Matches(minted.Shown[0]));
        spent.ConsumeAt(Now.AddHours(3));
        await identities.RecordConsumptionAsync(spent, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = await new Identities(reader)
            .ListBackupCodesAsync(user.Id, TestContext.Current.CancellationToken);

        // Consumed by a timestamp rather than by a deletion, so how many remain
        // is a filtered count (ADR 0057).
        Assert.Equal(BackupCode.SetSize, stored.Count);
        Assert.Equal(BackupCode.SetSize - 1, stored.Count(code => !code.IsSpent));
        Assert.Equal(Now.AddHours(3), stored.Single(code => code.IsSpent).UsedAt);
    }

    [Fact]
    public async Task A_session_round_trips_and_records_where_it_was_last_used()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var user = await AddedTo(context);
        var sessions = new Sessions(context);
        var secret = SessionSecret.Mint();
        var session = Session.Start(user.Id, secret, "203.0.113.7", Now);

        await sessions.AddAsync(session, TestContext.Current.CancellationToken);
        session.WasUsedAt(Now.AddDays(2), "198.51.100.4");
        await sessions.RecordUseAsync(session, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));

        // The row is the session: the browser holds the secret, the database
        // holds its hash, and nothing else connects them.
        Assert.True(stored.Matches(secret));
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal(Now, stored.StartedAt);
        Assert.Equal(Now.AddDays(2), stored.LastUsedAt);
        Assert.Equal("198.51.100.4", stored.LastSeenFrom);
    }

    [Fact]
    public async Task A_list_is_one_user_s_own_and_ending_all_others_reaches_no_further()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);
        var user = await AddedTo(context);
        var other = User.Invite("Somebody Else", "else@example.com", false, Now);
        await identities.TryAddAsync(other, TestContext.Current.CancellationToken);

        var sessions = new Sessions(context);
        var kept = Session.Start(user.Id, SessionSecret.Mint(), "203.0.113.7", Now);
        await sessions.AddAsync(kept, TestContext.Current.CancellationToken);
        await sessions.AddAsync(
            Session.Start(user.Id, SessionSecret.Mint(), "198.51.100.4", Now),
            TestContext.Current.CancellationToken);
        var theirs = Session.Start(other.Id, SessionSecret.Mint(), "192.0.2.9", Now);
        await sessions.AddAsync(theirs, TestContext.Current.CancellationToken);

        await sessions.RemoveEveryOtherAsync(kept, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var mine = Assert.Single(
            await new Sessions(reader).ListForAsync(
                user.Id, TestContext.Current.CancellationToken));
        Assert.Equal(kept.Id, mine.Id);

        // Somebody else's browser is not a thing this act reaches (ADR 0055).
        Assert.Single(await new Sessions(reader).ListForAsync(
            other.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_sweep_takes_both_deadlines()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var user = await AddedTo(context);
        var sessions = new Sessions(context);
        var live = Session.Start(user.Id, SessionSecret.Mint(), "203.0.113.7", Now);
        await sessions.AddAsync(live, TestContext.Current.CancellationToken);
        await sessions.AddAsync(
            Session.Start(user.Id, SessionSecret.Mint(), "198.51.100.4", Now.AddDays(-8)),
            TestContext.Current.CancellationToken);

        // Started a month ago and used this morning: the idle deadline says
        // nothing, and the absolute one is what takes it.
        var old = Session.Start(user.Id, SessionSecret.Mint(), "192.0.2.9", Now.AddDays(-31));
        old.WasUsedAt(Now.AddHours(-1), "192.0.2.9");
        await sessions.AddAsync(old, TestContext.Current.CancellationToken);

        await sessions.RemoveExpiredAsync(Now, TestContext.Current.CancellationToken);

        await using var reader = ContextFor(connectionString);
        var stored = Assert.Single(
            await new Sessions(reader).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(live.Id, stored.Id);
    }

    [Fact]
    public async Task Removing_every_identity_takes_the_sessions_and_the_codes()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = await MigratedAsync(connectionString);
        var identities = new Identities(context);
        var user = Enrolled();
        await identities.TryAddAsync(user, TestContext.Current.CancellationToken);
        await identities.TryAddAsync(
            Agent.Create("the terminal agent", user.Id, Now),
            TestContext.Current.CancellationToken);
        await identities.ReplaceBackupCodesAsync(
            user.Id, BackupCode.MintSet(user.Id, Now).Stored,
            TestContext.Current.CancellationToken);
        await new Sessions(context).AddAsync(
            Session.Start(user.Id, SessionSecret.Mint(), "203.0.113.7", Now),
            TestContext.Current.CancellationToken);

        var removed = await identities.RemoveEveryIdentityAsync(
            TestContext.Current.CancellationToken);

        // Host Recovery removes every identity, and the cascade is the
        // database's rather than a step the command has to remember (ADR 0058).
        Assert.Equal(2, removed);

        await using var reader = ContextFor(connectionString);
        Assert.False(await new Identities(reader).AnyAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.Sessions.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await reader.BackupCodes.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<User> AddedTo(LogaffeDbContext context)
    {
        var user = Enrolled();
        await new Identities(context).TryAddAsync(user, TestContext.Current.CancellationToken);

        return user;
    }

    /// <summary>
    /// An active administrator with a second factor. An account starts with none
    /// (ADR 0041); what these tests are about is the row once an enrolment has
    /// filled the other two columns in.
    /// </summary>
    private static User Enrolled()
    {
        var user = User.Bootstrap("The Administrator", Address, Now);
        user.ActivateWith(Hash);
        user.EnrolSecondFactor(Ciphertext, Now);

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
