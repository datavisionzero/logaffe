using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// The encryption key, kept on the host volume beside the rest of the
/// installation's secrets and never in the database.
/// </summary>
/// <remarks>
/// It is read on first use rather than at startup, because registering a service
/// has to stay free of side effects — the OpenAPI tooling builds the host at
/// compile time and has neither a volume nor a database.
/// </remarks>
public sealed class HostVolumeKey
{
    public const int LengthInBytes = 32;

    private readonly string path;
    private readonly ILogger logger;
    private readonly Lazy<byte[]> material;

    public HostVolumeKey(string volumePath, ILogger<HostVolumeKey> logger)
    {
        path = Path.Combine(volumePath, "keys", "token.key");
        this.logger = logger;
        material = new Lazy<byte[]>(ReadOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The key itself, read from the volume or written there.</summary>
    public byte[] Material => material.Value;

    private byte[] ReadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Create-or-read rather than exists-then-create: two containers starting
        // at once is an ordinary event in this product, and two of them writing
        // a key each would leave whichever lost holding tokens it cannot read.
        // Only one CreateNew can win, and the loser reads what the winner wrote.
        if (TryCreate(out var created))
        {
            // Ordinary on a first start, and not an alarm on its own: whether a
            // new key is the wrong key is a question about what the database
            // holds, and the startup check is what asks it.
            logger.LogInformation(
                "Wrote a new token encryption key to {Path}. Back it up together "
                + "with the database — neither store is useful without the other.",
                path);
            return created;
        }

        return Read();
    }

    private bool TryCreate(out byte[] key)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
        };

        // Readable by its owner and nobody else, set as the file is created
        // rather than afterwards, so there is no moment at which it is not.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        key = RandomNumberGenerator.GetBytes(LengthInBytes);

        try
        {
            using var file = new FileStream(path, options);
            using var writer = new StreamWriter(file);
            // Base64 with a newline: the operator has to be able to copy this
            // out of a terminal and back in without a hex editor.
            writer.WriteLine(Convert.ToBase64String(key));
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            key = null!;
            return false;
        }
    }

    private byte[] Read()
    {
        var text = File.ReadAllText(path).Trim();

        byte[] key;
        try
        {
            key = Convert.FromBase64String(text);
        }
        catch (FormatException cause)
        {
            throw new InvalidOperationException(
                $"The token encryption key at {path} is not base64. Restoring a "
                + "backup puts both halves back together.",
                cause);
        }

        return key.Length == LengthInBytes
            ? key
            : throw new InvalidOperationException(
                $"The token encryption key at {path} is {key.Length} bytes and "
                + $"has to be {LengthInBytes}.");
    }
}
