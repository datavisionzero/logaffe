using System.Security.Cryptography;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

public sealed class CheckTheKeyFitsTests
{
    [Fact]
    public async Task An_installation_holding_nothing_has_nothing_to_be_wrong_about()
    {
        var secrets = new StubSecrets();

        Assert.Equal(KeyFit.NothingSealed, await Check(secrets, new StubCipher()));

        // The ordinary first start, and it must not turn into a refusal.
        Assert.Equal(CheckTheKeyFits.SampleSize, secrets.Asked);
    }

    [Fact]
    public async Task A_key_that_opens_what_is_stored_fits() =>
        Assert.Equal(
            KeyFit.Fits,
            await Check(new StubSecrets([1], [2]), new StubCipher()));

    [Fact]
    public async Task A_key_that_opens_none_of_the_sample_does_not_fit() =>
        // Both stores are here and they are not two halves of one installation.
        Assert.Equal(
            KeyFit.DoesNotFit,
            await Check(new StubSecrets([1], [2], [3]), new StubCipher { Unreadable = [1, 2, 3] }));

    [Fact]
    public async Task One_corrupt_row_is_not_a_wrong_key() =>
        // Refusing to start is for a key that opens nothing, not for a row that
        // somebody wrote over.
        Assert.Equal(
            KeyFit.Fits,
            await Check(new StubSecrets([1], [2], [3]), new StubCipher { Unreadable = [1] }));

    private static Task<KeyFit> Check(ISealedSecrets secrets, ISecretCipher cipher) =>
        new CheckTheKeyFits(secrets, cipher).ExecuteAsync(TestContext.Current.CancellationToken);

    private sealed class StubSecrets(params byte[][] sample) : ISealedSecrets
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<byte[]>> SampleAsync(
            int count, CancellationToken cancellationToken)
        {
            Asked = count;
            return Task.FromResult<IReadOnlyList<byte[]>>(sample.Take(count).ToArray());
        }
    }

    private sealed class StubCipher : ISecretCipher
    {
        /// <summary>The first byte of every sealed value this cipher refuses.</summary>
        public byte[] Unreadable { get; init; } = [];

        public byte[] Encrypt(string secret) => throw new NotSupportedException();

        public string Decrypt(byte[] sealedSecret) =>
            Unreadable.Contains(sealedSecret[0])
                ? throw new CryptographicException("Not this key.")
                : "opened";
    }
}
