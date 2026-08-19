using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class HostTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static HostToken Issued(Guid hostId) =>
        HostToken.Issue(hostId, TokenIdentifier.Mint(), [1, 2, 3], Now);

    [Fact]
    public void An_issued_token_names_its_host_and_has_never_been_used()
    {
        var host = Guid.CreateVersion7();
        var token = Issued(host);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(host, token.HostId);
        Assert.Equal(Now, token.IssuedAt);
        Assert.Null(token.LastUsedAt);
    }

    [Fact]
    public void An_empty_secret_is_not_a_stored_secret() =>
        Assert.Throws<ArgumentException>(
            () => HostToken.Issue(Guid.CreateVersion7(), TokenIdentifier.Mint(), [], Now));

    [Fact]
    public void A_use_is_recorded()
    {
        var token = Issued(Guid.CreateVersion7());
        var used = Now.AddMinutes(3);

        token.WasUsedAt(used);

        Assert.Equal(used, token.LastUsedAt);
    }

    /// <summary>
    /// Time only moves forward here, so a delivery arriving out of order behind
    /// another cannot make a token look quieter than it is — which is the whole
    /// of what makes a rotation finishable by watching the old one go quiet.
    /// </summary>
    [Fact]
    public void A_use_never_moves_the_last_use_backwards()
    {
        var token = Issued(Guid.CreateVersion7());
        var later = Now.AddMinutes(10);

        token.WasUsedAt(later);
        token.WasUsedAt(Now.AddMinutes(2));

        Assert.Equal(later, token.LastUsedAt);
    }
}
