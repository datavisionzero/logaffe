namespace Logaffe.Application.Operations;

/// <summary>
/// One comparison, asked in two places: are there migrations here that this
/// binary does not know about?
/// </summary>
/// <remarks>
/// <para>
/// <c>SchemaMigrator</c> asks it of the live database on startup, which is what
/// makes an old image refuse a schema a later version already migrated
/// (<c>docs/operations.md</c>). <see cref="RestoreABackup"/> asks it of an
/// artifact's manifest, which is what makes an artifact from a newer logaffe
/// refused rather than replayed. The two are the same question about the same
/// kind of identifier, so they are the same function — and it sits in this layer
/// because that is the one both the use case and the adapter below can reach.
/// </para>
/// <para>
/// The identifiers are EF Core's migration ids, which begin with the timestamp
/// they were scaffolded at. That makes "newer" an ordinary ordinal comparison —
/// but nothing here relies on it: what is refused is anything <em>unknown</em>,
/// which is the honest reading. A database carrying a migration from a branch
/// that never merged is not newer either, and it is just as much not ours to
/// serve.
/// </para>
/// </remarks>
public static class SchemaVersions
{
    /// <summary>
    /// The migration ids among <paramref name="applied"/> that are not among
    /// <paramref name="known"/>, in order.
    /// </summary>
    public static IReadOnlyList<string> NotKnownHere(
        IEnumerable<string> applied, IEnumerable<string> known) =>
        applied
            .Except(known, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// What is thrown when the schema in front of this binary was written by a
/// newer one.
/// </summary>
/// <remarks>
/// There is deliberately no downgrade path — going back a version means
/// restoring a backup — which is what makes this refusal load-bearing rather
/// than tidy: it is the thing standing between a mistaken
/// <c>docker compose up</c> on a stale image and an installation quietly
/// serving reads and writes against a shape it misunderstands.
/// </remarks>
public sealed class SchemaIsNewerException(IReadOnlyList<string> migrations)
    : Exception(Describe(migrations))
{
    /// <summary>The migration ids this binary does not know about.</summary>
    public IReadOnlyList<string> Migrations { get; } = migrations;

    private static string Describe(IReadOnlyList<string> migrations) =>
        $"This database was migrated by a newer logaffe. It carries "
        + $"{migrations.Count} migration(s) this version does not know about: "
        + $"{string.Join(", ", migrations)}. Start the version that migrated it, "
        + $"or restore a backup — there is no downgrade path.";
}
