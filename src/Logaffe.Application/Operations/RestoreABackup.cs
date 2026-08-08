using System.Formats.Tar;
using System.Text.Json;
using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>What a restore put back.</summary>
/// <param name="Manifest">What the artifact said it was.</param>
/// <param name="Files">How many files went onto the host volume.</param>
/// <param name="Tables">How many tables were replayed.</param>
public sealed record Restored(BackupManifest Manifest, int Files, int Tables);

/// <summary>
/// What is thrown when an artifact is not one this logaffe can put back.
/// </summary>
public sealed class ArtifactRefusedException(string message) : Exception(message);

/// <summary>
/// Puts both halves of an installation back.
/// </summary>
/// <remarks>
/// <para>
/// The other half of ADR 0024, and the standing cost of ADR 0037: the format is
/// ours, so the replay is ours too — there is no <c>pg_restore</c> to lean on.
/// </para>
/// <para>
/// <b>It replaces.</b> "Put my installation back" means replacing, and refusing
/// an installation that already holds something would force the operator to
/// empty a Docker volume by hand at the worst possible moment. It is also the
/// only answer that does not need "empty" defined: a started installation is
/// never literally empty, since it already carries its schema and a
/// <c>claim_window</c> row (ADR 0034).
/// </para>
/// <para>
/// <b>Both halves or neither.</b> The refusals are made against the manifest,
/// before anything is written, because an artifact that turns out to be half of
/// one after the database is already gone is exactly the trap this command
/// exists to remove.
/// </para>
/// </remarks>
public sealed class RestoreABackup(IDatabaseDump database, IHostVolume volume)
{
    private static readonly JsonSerializerOptions ManifestFormat =
        new(JsonSerializerDefaults.Web);

    public async Task<Restored> ExecuteAsync(
        Stream artifact, CancellationToken cancellationToken)
    {
        await using var tar = new TarReader(artifact, leaveOpen: true);

        var manifest = await ReadManifestAsync(tar, cancellationToken);

        Refuse(manifest);

        // From here on the installation that was here is gone. The order is the
        // artifact's own: the schema is built back to what the bytes came out
        // of before a single one of them is replayed.
        await database.ResetToAsync(manifest.Migration, cancellationToken);

        var tables = manifest.Tables.ToDictionary(table => $"data/{table.Name}", StringComparer.Ordinal);
        var files = 0;
        var replayed = 0;

        while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
        {
            if (entry.DataStream is not { } content)
            {
                continue;
            }

            if (entry.Name.StartsWith("volume/", StringComparison.Ordinal))
            {
                await using var destination = volume.Create(entry.Name["volume/".Length..]);
                await content.CopyToAsync(destination, cancellationToken);
                files++;
            }
            else if (tables.TryGetValue(entry.Name, out var table))
            {
                await database.CopyInAsync(table, content, cancellationToken);
                replayed++;
            }
        }

        if (replayed != manifest.Tables.Count || files != manifest.Volume.Count)
        {
            // The artifact promised more than it held, and the promise was
            // checked against what arrived rather than assumed. Saying so is all
            // that can be done here — the installation is already replaced —
            // but an operator who is told beats one who finds out.
            throw new ArtifactRefusedException(
                $"The artifact said it held {manifest.Volume.Count} file(s) and "
                + $"{manifest.Tables.Count} table(s), and {files} and {replayed} arrived. "
                + "What is in this installation is whatever was in the artifact, and "
                + "it is not what the artifact said it was.");
        }

        return new Restored(manifest, files, replayed);
    }

    /// <summary>
    /// The manifest is written first for exactly this reason: everything that
    /// decides whether the artifact can be put back is readable before any of
    /// the bytes behind it are.
    /// </summary>
    private static async Task<BackupManifest> ReadManifestAsync(
        TarReader tar, CancellationToken cancellationToken)
    {
        TarEntry? first;

        try
        {
            first = await tar.GetNextEntryAsync(cancellationToken: cancellationToken);
        }
        catch (InvalidDataException cause)
        {
            // Whatever was piped in, it is not a tar. Said as its own sentence
            // rather than as a stack trace, because the likeliest cause is a
            // redirect that picked up the wrong file.
            throw new ArtifactRefusedException(
                $"This is not a tar, so it is not a logaffe backup: {cause.Message}");
        }

        if (first is null)
        {
            throw new ArtifactRefusedException(
                "There is nothing on standard input. A backup artifact is a tar, and "
                + "this command reads it from there.");
        }

        if (first.Name != "manifest.json" || first.DataStream is not { } content)
        {
            throw new ArtifactRefusedException(
                $"This is not a logaffe backup: the first thing in it is {first.Name} "
                + "rather than manifest.json.");
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<BackupManifest>(
                       content, ManifestFormat, cancellationToken)
                   ?? throw new ArtifactRefusedException(
                       "The artifact's manifest.json is empty.");
        }
        catch (JsonException cause)
        {
            throw new ArtifactRefusedException(
                $"The artifact's manifest.json cannot be read: {cause.Message}");
        }
    }

    private void Refuse(BackupManifest manifest)
    {
        // Every field is required, and JSON has no way to say so: a document
        // missing one deserializes into nulls, and the first thing to touch one
        // would report a null argument rather than a bad artifact. Read as a
        // whole first, so that everything after this can trust what it holds.
        if (string.IsNullOrWhiteSpace(manifest.Migration)
            || manifest.Volume is null
            || manifest.Tables is null)
        {
            throw new ArtifactRefusedException(
                "This artifact's manifest.json is missing something this version needs "
                + "— it names no schema, no volume, or no tables. Nothing has been "
                + "changed.");
        }

        // The same comparison the installation makes at startup against a
        // database a later build migrated, asked here of the artifact instead
        // (ADR 0037). There is no downgrade path, so an artifact from a newer
        // logaffe is refused rather than attempted: the schema has moved and the
        // code behind it has not.
        if (SchemaVersions.NotKnownHere([manifest.Migration], database.KnownMigrations).Count > 0)
        {
            throw new ArtifactRefusedException(
                $"This artifact was taken from logaffe {manifest.Logaffe}, at a schema "
                + $"this version does not know ({manifest.Migration}). Restore it into "
                + "that version or a newer one. Nothing has been changed.");
        }

        if (!manifest.Volume.Contains(TakeABackup.KeyFile, StringComparer.Ordinal))
        {
            throw new ArtifactRefusedException(
                $"This artifact holds no {TakeABackup.KeyFile}, so restoring it would "
                + "produce an installation whose every token is undecryptable. Both "
                + "halves or neither (ADR 0024). Nothing has been changed.");
        }

        if (manifest.Tables.Count == 0)
        {
            throw new ArtifactRefusedException(
                "This artifact holds no tables. Nothing has been changed.");
        }
    }
}
