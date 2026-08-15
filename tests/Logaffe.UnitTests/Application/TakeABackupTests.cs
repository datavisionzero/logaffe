using System.Formats.Tar;
using System.Text.Json;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The shape of the artifact: what is in it, in which order, and what stops it
/// from being written at all.
/// </summary>
/// <remarks>
/// Whether the bytes replay is a question no substitute can answer, and it is
/// asked of a real database by the round trip in the integration tests. What can
/// be asked here is everything ADR 0024 is about — that both halves go in or
/// neither does.
/// </remarks>
public sealed class TakeABackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_artifact_holds_the_manifest_the_volume_and_the_tables()
    {
        var artifact = new MemoryStream();

        var manifest = await Backup().ExecuteAsync(
            artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);

        Assert.Equal("1.4.0", manifest.Logaffe);
        Assert.Equal("20260807182839_LogEntries", manifest.Migration);
        Assert.Equal(Now, manifest.TakenAt);
        Assert.True(manifest.Entries);

        Assert.Equal(
            [
                "manifest.json",
                "volume/keys/token.key",
                "data/project",
                "data/ingest_token",
                "data/log_entry",
            ],
            await NamesIn(artifact));
    }

    /// <summary>
    /// The volume's own log is the one file there an artifact leaves behind. It
    /// was carried once, and on an installation with almost nothing in it that
    /// made the log 98% of the artifact (#66).
    /// </summary>
    [Fact]
    public async Task The_artifact_leaves_logaffes_own_log_on_the_volume()
    {
        var volume = new Volume(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TakeABackup.KeyFile] = "a key",
            ["keys/data-protection/key-1.xml"] = "a ring",
            ["logs/logaffe-20260815.log"] = "a line",
            ["logs/logaffe-20260814.log"] = "an older line",
        });

        var artifact = new MemoryStream();

        var manifest = await Backup(volume: volume).ExecuteAsync(
            artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["keys/data-protection/key-1.xml", TakeABackup.KeyFile],
            manifest.Volume);

        Assert.DoesNotContain(
            await NamesIn(artifact),
            name => name.StartsWith("volume/logs/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The key is what makes the rest of the artifact readable, so its absence
    /// stops the command — and the log being skipped must not be what decides
    /// that. A volume of nothing but log is still a volume without a key.
    /// </summary>
    [Fact]
    public async Task A_volume_holding_only_a_log_is_still_a_missing_key()
    {
        var volume = new Volume(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["logs/logaffe-20260815.log"] = "a line",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Backup(volume: volume).ExecuteAsync(
                new MemoryStream(), "1.4.0", withEntries: true, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The manifest is what a restore reads before it replays anything, so it
    /// carries the columns each table's bytes fill rather than leaving the
    /// replay to match them by position.
    /// </summary>
    [Fact]
    public async Task The_manifest_names_the_columns_of_every_table_it_carries()
    {
        var artifact = new MemoryStream();

        await Backup().ExecuteAsync(
            artifact, "1.4.0", withEntries: true, TestContext.Current.CancellationToken);

        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await ContentOf(artifact, "manifest.json"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(
            ["project", "ingest_token", "log_entry"],
            manifest.Tables.Select(table => table.Name));

        Assert.Equal(["id", "project_id"], manifest.Tables[1].Columns);
    }

    /// <summary>
    /// Documented as a legitimate choice rather than a broken artifact, so the
    /// manifest says the entries were left out instead of leaving a restore to
    /// infer it from a file that is not there.
    /// </summary>
    [Fact]
    public async Task The_entries_can_be_left_out()
    {
        var artifact = new MemoryStream();

        var manifest = await Backup().ExecuteAsync(
            artifact, "1.4.0", withEntries: false, TestContext.Current.CancellationToken);

        Assert.False(manifest.Entries);
        Assert.DoesNotContain(TakeABackup.EntryTable, manifest.Tables.Select(table => table.Name));
        Assert.DoesNotContain($"data/{TakeABackup.EntryTable}", await NamesIn(artifact));

        // The half whose loss cannot be shrugged off is still all there.
        Assert.Contains("volume/keys/token.key", await NamesIn(artifact));
        Assert.Contains("data/project", await NamesIn(artifact));
    }

    /// <summary>
    /// A database restored without its key produces an installation whose every
    /// token is undecryptable (ADR 0022), which is exactly the belief ADR 0024
    /// exists to prevent. Half an artifact is worse than none, because it looks
    /// like one.
    /// </summary>
    [Fact]
    public async Task A_volume_without_the_key_is_not_backed_up_at_all()
    {
        var volume = new Volume(new() { ["logs/logaffe.log"] = "nothing that matters" });

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Backup(volume).ExecuteAsync(
                new MemoryStream(), "1.4.0", withEntries: true,
                TestContext.Current.CancellationToken));

        Assert.Contains("keys/token.key", refusal.Message);
    }

    private static TakeABackup Backup(Volume? volume = null) =>
        new(new Dump(), volume ?? new Volume(), new StoppedClock(Now));

    private static async Task<IReadOnlyList<string>> NamesIn(MemoryStream artifact)
    {
        var names = new List<string>();

        artifact.Position = 0;
        await using var reader = new TarReader(artifact, leaveOpen: true);

        while (await reader.GetNextEntryAsync() is { } entry)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    private static async Task<string> ContentOf(MemoryStream artifact, string name)
    {
        artifact.Position = 0;
        await using var reader = new TarReader(artifact, leaveOpen: true);

        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.Name == name)
            {
                using var content = new StreamReader(entry.DataStream!);
                return await content.ReadToEndAsync();
            }
        }

        throw new InvalidOperationException($"{name} is not in the artifact.");
    }
}
