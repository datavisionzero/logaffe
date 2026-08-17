using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The door, over the two stores it actually runs on: the rows in Postgres and
/// the key on the host volume.
/// </summary>
/// <remarks>
/// What no substitute can vouch for is here — that the second factor is checked
/// against a secret the real cipher opened out of a real row, that a real PBKDF2
/// hash written by a claim is what a sign-in proves against, that a spent backup
/// code is still spent for the next request, and that a session started in one
/// request is found by another that never saw it.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SignInActsTests(PostgresFixture postgres) : IDisposable
{
    private const string TheirPassword = "a passphrase they typed";

    /// <summary>
    /// RFC 6238's own SHA-1 secret, in the base32 an authenticator app is
    /// enrolled with. The twenty bytes it stands for are known as well, which is
    /// what lets <see cref="CodeAt"/> arrive at the six digits from the
    /// specification rather than from the adapter under test.
    /// </summary>
    private const string SecondFactorSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly byte[] SecondFactorKey =
        Encoding.UTF8.GetBytes("12345678901234567890");

    private static readonly DateTimeOffset Claimed = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string _volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;

    public void Dispose() => Directory.Delete(_volume, recursive: true);

    [Fact]
    public async Task The_password_and_the_second_factor_start_a_session_another_request_finds()
    {
        var installation = await ClaimedInstallationAsync();

        var signedIn = await SignInAsync(installation, TheirPassword, CodeAt(Claimed), null);

        Assert.NotNull(signedIn);

        // A separate request, as the browser's next one would be, carrying
        // nothing over but the secret that went into the cookie.
        var admitted = await AuthenticateAsync(installation, signedIn.Secret.Text, Claimed);

        Assert.NotNull(admitted);
        Assert.Equal(signedIn.Session.Id, admitted.Session.Id);
    }

    [Fact]
    public async Task A_wrong_password_admits_nothing_against_a_real_hash()
    {
        var installation = await ClaimedInstallationAsync();

        Assert.Null(await SignInAsync(
            installation, "some other passphrase", CodeAt(Claimed), null));

        await using var context = ContextFor(installation);
        Assert.Empty(await context.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_right_password_with_a_code_the_secret_does_not_produce_admits_nothing()
    {
        var installation = await ClaimedInstallationAsync();

        // Two hours out, which is past every step of slack the adapter allows.
        var stale = CodeAt(Claimed - TimeSpan.FromHours(2));

        Assert.Null(await SignInAsync(installation, TheirPassword, stale, null));
    }

    [Fact]
    public async Task A_backup_code_gets_in_once_and_stays_spent()
    {
        var installation = await ClaimedInstallationAsync();
        var code = installation.BackupCodes[0].Display;

        var signedIn = await SignInAsync(installation, TheirPassword, null, code);

        Assert.NotNull(signedIn);
        Assert.Equal(BackupCode.SetSize - 1, signedIn.BackupCodesRemaining);

        // The consumption is committed, so the next request finds it spent
        // rather than fresh — which is the whole of what single use means once
        // there are two requests.
        Assert.Null(await SignInAsync(installation, TheirPassword, null, code));

        await using var context = ContextFor(installation);
        Assert.Equal(
            1,
            await context.BackupCodes.CountAsync(
                stored => stored.UsedAt != null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Signing_out_removes_the_row_and_the_secret_admits_nothing_after_it()
    {
        var installation = await ClaimedInstallationAsync();

        var signedIn = await SignInAsync(installation, TheirPassword, CodeAt(Claimed), null);

        Assert.NotNull(signedIn);

        await using (var context = ContextFor(installation))
        {
            var session = await context.Sessions.SingleAsync(TestContext.Current.CancellationToken);
            await new SignOut(new Sessions(context))
                .ExecuteAsync(session, TestContext.Current.CancellationToken);
        }

        Assert.Null(await AuthenticateAsync(installation, signedIn.Secret.Text, Claimed));

        await using var reader = ContextFor(installation);
        Assert.Empty(await reader.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_use_past_the_interval_is_written_and_the_next_request_reads_it()
    {
        var installation = await ClaimedInstallationAsync();

        var signedIn = await SignInAsync(installation, TheirPassword, CodeAt(Claimed), null);

        Assert.NotNull(signedIn);

        var later = Claimed + AuthenticateSession.UseWriteInterval;
        var admitted = await AuthenticateAsync(installation, signedIn.Secret.Text, later);

        Assert.NotNull(admitted);
        Assert.True(admitted.DeadlineMoved);

        await using var context = ContextFor(installation);
        var stored = await context.Sessions.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(later, stored.LastUsedAt);
        Assert.Equal(later + Session.SlidingLifetime, stored.ExpiresAt);
    }

    /// <summary>
    /// An installation in the state a completed claim leaves it in: a real
    /// PBKDF2 hash, a TOTP secret sealed under the key on the volume, and a set
    /// of codes on paper.
    /// </summary>
    private async Task<Installation> ClaimedInstallationAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        var theOperator = Operator.Claim(
            new FrameworkPasswordHasher().Hash(Password.Create(TheirPassword)), Claimed);
        theOperator.EnrolSecondFactor(CipherOn(_volume).Encrypt(SecondFactorSecret), Claimed);

        var minted = BackupCode.MintSet(theOperator.Id, Claimed);
        var operators = new Operators(context);
        Assert.True(await operators.TryClaimAsync(
            theOperator, TestContext.Current.CancellationToken));
        await operators.ReplaceBackupCodesAsync(
            minted.Stored, TestContext.Current.CancellationToken);

        return new Installation(connectionString, minted.Shown);
    }

    private async Task<SignedIn?> SignInAsync(
        Installation installation, string password, string? code, string? backupCode)
    {
        await using var context = ContextFor(installation);

        return await new SignIn(
                new Operators(context),
                new Sessions(context),
                new FrameworkPasswordHasher(),
                new Rfc6238SecondFactor(),
                CipherOn(_volume),
                At(Claimed))
            .ExecuteAsync(
                password, code, backupCode, "203.0.113.7", TestContext.Current.CancellationToken);
    }

    private static async Task<AdmittedSession?> AuthenticateAsync(
        Installation installation, string secret, DateTimeOffset now)
    {
        await using var context = ContextFor(installation);

        return await new AuthenticateSession(new Sessions(context), At(now))
            .ExecuteAsync(secret, "203.0.113.7", TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The six digits the enrolled secret produces at that moment, which is what
    /// the authenticator app in the operator's pocket would be showing.
    /// </summary>
    /// <remarks>
    /// RFC 6238 written out here rather than asked of
    /// <see cref="Rfc6238SecondFactor"/>: a test that computes the code with the
    /// thing it is checking proves that the adapter agrees with itself.
    /// </remarks>
    private static string CodeAt(DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);

        var mac = HMACSHA1.HashData(SecondFactorKey, counter);
        var offset = mac[^1] & 0x0F;
        var truncated = BinaryPrimitives.ReadUInt32BigEndian(mac.AsSpan(offset, 4)) & 0x7FFFFFFF;

        return (truncated % 1_000_000).ToString("D6");
    }

    private static TimeProvider At(DateTimeOffset now) => new FixedClock(now);

    private static LogaffeDbContext ContextFor(Installation installation) =>
        ContextFor(installation.ConnectionString);

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static ISecretCipher CipherOn(string volumePath) =>
        new AesGcmSecretCipher(new HostVolumeKey(volumePath, NullLogger<HostVolumeKey>.Instance));

    private sealed record Installation(
        string ConnectionString, IReadOnlyList<BackupCodeText> BackupCodes);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
