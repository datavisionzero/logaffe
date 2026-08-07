using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class IngestTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Project = Guid.CreateVersion7();

    private static readonly byte[] Ciphertext = [1, 2, 3, 4];

    [Fact]
    public void An_issued_token_belongs_to_a_project_and_has_never_been_used()
    {
        var identifier = TokenIdentifier.Mint();

        var token = IngestToken.Issue(Project, identifier, Ciphertext, Now);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(Project, token.ProjectId);
        Assert.Equal(identifier, token.Identifier);
        Assert.Equal(Ciphertext, token.EncryptedSecret);
        Assert.Equal(Now, token.IssuedAt);
        // Which is what tells a token that was issued and never deployed apart
        // from one that has gone quiet.
        Assert.Null(token.LastUsedAt);
    }

    [Fact]
    public void A_token_without_its_ciphertext_is_a_corrupt_row_not_a_revoked_one() =>
        Assert.Throws<ArgumentException>(
            () => IngestToken.Issue(Project, TokenIdentifier.Mint(), [], Now));

    [Fact]
    public void Using_a_token_records_when()
    {
        var token = IngestToken.Issue(Project, TokenIdentifier.Mint(), Ciphertext, Now);

        token.WasUsedAt(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), token.LastUsedAt);
    }

    [Fact]
    public void A_delivery_arriving_out_of_order_cannot_make_a_token_look_quieter()
    {
        var token = IngestToken.Issue(Project, TokenIdentifier.Mint(), Ciphertext, Now);
        token.WasUsedAt(Now.AddMinutes(5));

        token.WasUsedAt(Now.AddMinutes(1));

        // Rotation is finished when the old token's last use stops moving, so a
        // last use that can move backwards would answer the wrong question.
        Assert.Equal(Now.AddMinutes(5), token.LastUsedAt);
    }
}
