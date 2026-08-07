using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Logaffe.Infrastructure.Secrets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The password hasher, which needs no database and lives here because this is
/// the project that can see an adapter.
/// </summary>
/// <remarks>
/// The thing worth proving is the one the product leans on: the format carries
/// its own parameters, so a hash written at an older cost is recognized as such
/// and rewritten on the next sign-in. That is what makes raising the cost later
/// a path rather than an intention (ADR 0032).
/// </remarks>
public sealed class PasswordHasherTests
{
    private static readonly Password Chosen = Password.Create("correct horse battery staple");

    private readonly FrameworkPasswordHasher hasher = new();

    [Fact]
    public void The_password_proves_itself() =>
        Assert.Equal(PasswordCheck.Right, hasher.Verify(hasher.Hash(Chosen), Chosen));

    [Fact]
    public void Another_password_does_not() =>
        Assert.Equal(
            PasswordCheck.Wrong,
            hasher.Verify(hasher.Hash(Chosen), Password.Create("correct horse battery stapl")));

    [Fact]
    public void One_password_hashes_to_two_different_things() =>
        // Salted, so a dump does not say which of two accounts share a password
        // — and this product has one account, so it says nothing at all.
        Assert.NotEqual(hasher.Hash(Chosen), hasher.Hash(Chosen));

    [Fact]
    public void A_hash_written_at_an_older_cost_is_right_and_out_of_date()
    {
        var older = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = FrameworkPasswordHasher.IterationCount / 2,
        })).HashPassword(new object(), Chosen.Text);

        // It admits exactly as a current hash does; what it also does is tell
        // the caller it owes the row a rewrite.
        Assert.Equal(PasswordCheck.RightAndOutOfDate, hasher.Verify(older, Chosen));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash at all")]
    [InlineData("AAAA")]
    public void A_hash_this_cannot_read_is_a_wrong_password(string stored) =>
        // A row somebody wrote over, and there is nothing else it could
        // truthfully be turned into: there is no reset over the network to offer
        // instead (ADR 0015).
        Assert.Equal(PasswordCheck.Wrong, hasher.Verify(stored, Chosen));
}
