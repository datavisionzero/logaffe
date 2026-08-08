using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// A database that remembers what was written out of it and what was put back.
/// </summary>
/// <remarks>
/// It is deliberately not a Postgres: what a substitute can answer about a
/// backup is the shape of the artifact and the order of the acts, and whether
/// the bytes themselves replay is asked of a real server by the round trip in
/// the integration tests.
/// </remarks>
internal sealed class Dump : IDatabaseDump
{
    public const string Migration = "20260807182839_LogEntries";

    public List<DumpedTable> Tables { get; init; } =
    [
        new("project", ["id", "name"]),
        new("ingest_token", ["id", "project_id"]),
        new(TakeABackup.EntryTable, ["id", "project_id", "rendered_message"]),
    ];

    public IReadOnlyList<string> KnownMigrations { get; init; } =
        ["20260806175848_InitialSchema", Migration];

    /// <summary>What each table holds, standing in for its COPY bytes.</summary>
    public Dictionary<string, byte[]> Rows { get; } = new(StringComparer.Ordinal);

    /// <summary>What a replay put back, and what it was reset to first.</summary>
    public Dictionary<string, byte[]> Replayed { get; } = new(StringComparer.Ordinal);

    public string? ResetTo { get; private set; }

    public Task<string> LatestMigrationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Migration);

    public Task<IReadOnlyList<DumpedTable>> TablesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DumpedTable>>(Tables);

    public async Task CopyOutAsync(
        DumpedTable table, Stream destination, CancellationToken cancellationToken) =>
        await destination.WriteAsync(
            Rows.TryGetValue(table.Name, out var bytes)
                ? bytes
                : Encoding.UTF8.GetBytes($"the bytes of {table.Name}"),
            cancellationToken);

    public Task ResetToAsync(string migration, CancellationToken cancellationToken)
    {
        ResetTo = migration;
        Replayed.Clear();

        return Task.CompletedTask;
    }

    public async Task CopyInAsync(
        DumpedTable table, Stream source, CancellationToken cancellationToken)
    {
        using var held = new MemoryStream();
        await source.CopyToAsync(held, cancellationToken);

        Replayed[table.Name] = held.ToArray();
    }
}

/// <summary>A host volume held in memory.</summary>
internal sealed class Volume(Dictionary<string, string>? contents = null) : IHostVolume
{
    public Dictionary<string, string> Contents { get; } = contents ?? new(StringComparer.Ordinal)
    {
        [TakeABackup.KeyFile] = "a key",
        ["logs/logaffe.log"] = "a line",
    };

    public string Path => "/var/lib/logaffe";

    public IReadOnlyList<string> Files() => [.. Contents.Keys.Order(StringComparer.Ordinal)];

    public Stream OpenRead(string relativePath) =>
        new MemoryStream(Encoding.UTF8.GetBytes(Contents[relativePath]));

    public Stream Create(string relativePath) => new Written(this, relativePath);

    /// <summary>
    /// A file that lands in <see cref="Contents"/> when it is closed, which is
    /// the only moment at which a real one would be complete either.
    /// </summary>
    private sealed class Written(Volume volume, string relativePath) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            volume.Contents[relativePath] = Encoding.UTF8.GetString(ToArray());

            base.Dispose(disposing);
        }
    }
}
