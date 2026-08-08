using System.Data;
using Logaffe.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Logaffe.Infrastructure.Persistence;

/// <summary>
/// The database writing itself out, one table at a time, through binary
/// <c>COPY</c>.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of the <c>COPY</c> writer the ingest path already has (ADR 0003),
/// which is why ADR 0037 could rule out <c>postgresql-client</c> in the image
/// without inventing a new kind of thing in this codebase. It is raw on purpose:
/// the bytes Postgres produces are the bytes the artifact carries and the bytes
/// the replay hands back, so nothing here parses a value it does not have to.
/// </para>
/// <para>
/// <b>The tables come from the model rather than from a list kept by hand.</b>
/// ADR 0037 accepts that every schema change has two places to land; this makes
/// one of the two automatic, because a table EF Core declares is a table the
/// artifact carries with nobody having to remember. What is left to get right is
/// the replay.
/// </para>
/// </remarks>
public sealed class PostgresDump(LogaffeDbContext context) : IDatabaseDump
{
    public async Task<string> LatestMigrationAsync(CancellationToken cancellationToken)
    {
        // Ordinal, because a migration id begins with the timestamp it was
        // scaffolded at, which is what makes "latest" a reading of the string.
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return applied.Length > 0
            ? applied[^1]
            : throw new InvalidOperationException(
                "This database has no migrations applied, so there is no installation "
                + "here to back up.");
    }

    public IReadOnlyList<string> KnownMigrations => [.. context.Database.GetMigrations()];

    public Task<IReadOnlyList<DumpedTable>> TablesAsync(CancellationToken cancellationToken)
    {
        var ordered = new List<DumpedTable>();
        var placed = new HashSet<IEntityType>();

        foreach (var type in context.Model.GetEntityTypes()
                     .Where(type => type.GetTableName() is not null)
                     .OrderBy(type => type.GetTableName(), StringComparer.Ordinal))
        {
            Place(type);
        }

        return Task.FromResult<IReadOnlyList<DumpedTable>>(ordered);

        // A table after everything it points at, so that a replay filling them in
        // this order never inserts a row whose foreign key names one that is not
        // there yet.
        void Place(IEntityType type)
        {
            if (!placed.Add(type))
            {
                return;
            }

            foreach (var principal in type.GetForeignKeys()
                         .Select(key => key.PrincipalEntityType)
                         .Where(principal => principal != type
                             && principal.GetTableName() is not null))
            {
                Place(principal);
            }

            ordered.Add(Describe(type));
        }
    }

    public async Task CopyOutAsync(
        DumpedTable table, Stream destination, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        // Whatever the connection's state was on the way in is what it is on the
        // way out — the same courtesy the ingest writer extends, and for the same
        // reason: this runs inside somebody else's scope.
        var wasClosed = connection.State is not ConnectionState.Open;
        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var copy = await connection.BeginRawBinaryCopyAsync(
                $"copy {Quote(table.Name)} ({Columns(table)}) to stdout (format binary)",
                cancellationToken);

            await copy.CopyToAsync(destination, cancellationToken);
        }
        finally
        {
            if (wasClosed)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    public async Task ResetToAsync(string migration, CancellationToken cancellationToken)
    {
        // The whole schema rather than the tables the model happens to name:
        // what is being replaced is an installation, and an artifact from an
        // older logaffe would leave behind exactly the tables its own model
        // never had. Recreating the schema also takes the extensions and the
        // migration history with it, which is what makes the rebuild below start
        // from nothing.
        await context.Database.ExecuteSqlRawAsync(
            "drop schema public cascade; create schema public", cancellationToken);

        await context.Database.GetService<IMigrator>()
            .MigrateAsync(migration, cancellationToken);
    }

    public async Task CopyInAsync(
        DumpedTable table, Stream source, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        var wasClosed = connection.State is not ConnectionState.Open;
        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var copy = await connection.BeginRawBinaryCopyAsync(
                $"copy {Quote(table.Name)} ({Columns(table)}) from stdin (format binary)",
                cancellationToken);

            await source.CopyToAsync(copy, cancellationToken);

            // Ending the stream is what commits the copy; letting it be disposed
            // without this would be Npgsql's way of hearing that the caller
            // changed its mind.
            await copy.FlushAsync(cancellationToken);
        }
        finally
        {
            if (wasClosed)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>
    /// The columns in model order, which is the order the bytes are in and the
    /// order the manifest records.
    /// </summary>
    private static DumpedTable Describe(IEntityType type)
    {
        var table = StoreObjectIdentifier.Create(type, StoreObjectType.Table)
            ?? throw new InvalidOperationException(
                $"{type.DisplayName()} is mapped to no table.");

        return new DumpedTable(
            type.GetTableName()!,
            [.. type.GetProperties()
                .Select(property => property.GetColumnName(table))
                .OfType<string>()]);
    }

    internal static string Columns(DumpedTable table) =>
        string.Join(", ", table.Columns.Select(Quote));

    /// <summary>
    /// Every identifier here comes from the model or from a manifest this
    /// installation wrote, and quoting them is what keeps that true of the next
    /// one as well.
    /// </summary>
    internal static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
