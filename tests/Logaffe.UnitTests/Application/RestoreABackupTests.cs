using System.Formats.Tar;
using System.Text;
using System.Text.Json;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// What a restore refuses, and what it does once it has not refused.
/// </summary>
/// <remarks>
/// The refusals are the half worth asking here, because every one of them has to
/// happen <em>before</em> anything is written — an artifact that turns out to be
/// half of one after the database is gone is the trap the command exists to
/// remove. Whether the bytes replay into a real schema is the round trip in the
/// integration tests.
/// </remarks>
public sealed class RestoreABackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_artifact_goes_back_where_it_came_from()
    {
        var taken = new Dump
        {
            Rows =
            {
                ["project"] = Encoding.UTF8.GetBytes("one project"),
                ["ingest_token"] = Encoding.UTF8.GetBytes("one token"),
                [TakeABackup.EntryTable] = Encoding.UTF8.GetBytes("two entries"),
            },
        };

        var artifact = await ArtifactOf(taken, new Volume(), withEntries: true);

        // A different installation entirely: the one being restored into.
        var into = new Dump();
        var volume = new Volume(new(StringComparer.Ordinal)
        {
            [TakeABackup.KeyFile] = "somebody else's key",
        });

        var restored = await new RestoreABackup(into, volume)
            .ExecuteAsync(artifact, TestContext.Current.CancellationToken);

        Assert.Equal(Dump.Migration, into.ResetTo);
        Assert.Equal(3, restored.Tables);
        Assert.Equal(2, restored.Files);

        // Both halves. The key above all, because a database without it is an
        // installation whose every token is undecryptable (ADR 0024).
        Assert.Equal("a key", volume.Contents[TakeABackup.KeyFile]);
        Assert.Equal(
            taken.Rows.ToDictionary(row => row.Key, row => Encoding.UTF8.GetString(row.Value)),
            into.Replayed.ToDictionary(row => row.Key, row => Encoding.UTF8.GetString(row.Value)));
    }

    /// <summary>
    /// The same comparison the installation makes at startup against a database
    /// a newer build migrated (#25), asked of the artifact instead. There is no
    /// downgrade path, so this is refused rather than attempted.
    /// </summary>
    [Fact]
    public async Task An_artifact_from_a_newer_logaffe_is_refused_before_anything_is_touched()
    {
        var artifact = await ArtifactOf(new Dump(), new Volume(), withEntries: true);

        var into = new Dump { KnownMigrations = ["20260806175848_InitialSchema"] };
        var volume = new Volume();

        var refusal = await Assert.ThrowsAsync<ArtifactRefusedException>(() =>
            new RestoreABackup(into, volume)
                .ExecuteAsync(artifact, TestContext.Current.CancellationToken));

        Assert.Contains(Dump.Migration, refusal.Message);
        Assert.Null(into.ResetTo);
        Assert.Empty(into.Replayed);
    }

    [Fact]
    public async Task An_artifact_holding_no_key_is_refused_before_anything_is_touched()
    {
        // Not something `backup` can produce — it refuses to write one — so this
        // is the artifact somebody assembled by hand, or took apart and put
        // back together wrong.
        var artifact = await ArtifactWithoutTheKeyAsync();

        var into = new Dump();

        var refusal = await Assert.ThrowsAsync<ArtifactRefusedException>(() =>
            new RestoreABackup(into, new Volume())
                .ExecuteAsync(artifact, TestContext.Current.CancellationToken));

        Assert.Contains(TakeABackup.KeyFile, refusal.Message);
        Assert.Null(into.ResetTo);
    }

    /// <summary>
    /// Every field of the manifest is required and JSON has no way to say so, so
    /// a document missing one deserializes into nulls. Found by restoring an
    /// artifact taken before the manifest carried its volume: the first thing to
    /// touch a null reported a null argument, which tells the operator nothing
    /// about the artifact in their hand.
    /// </summary>
    [Fact]
    public async Task An_artifact_whose_manifest_is_missing_something_is_refused()
    {
        var artifact = await ArtifactWithManifestAsync(
            """{"logaffe":"1.4.0","migration":"20260807182839_LogEntries","entries":true}""");

        var into = new Dump();

        var refusal = await Assert.ThrowsAsync<ArtifactRefusedException>(() =>
            new RestoreABackup(into, new Volume())
                .ExecuteAsync(artifact, TestContext.Current.CancellationToken));

        Assert.Contains("manifest.json", refusal.Message);
        Assert.Null(into.ResetTo);
    }

    [Fact]
    public async Task Something_that_is_not_an_artifact_is_refused()
    {
        var into = new Dump();

        var refusal = await Assert.ThrowsAsync<ArtifactRefusedException>(() =>
            new RestoreABackup(into, new Volume()).ExecuteAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(new string('n', 4096))),
                TestContext.Current.CancellationToken));

        Assert.Contains("tar", refusal.Message);
        Assert.Null(into.ResetTo);
    }

    /// <summary>
    /// An artifact taken without the entries restores an installation without
    /// them, and with everything whose loss cannot be shrugged off.
    /// </summary>
    [Fact]
    public async Task An_artifact_taken_without_the_entries_puts_back_everything_else()
    {
        var artifact = await ArtifactOf(new Dump(), new Volume(), withEntries: false);

        var into = new Dump();

        var restored = await new RestoreABackup(into, new Volume())
            .ExecuteAsync(artifact, TestContext.Current.CancellationToken);

        Assert.False(restored.Manifest.Entries);
        Assert.DoesNotContain(TakeABackup.EntryTable, into.Replayed.Keys);
        Assert.Contains("project", into.Replayed.Keys);
    }

    private static async Task<MemoryStream> ArtifactOf(Dump dump, Volume volume, bool withEntries)
    {
        var artifact = new MemoryStream();

        await new TakeABackup(dump, volume, new StoppedClock(Now))
            .ExecuteAsync(artifact, "1.4.0", withEntries, TestContext.Current.CancellationToken);

        artifact.Position = 0;

        return artifact;
    }

    /// <summary>
    /// Not something `backup` can produce -- it refuses to write one -- so this
    /// is assembled by hand, which is the only way one of these comes about.
    /// </summary>
    private static Task<MemoryStream> ArtifactWithoutTheKeyAsync() =>
        ArtifactWithManifestAsync(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(
            new BackupManifest(
                "1.4.0",
                Dump.Migration,
                Now,
                Entries: false,
                Volume: ["logs/logaffe.log"],
                Tables: [new DumpedTable("project", ["id", "name"])]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    /// <summary>A tar carrying whatever manifest the test wants to hand over.</summary>
    private static async Task<MemoryStream> ArtifactWithManifestAsync(string manifest)
    {
        var artifact = new MemoryStream();

        await using (var tar = new TarWriter(artifact, TarEntryFormat.Pax, leaveOpen: true))
        {
            await tar.WriteEntryAsync(
                new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest)),
                },
                TestContext.Current.CancellationToken);
        }

        artifact.Position = 0;

        return artifact;
    }
}
