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

    /// <summary>
    /// Opens one for writing, replacing whatever was there and making the
    /// directories above it.
    /// </summary>
    /// <remarks>
    /// A restore puts the artifact's files over the volume's rather than
    /// emptying it first. What has to be right is that the key the restored
    /// database was sealed under is the key that ends up here, and overwriting
    /// says that exactly; a file the artifact does not carry cannot make the
    /// restored installation wrong, and one of them is the log this command is
    /// writing its own account of itself into (ADR 0002).
    /// </remarks>
    Stream Create(string relativePath);
}
