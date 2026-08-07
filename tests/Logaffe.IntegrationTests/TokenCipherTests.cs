using System.Security.Cryptography;
using Logaffe.Domain.Tokens;
using Logaffe.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The cipher and the key it reads off the volume. This needs no database and a
/// real filesystem, which is why it is here rather than beside the domain rules:
/// the key file, its permissions and what happens when two of them race are
/// exactly the parts no substitute can vouch for.
/// </summary>
public sealed class TokenCipherTests : IDisposable
{
    private readonly string volume = Directory.CreateTempSubdirectory("logaffe-key-").FullName;

    public void Dispose() => Directory.Delete(volume, recursive: true);

    [Fact]
    public void A_sealed_secret_opens_to_what_went_in()
    {
        var cipher = CipherOn(volume);
        var token = TokenText.Mint(TokenKind.Ingest);

        var sealedSecret = cipher.Encrypt(token.Secret);

        Assert.Equal(token.Secret, cipher.Decrypt(sealedSecret));
    }

    [Fact]
    public void One_secret_sealed_twice_gives_two_values()
    {
        var cipher = CipherOn(volume);
        var token = TokenText.Mint(TokenKind.Ingest);

        var first = cipher.Encrypt(token.Secret);
        var second = cipher.Encrypt(token.Secret);

        // The property the whole of ADR 0031 turns on: a ciphertext cannot be
        // looked up by the value presented, which is why a token names its row.
        Assert.NotEqual(first, second);
        Assert.Equal(token.Secret, cipher.Decrypt(first));
        Assert.Equal(token.Secret, cipher.Decrypt(second));
    }

    [Fact]
    public void A_row_that_was_altered_does_not_open()
    {
        var cipher = CipherOn(volume);
        var sealedSecret = cipher.Encrypt(TokenText.Mint(TokenKind.Ingest).Secret);

        sealedSecret[^1] ^= 0xFF;

        // GCM authenticates as well as encrypts, so this fails rather than
        // opening as something else.
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(sealedSecret));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(28)]
    public void Something_this_cipher_did_not_seal_does_not_open(int length) =>
        Assert.ThrowsAny<CryptographicException>(
            () => CipherOn(volume).Decrypt(new byte[length]));

    [Fact]
    public void Another_installations_key_does_not_open_it()
    {
        var sealedSecret = CipherOn(volume).Encrypt(TokenText.Mint(TokenKind.Agent).Secret);

        var elsewhere = Directory.CreateTempSubdirectory("logaffe-key-").FullName;
        try
        {
            // A database restored without its key is an installation whose every
            // token is undecryptable, and this is that in one assertion.
            Assert.ThrowsAny<CryptographicException>(
                () => CipherOn(elsewhere).Decrypt(sealedSecret));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void The_key_is_written_once_and_read_back()
    {
        var token = TokenText.Mint(TokenKind.Ingest);
        var sealedSecret = CipherOn(volume).Encrypt(token.Secret);

        // A second container against the same volume: it finds the key rather
        // than writing one, which is what makes a restart uneventful.
        Assert.Equal(token.Secret, CipherOn(volume).Decrypt(sealedSecret));
    }

    [Fact]
    public void A_key_is_readable_by_its_owner_and_nobody_else()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes.");
            return;
        }

        _ = KeyOn(volume).Material;

        var mode = File.GetUnixFileMode(Path.Combine(volume, "keys", "token.key"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void A_key_file_that_is_not_a_key_is_refused()
    {
        var keyPath = Path.Combine(volume, "keys", "token.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        File.WriteAllText(keyPath, "this is not a key");

        // Loudly, because the alternative is an installation that starts and
        // then cannot read a single token it holds.
        Assert.Throws<InvalidOperationException>(() => KeyOn(volume).Material);
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused()
    {
        var keyPath = Path.Combine(volume, "keys", "token.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        File.WriteAllText(keyPath, Convert.ToBase64String(new byte[16]));

        Assert.Throws<InvalidOperationException>(() => KeyOn(volume).Material);
    }

    private static HostVolumeKey KeyOn(string volumePath) =>
        new(volumePath, NullLogger<HostVolumeKey>.Instance);

    private static AesGcmTokenCipher CipherOn(string volumePath) => new(KeyOn(volumePath));
}
