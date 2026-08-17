using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// The file a drawn claim secret is handed over in, on the host volume beside
/// the key.
/// </summary>
/// <remarks>
/// <para>
/// It is a delivery copy rather than a store (ADR 0040): what decides whether a
/// presented secret is the right one is the hash in the database, and this is the
/// only form the secret itself ever takes. Whoever installs reads it and hands it
/// to whoever is going to claim.
/// </para>
/// <para>
/// Readable by its owner and nobody else, set as the file is created rather than
/// afterwards, exactly as <see cref="HostVolumeKey"/> does it — so there is no
/// moment at which it is not.
/// </para>
/// </remarks>
public sealed class ClaimSecretFile(string volumePath) : IClaimSecretHandover
{
    public string Path { get; } = System.IO.Path.Combine(volumePath, "claim-secret.txt");

    public async Task WriteAsync(ClaimSecret secret, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var options = new FileStreamOptions
        {
            // Replacing whatever was there: Host Recovery draws a fresh secret,
            // and the one this overwrites is void the moment it does.
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var file = new FileStream(Path, options);
        await using var writer = new StreamWriter(file);

        // With a newline, because this is read out of a terminal and pasted into
        // a browser, and a value that runs into a prompt gets copied wrong.
        await writer.WriteLineAsync(secret.Text.AsMemory(), cancellationToken);
    }

    public void Remove() => File.Delete(Path);
}
