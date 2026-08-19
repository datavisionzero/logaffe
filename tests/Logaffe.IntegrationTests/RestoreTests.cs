using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Operators;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The round trip: take an artifact, restore it, and find the installation the
/// operator had.
/// </summary>
/// <remarks>
/// This is the test ADR 0037 names as the one that matters. The format is ours,
/// so the replay is ours, and nothing short of a real Postgres can say whether
/// binary <c>COPY</c> bytes go back into a schema this code rebuilt. It restores
/// into a <em>different</em> installation on purpose — another database, another
/// key, another project — because a round trip into the database it came from
/// would pass on a replay that did nothing at all.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class RestoreTests(PostgresFixture postgres) : IDisposable
{
    private const string TheirPassword = "a passphrase they typed";

    /// <summary>RFC 6238's own secret, in the base32 an app is enrolled with.</summary>
    private const string SecondFactorSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly byte[] SecondFactorKey =
        Encoding.UTF8.GetBytes("12345678901234567890");

    private static readonly DateTimeOffset Claimed = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly string taken = Directory.CreateTempSubdirectory("logaffe-taken-").FullName;
    private readonly string into = Directory.CreateTempSubdirectory("logaffe-into-").FullName;

    public void Dispose()
    {
        Directory.Delete(taken, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact]
    public async Task What_comes_back_is_the_installation_the_operator_had()
    {
        var original = await AnInstallationAsync(taken, "orders-api");
        var artifact = await BackupOfAsync(original, taken);

        // Somewhere else entirely: another database with another project in it,
        // and another key on its volume.
        var elsewhere = await AnInstallationAsync(into, "something-else");

        var restored = await RestoreIntoAsync(elsewhere, into, artifact);

        Assert.Equal(14, restored.Tables);
        Assert.Equal(original.Migration, restored.Manifest.Migration);

        await using var context = ContextFor(elsewhere.ConnectionString);

        // The project is the one from the artifact, and the one that was here is
        // not: a restore replaces, it does not merge.
        var project = await context.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original.ProjectId, project.Id);
        Assert.Equal("orders-api", project.Name);
        Assert.Equal(RetentionWindow.OfDays(14), project.Retention);

        // The entries, which are the bulk and the part that goes through COPY in
        // both directions.
        Assert.Equal(
            [1L, 2L],
            await context.Database.SqlQuery<long>($"select id as \"Value\" from log_entry order by id")
                .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0024's whole point: the two halves are one artifact. A restore that
    /// put back a database and left the old key would produce an installation
    /// whose every token is undecryptable — which is exactly the trap the
    /// command exists to remove.
    /// </summary>
    [Fact]
    public async Task The_tokens_are_readable_and_the_operator_can_sign_in()
    {
        var original = await AnInstallationAsync(taken, "orders-api");
        var artifact = await BackupOfAsync(original, taken);

        var elsewhere = await AnInstallationAsync(into, "something-else");
        await RestoreIntoAsync(elsewhere, into, artifact);

        // A cipher built after the restore, so it reads the key that arrived
        // with the artifact rather than the one that was on this volume.
        var cipher = CipherOn(into);

        await using var context = ContextFor(elsewhere.ConnectionString);

        var stored = await context.IngestTokens.SingleAsync(TestContext.Current.CancellationToken);
        var readBack = TokenText.From(
            TokenKind.Ingest, stored.Identifier, cipher.Decrypt(stored.EncryptedSecret));

        Assert.Equal(original.Token, readBack.Text);

        // And the person: the password hash and the second factor's secret came
        // back together, which is the other thing losing a volume costs.
        var signedIn = await new SignIn(
                new Operators(context),
                new Sessions(context),
                new FrameworkPasswordHasher(),
                new Rfc6238SecondFactor(),
                cipher,
                At(Claimed))
            .ExecuteAsync(
                TheirPassword, CodeAt(Claimed), null, "203.0.113.7",
                TestContext.Current.CancellationToken);

        Assert.NotNull(signedIn);
    }

    /// <summary>
    /// The refusal ADR 0024 asks for, against a real database: an artifact
    /// naming a migration this binary does not know is refused, and the
    /// installation it was pointed at is untouched.
    /// </summary>
    [Fact]
    public async Task An_artifact_from_a_newer_logaffe_leaves_the_installation_alone()
    {
        var original = await AnInstallationAsync(taken, "orders-api");
        var artifact = await BackupOfAsync(original, taken);

        // The same artifact with a migration id from a logaffe that does not
        // exist yet, which is what an operator downgrading their image would be
        // holding.
        // Padded to the length of the id it replaces, so that the tar's own
        // bookkeeping still adds up whatever this binary's latest migration
        // happens to be called.
        var tooNew = "29991231235959_TooNew".PadRight(original.Migration.Length, '0');

        var fromTheFuture = Rewritten(artifact, original.Migration, tooNew);

        var elsewhere = await AnInstallationAsync(into, "something-else");

        var refusal = await Assert.ThrowsAsync<ArtifactRefusedException>(() =>
            RestoreIntoAsync(elsewhere, into, fromTheFuture));

        Assert.Contains(tooNew, refusal.Message);

        await using var context = ContextFor(elsewhere.ConnectionString);

        Assert.Equal(
            "something-else",
            (await context.Projects.SingleAsync(TestContext.Current.CancellationToken)).Name);
    }

    /// <summary>
    /// An installation in the state a completed claim leaves it in, plus a
    /// project, a token sealed under this volume's key, and two entries.
    /// </summary>
    private async Task<Installation> AnInstallationAsync(string volume, string projectName)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var context = ContextFor(connectionString);
        await new SchemaMigrator(context, NullLogger<SchemaMigrator>.Instance)
            .ApplyAsync(TestContext.Current.CancellationToken);

        var cipher = CipherOn(volume);

        var theOperator = Operator.Claim(
            new FrameworkPasswordHasher().Hash(Password.Create(TheirPassword)), Claimed);
        theOperator.EnrolSecondFactor(cipher.Encrypt(SecondFactorSecret), Claimed);

        var operators = new Operators(context);
        Assert.True(await operators.TryClaimAsync(
            theOperator, TestContext.Current.CancellationToken));
        await operators.ReplaceBackupCodesAsync(
            BackupCode.MintSet(theOperator.Id, Claimed).Stored,
            TestContext.Current.CancellationToken);

        var project = Project.Create(projectName, RetentionWindow.OfDays(14), Claimed);
        var minted = TokenText.Mint(TokenKind.Ingest);

        context.Projects.Add(project);
        context.IngestTokens.Add(IngestToken.Issue(
            project.Id, minted.Identifier, cipher.Encrypt(minted.Secret), Claimed));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new Entries(context).WriteAsync(
            [Entry(1, project.Id), Entry(2, project.Id)],
            TestContext.Current.CancellationToken);

        return new Installation(
            connectionString,
            project.Id,
            minted.Text,
            (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
                .Order(StringComparer.Ordinal)
                .Last());
    }

    private static async Task<MemoryStream> BackupOfAsync(Installation installation, string volume)
    {
        var artifact = new MemoryStream();

        await using (var context = ContextFor(installation.ConnectionString))
        {
            await new TakeABackup(new PostgresDump(context), new HostVolume(volume), At(Claimed))
                .ExecuteAsync(
                    artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);
        }

        artifact.Position = 0;

        return artifact;
    }

    private static async Task<Restored> RestoreIntoAsync(
        Installation installation, string volume, MemoryStream artifact)
    {
        await using var context = ContextFor(installation.ConnectionString);

        return await new RestoreABackup(new PostgresDump(context), new HostVolume(volume))
            .ExecuteAsync(artifact, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The same artifact with one string in its manifest changed. Both are the
    /// same length, so the tar's own bookkeeping still adds up.
    /// </summary>
    private static MemoryStream Rewritten(MemoryStream artifact, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);

        var bytes = artifact.ToArray();
        var found = Encoding.UTF8.GetString(bytes).IndexOf(from, StringComparison.Ordinal);

        Assert.True(found > 0);
        Encoding.UTF8.GetBytes(to).CopyTo(bytes.AsSpan(found));

        return new MemoryStream(bytes);
    }

    private static LogEntry Entry(long id, Guid projectId) => new()
    {
        Id = id,
        ProjectId = projectId,
        EventTime = Claimed,
        ReceiptTime = Claimed,
        Level = Level.Information,
        MessageTemplate = "Checkout {OrderId} failed",
        RenderedMessage = "Checkout 4711 failed",
        Properties = """{"OrderId":4711}""",
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    /// <summary>RFC 6238 written out rather than asked of the adapter.</summary>
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

    private static LogaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<LogaffeDbContext>().UseNpgsql(connectionString).Options);

    private static ISecretCipher CipherOn(string volumePath) =>
        new AesGcmSecretCipher(new HostVolumeKey(volumePath, NullLogger<HostVolumeKey>.Instance));

    private sealed record Installation(
        string ConnectionString, Guid ProjectId, string Token, string Migration);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
