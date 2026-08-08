using System.Formats.Tar;
using System.Text.Json;
using System.Text.Json.Serialization;
using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the artifact says about itself, and the first thing in it.
/// </summary>
/// <param name="Logaffe">The version that produced it.</param>
/// <param name="Migration">
/// The last migration applied to the database it was taken from. This is what a
/// restore reads before it replays anything, so it is not optional.
/// </param>
/// <param name="TakenAt">The instant it was taken.</param>
/// <param name="Entries">
/// Whether the entry table is in it. <c>false</c> is a legitimate choice rather
/// than a broken artifact (<c>docs/operations.md</c>), so it is stated rather
/// than left to be inferred from a missing file.
/// </param>
/// <param name="Tables">
/// The tables the artifact carries, in the order a replay can follow them, each
/// naming the columns its bytes fill.
/// </param>
public sealed record BackupManifest(
    string Logaffe,
    string Migration,
    DateTimeOffset TakenAt,
    bool Entries,
    IReadOnlyList<DumpedTable> Tables);

/// <summary>
/// Writes both halves of an installation into one artifact.
/// </summary>
/// <remarks>
/// <para>
/// logaffe does not run backups, schedule them or ship them anywhere — that is
/// <c>VISION.md</c>'s line and it stands. What this removes is not the operator's
/// responsibility but their opportunity to discharge it incorrectly: the
/// database and the key material go into one tar or neither does (ADR 0024).
/// </para>
/// <para>
/// The manifest goes first, so that a reader can decide what it is looking at
/// before reading the gigabytes behind it. Then the volume, then the tables —
/// the order a restore wants them in.
/// </para>
/// </remarks>
public sealed class TakeABackup(IDatabaseDump database, IHostVolume volume, TimeProvider clock)
{
    /// <summary>
    /// The bulk of any installation, and the one part that is expendable: short
    /// lived by design and additive to the applications' own files.
    /// </summary>
    public const string EntryTable = "log_entry";

    /// <summary>
    /// Without this the artifact is half of one, so its absence stops the
    /// command rather than producing something that only looks like a backup.
    /// </summary>
    private const string KeyFile = "keys/token.key";

    private static readonly JsonSerializerOptions ManifestFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public async Task<BackupManifest> ExecuteAsync(
        Stream destination,
        string version,
        bool withEntries,
        CancellationToken cancellationToken)
    {
        var files = volume.Files();

        if (!files.Contains(KeyFile, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"There is no {KeyFile} on the volume at {volume.Path}, so this would "
                + "be half an artifact: a database nothing can decrypt. Nothing was "
                + "written.");
        }

        var tables = (await database.TablesAsync(cancellationToken))
            .Where(table => withEntries || table.Name != EntryTable)
            .ToArray();

        var manifest = new BackupManifest(
            version,
            await database.LatestMigrationAsync(cancellationToken),
            clock.GetUtcNow(),
            withEntries,
            tables);

        await using var tar = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);

        await WriteAsync(
            tar,
            "manifest.json",
            new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestFormat)),
            cancellationToken);

        foreach (var file in files)
        {
            await using var content = volume.OpenRead(file);
            await WriteAsync(tar, $"volume/{file}", content, cancellationToken);
        }

        foreach (var table in tables)
        {
            // Spooled to disk rather than held in memory, because tar states an
            // entry's length before its bytes and a COPY stream does not know
            // its own. The entry table is why that matters: an installation's
            // largest table has to pass through here without being read into
            // memory first, and scratch space bounded by one table is the price.
            await using var spool = Scratch();
            await database.CopyOutAsync(table, spool, cancellationToken);
            spool.Position = 0;

            await WriteAsync(tar, $"data/{table.Name}", spool, cancellationToken);
        }

        return manifest;
    }

    private static async Task WriteAsync(
        TarWriter tar, string name, Stream content, CancellationToken cancellationToken)
    {
        // Pax rather than the older formats: it puts no limit on a path's length
        // and none on a file's size, and the entry table is a file whose size
        // has no business being bounded by an archive format from 1979.
        await tar.WriteEntryAsync(
            new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = content },
            cancellationToken);
    }

    private static FileStream Scratch() =>
        new(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()),
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                // Gone when it is closed, including when this throws: a failed
                // backup does not leave a copy of the database in the container.
                Options = FileOptions.DeleteOnClose,
            });
}
