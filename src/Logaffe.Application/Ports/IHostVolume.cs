namespace Logaffe.Application.Ports;

/// <summary>
/// The other half of an installation: everything that is not in the database.
/// </summary>
/// <remarks>
/// The encryption key above all (ADR 0022), which is why a backup that holds one
/// half and not the other is not a backup of this product (ADR 0024) — a
/// database restored without its key produces an installation whose every token
/// is undecryptable, discovered at the moment the operator most needs it not to
/// be.
/// </remarks>
public interface IHostVolume
{
    /// <summary>Where it is, for a sentence that has to name it.</summary>
    string Path { get; }

    /// <summary>
    /// Every file on the volume, as a path relative to its root, in a stable
    /// order.
    /// </summary>
    IReadOnlyList<string> Files();

    /// <summary>
    /// Opens one of them for reading, sharing it: the file log on this volume is
    /// held open by the installation this is running beside.
    /// </summary>
    Stream OpenRead(string relativePath);
}
