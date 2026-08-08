namespace Logaffe.Application.Ports;

/// <summary>
/// One table as an artifact carries it: its name, and the columns in the order
/// its bytes are written.
/// </summary>
/// <remarks>
/// The columns are written down rather than left implicit because binary
/// <c>COPY</c> matches values to columns by position and nothing checks the
/// names — the same trap the ingest path's writer names. Carrying them in the
/// artifact means the replay names the columns it is filling instead of trusting
/// that the table it is filling has the shape it had when the dump was taken.
/// </remarks>
public sealed record DumpedTable(string Name, IReadOnlyList<string> Columns);

/// <summary>
/// The database, as something that can be written out and read back.
/// </summary>
/// <remarks>
/// The installation dumps its own database rather than shelling out to
/// <c>pg_dump</c> (ADR 0037): the runtime image carries no Postgres tooling, the
/// database is a separate container, and adding a client to the image would tie
/// the image's version to the server's. What that buys is no version coupling;
/// what it costs is that the format is ours, so the replay is ours too.
/// </remarks>
public interface IDatabaseDump
{
    /// <summary>
    /// The last migration applied to this database, which is the artifact's
    /// compatibility surface: a restore refuses an artifact naming a migration
    /// it does not know (ADR 0024).
    /// </summary>
    Task<string> LatestMigrationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every migration this binary was built with, which is what an artifact's
    /// is compared against.
    /// </summary>
    IReadOnlyList<string> KnownMigrations { get; }

    /// <summary>
    /// Every table, in an order a replay can follow without tripping over a
    /// foreign key.
    /// </summary>
    Task<IReadOnlyList<DumpedTable>> TablesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes one table out, as it is and without buffering it in memory: the
    /// entry table is the largest thing an installation holds.
    /// </summary>
    Task CopyOutAsync(
        DumpedTable table, Stream destination, CancellationToken cancellationToken);

    /// <summary>
    /// Empties the database and builds the schema back up to the migration the
    /// artifact was taken at — not to the newest one this binary knows.
    /// </summary>
    /// <remarks>
    /// The shape the bytes came out of is the shape they have to go back into,
    /// and an artifact from an older logaffe is one a newer one is documented to
    /// accept. Building the schema to the artifact's own migration is what makes
    /// those two statements compatible: the replay fills the tables it was taken
    /// from, and the first start afterwards migrates the rest of the way, which
    /// is the ordinary upgrade path and not a second mechanism.
    /// </remarks>
    Task ResetToAsync(string migration, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one table back in, from the bytes <see cref="CopyOutAsync"/> wrote.
    /// </summary>
    Task CopyInAsync(
        DumpedTable table, Stream source, CancellationToken cancellationToken);
}
