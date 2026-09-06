using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Logaffe.Infrastructure.Secrets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The password hasher, which needs no database and lives here because this is
/// the project that can see an adapter.
/// </summary>
/// <remarks>
/// Two things are worth proving and the product leans on both. The format
/// carries its own parameters, so a hash written at older ones is recognized as
/// such and rewritten on the next sign-in — which is what makes raising the cost
/// later a path rather than an intention. And <b>a PBKDF2 hash from before
/// ADR 0057 still admits</b>, which is what makes the move to Argon2id something
/// nobody has to be told about.
/// </remarks>
public sealed class PasswordHasherTests
{
    private static readonly Password Chosen = Password.Create("correct horse battery staple");

    private readonly Argon2idPasswordHasher hasher = new();

    [Fact]
    public void The_password_proves_itself() =>
        Assert.Equal(PasswordCheck.Right, hasher.Verify(hasher.Hash(Chosen), Chosen));

    [Fact]
    public void What_it_writes_is_argon2id_carrying_its_own_parameters()
    {
        var written = hasher.Hash(Chosen);

        Assert.StartsWith("$argon2id$v=19$", written, StringComparison.Ordinal);
        Assert.Contains(
            $"m={Argon2idPasswordHasher.MemoryKibibytes},"
            + $"t={Argon2idPasswordHasher.Iterations},"
            + $"p={Argon2idPasswordHasher.Parallelism}",
            written,
            StringComparison.Ordinal);

        // The column holds 256 characters, which is the room ADR 0057 says the
        // next set of parameters has to fit in.
        Assert.True(written.Length <= User.PasswordHashMaxLength);
    }

    [Fact]
    public void Another_password_does_not() =>
        Assert.Equal(
            PasswordCheck.Wrong,
            hasher.Verify(hasher.Hash(Chosen), Password.Create("correct horse battery stapl")));

    [Fact]
    public void One_password_hashes_to_two_different_things() =>
        // Salted, so a stolen dump does not say which two accounts share a
        // password.
        Assert.NotEqual(hasher.Hash(Chosen), hasher.Hash(Chosen));

    [Fact]
    public void A_hash_written_at_older_parameters_is_right_and_out_of_date()
    {
        // Written the way an installation on cheaper parameters would have
        // written it, and derived at those parameters rather than at this
        // file's — which is the property under test: what the candidate is
        // hashed with comes out of the stored value.
        const int WasMemory = 19_456;
        const int WasIterations = 2;

        var salt = RandomNumberGenerator.GetBytes(16);

        using var argon = new Argon2id(Encoding.UTF8.GetBytes(Chosen.Text))
        {
            Salt = salt,
            MemorySize = WasMemory,
            Iterations = WasIterations,
            DegreeOfParallelism = 1,
        };

        var older = $"$argon2id$v=19$m={WasMemory},t={WasIterations},p=1$"
            + $"{Convert.ToBase64String(salt)}$"
            + Convert.ToBase64String(argon.GetBytes(32));

        // It admits exactly as a current hash does; what it also does is tell
        // the caller it owes the row a rewrite, which is what makes raising the
        // parameters later a path rather than an intention.
        Assert.Equal(PasswordCheck.RightAndOutOfDate, hasher.Verify(older, Chosen));
    }

    [Fact]
    public void A_pbkdf2_hash_from_before_the_move_still_admits()
    {
        var written = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 210_000,
        })).HashPassword(new object(), Chosen.Text);

        // Nobody is locked out by the move and nobody resets anything: the old
        // format is read, and it comes back owing the row a rewrite, which the
        // next successful sign-in performs (ADR 0057).
        Assert.Equal(PasswordCheck.RightAndOutOfDate, hasher.Verify(written, Chosen));

        // And a wrong password against an old hash is still a wrong password.
        Assert.Equal(
            PasswordCheck.Wrong,
            hasher.Verify(written, Password.Create("correct horse battery stapl")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash at all")]
    [InlineData("AAAA")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$not-base64$also-not")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    [InlineData("$argon2id$v=19$m=99999999,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAA")]
    public void A_hash_this_cannot_read_is_a_wrong_password(string stored) =>
        // A row somebody wrote over, and there is nothing else it could
        // truthfully be turned into. The last case is the one worth naming: a
        // stored value asking for a gigabyte of memory would turn a sign-in into
        // an hour of work, so what it may ask for is bounded.
        Assert.Equal(PasswordCheck.Wrong, hasher.Verify(stored, Chosen));
}
