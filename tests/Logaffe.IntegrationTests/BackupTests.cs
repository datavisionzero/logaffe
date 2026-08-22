using System.Formats.Tar;
using System.Text.Json;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Entries;
using Logaffe.Domain.Projects;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Persistence;
using Logaffe.Infrastructure.Persistence.Log;
using Logaffe.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The artifact, taken from the Postgres an installation actually runs.
/// </summary>
/// <remarks>
/// ADR 0037 makes the format ours, so what a substitute cannot vouch for is
/// everything about the bytes: which tables the model actually has, whether
/// <c>COPY</c> accepts the columns the manifest names, and what comes out of a
/// table that is empty. Whether it replays is the round trip, which arrives with
/// the restore verb.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class BackupTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first eleven bytes of any binary <c>COPY</c> stream, which is how a
    /// file in <c>data/</c> can be recognised as one at all.
    /// </summary>
    private static readonly byte[] CopySignature =
        [0x50, 0x47, 0x43, 0x4F, 0x50, 0x59, 0x0A, 0xFF, 0x0D, 0x0A, 0x00];

    [Fact]
    public async Task Every_table_is_dumped_after_the_ones_it_points_at()
    {
        await using var context = await MigratedAsync();

        var tables = await new PostgresDump(context)
            .TablesAsync(TestContext.Current.CancellationToken);

        var names = tables.Select(table => table.Name).ToArray();

        // Everything EF Core declares, with nothing left out: a table the model
        // has and the artifact does not is a table an operator loses.
        Assert.Equal(
            [
                "agent_token", "alert_condition_state", "backup_code", "claim_guard",
                "filesystem_reading", "host", "host_sample", "host_token",
                "ingest_token", "installation_settings", "log_entry", "operator",
                "project", "project_group", "project_tally", "session",
            ],
            names.Order(StringComparer.Ordinal));

        // Every foreign key in the schema, each read as "after".
        Assert.True(Array.IndexOf(names, "project") < Array.IndexOf(names, "ingest_token"));
        Assert.True(Array.IndexOf(names, "project_group") < Array.IndexOf(names, "project"));
        Assert.True(Array.IndexOf(names, "operator") < Array.IndexOf(names, "session"));
        Assert.True(Array.IndexOf(names, "operator") < Array.IndexOf(names, "backup_code"));

        // The host is pointed at by four things, one of them the project — which
        // is why it has to be restored before a table that was already in this
        // list before hosts existed.
        Assert.True(Array.IndexOf(names, "host") < Array.IndexOf(names, "project"));
        Assert.True(Array.IndexOf(names, "host") < Array.IndexOf(names, "host_token"));
        Assert.True(Array.IndexOf(names, "host") < Array.IndexOf(names, "host_sample"));
        Assert.True(
            Array.IndexOf(names, "host") < Array.IndexOf(names, "filesystem_reading"));
    }

    [Fact]
    public async Task The_artifact_holds_both_halves_of_the_installation()
    {
        await using var context = await MigratedAsync();
        await SeedAsync(context);

        var volume = VolumeWithAKey();
        var artifact = new MemoryStream();

        var manifest = await new TakeABackup(
                new PostgresDump(context), new HostVolume(volume), At(Now))
            .ExecuteAsync(
                artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);

        Assert.Equal(
            (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
                .Order(StringComparer.Ordinal)
                .Last(),
            manifest.Migration);

        var files = await ReadAsync(artifact);

        // The key half, byte for byte, because a database restored without it
        // produces an installation whose every token is undecryptable (ADR 0024).
        Assert.Equal(
            await File.ReadAllBytesAsync(
                Path.Combine(volume, "keys", "token.key"), TestContext.Current.CancellationToken),
            files["volume/keys/token.key"]);

        // The database half, one file per table, each one a COPY stream.
        foreach (var table in manifest.Tables)
        {
            var dumped = files[$"data/{table.Name}"];

            Assert.Equal(CopySignature, dumped[..CopySignature.Length]);
        }

        // And the rows are in it: a project, its token and two entries are more
        // bytes than an empty table's header and trailer.
        Assert.True(files["data/project"].Length > files["data/agent_token"].Length);
        Assert.True(files["data/log_entry"].Length > files["data/project"].Length);
    }

    [Fact]
    public async Task Leaving_the_entries_out_leaves_everything_else_in()
    {
        await using var context = await MigratedAsync();
        await SeedAsync(context);

        var artifact = new MemoryStream();

        var manifest = await new TakeABackup(
                new PostgresDump(context), new HostVolume(VolumeWithAKey()), At(Now))
            .ExecuteAsync(
                artifact, "1.4.0", withEntries: false, TestContext.Current.CancellationToken);

        var files = await ReadAsync(artifact);

        Assert.False(manifest.Entries);
        Assert.DoesNotContain($"data/{TakeABackup.EntryTable}", files.Keys);
        Assert.Contains("data/project", files.Keys);
        Assert.Contains("volume/keys/token.key", files.Keys);
    }

    /// <summary>
    /// The manifest is a document a later logaffe reads, so what it says has to
    /// survive a round trip through JSON rather than only through this process.
    /// </summary>
    [Fact]
    public async Task The_manifest_reads_back_as_what_was_written()
    {
        await using var context = await MigratedAsync();
        var artifact = new MemoryStream();

        var written = await new TakeABackup(
                new PostgresDump(context), new HostVolume(VolumeWithAKey()), At(Now))
            .ExecuteAsync(
                artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);

        var read = JsonSerializer.Deserialize<BackupManifest>(
            (await ReadAsync(artifact))["manifest.json"],
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(written.Logaffe, read.Logaffe);
        Assert.Equal(written.Migration, read.Migration);
        Assert.Equal(written.TakenAt, read.TakenAt);
        Assert.Equal(written.Entries, read.Entries);
        Assert.Equal(
            written.Tables.Select(table => (table.Name, string.Join(",", table.Columns))),
            read.Tables.Select(table => (table.Name, string.Join(",", table.Columns))));
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

    private static async Task SeedAsync(LogaffeDbContext context)
    {
        var project = Project.Create("orders-api", RetentionWindow.OfDays(14), Now);
        var minted = TokenText.Mint(TokenKind.Ingest);

        context.Projects.Add(project);
        context.IngestTokens.Add(
            IngestToken.Issue(project.Id, minted.Identifier, [1, 2, 3, 4], Now));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new Entries(context).WriteAsync(
            [
                Entry(1, project.Id, "Checkout {OrderId} failed"),
                Entry(2, project.Id, "Disk full on /dev/sda1"),
            ],
            TestContext.Current.CancellationToken);
    }

    private static LogEntry Entry(long id, Guid projectId, string template) => new()
    {
        Id = id,
        ProjectId = projectId,
        EventTime = Now,
        ReceiptTime = Now,
        Level = Level.Information,
        MessageTemplate = template,
        RenderedMessage = template,
        MessageTruncated = false,
        ExceptionTruncated = false,
    };

    /// <summary>
    /// A volume of its own per test, holding what every volume holds first.
    /// </summary>
    private static string VolumeWithAKey()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(Path.Combine(path, "keys"));
        File.WriteAllText(
            Path.Combine(path, "keys", "token.key"),
            Convert.ToBase64String(new byte[HostVolumeKey.LengthInBytes]));

        return path;
    }

    private static TimeProvider At(DateTimeOffset now) => new FixedClock(now);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<Dictionary<string, byte[]>> ReadAsync(MemoryStream artifact)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        artifact.Position = 0;
        await using var reader = new TarReader(artifact, leaveOpen: true);

        while (await reader.GetNextEntryAsync() is { } entry)
        {
            using var content = new MemoryStream();
            await entry.DataStream!.CopyToAsync(content, TestContext.Current.CancellationToken);
            files[entry.Name] = content.ToArray();
        }

        return files;
    }
}
