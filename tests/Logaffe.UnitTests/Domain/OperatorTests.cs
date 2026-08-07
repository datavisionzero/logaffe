using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class OperatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] SealedSecret = [1, 2, 3, 4];

    private const string Hash = "AQAAAAIAAYagAAAAE-not-a-real-hash";

    [Fact]
    public void A_claimed_account_is_enrolled_at_the_moment_it_is_claimed()
    {
        var theOperator = Operator.Claim(Hash, SealedSecret, Now);

        Assert.NotEqual(Guid.Empty, theOperator.Id);
        Assert.Equal(Hash, theOperator.PasswordHash);
        Assert.Equal(SealedSecret, theOperator.EncryptedSecondFactorSecret);
        // The claim is one act: there is no operator without a second factor,
        // so there is no moment between the two to have a date of its own.
        Assert.Equal(Now, theOperator.SecondFactorEnrolledAt);
        Assert.Equal(Now, theOperator.ClaimedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_operator_holds_their_password_hashed(string hash) =>
        Assert.Throws<ArgumentException>(() => Operator.Claim(hash, SealedSecret, Now));

    [Fact]
    public void A_hash_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => Operator.Claim(
            new string('x', Operator.PasswordHashMaxLength + 1), SealedSecret, Now));

    [Fact]
    public void An_operator_holds_their_second_factor_encrypted() =>
        // A row without it is an account that cannot verify a code, which is a
        // corrupt account rather than a claimable one.
        Assert.Throws<ArgumentException>(() => Operator.Claim(Hash, [], Now));

    [Fact]
    public void Rehashing_keeps_the_same_password_at_the_current_cost()
    {
        var theOperator = Operator.Claim(Hash, SealedSecret, Now);

        theOperator.RehashedTo("AQAAAAIAAyagAAAAE-rewritten");

        // Maintenance nobody asked for: the account is otherwise exactly as it
        // was, and in particular the second factor is untouched.
        Assert.Equal("AQAAAAIAAyagAAAAE-rewritten", theOperator.PasswordHash);
        Assert.Equal(Now, theOperator.SecondFactorEnrolledAt);
        Assert.Equal(SealedSecret, theOperator.EncryptedSecondFactorSecret);
    }

    [Fact]
    public void Changing_the_password_leaves_the_second_factor_alone()
    {
        var theOperator = Operator.Claim(Hash, SealedSecret, Now);

        theOperator.ChangePasswordTo("AQAAAAIAAyagAAAAE-chosen");

        Assert.Equal("AQAAAAIAAyagAAAAE-chosen", theOperator.PasswordHash);
        Assert.Equal(SealedSecret, theOperator.EncryptedSecondFactorSecret);
    }

    [Fact]
    public void Re_enrolling_overwrites_the_secret_and_keeps_the_account()
    {
        var theOperator = Operator.Claim(Hash, SealedSecret, Now);
        var identity = theOperator.Id;

        theOperator.ReEnrolSecondFactor([9, 9, 9], Now.AddYears(1));

        // An overwrite, not a second enrolment beside the first: nothing of the
        // previous secret survives, and what is kept of it is the date it
        // stopped being current (ADR 0032).
        Assert.Equal(new byte[] { 9, 9, 9 }, theOperator.EncryptedSecondFactorSecret);
        Assert.Equal(Now.AddYears(1), theOperator.SecondFactorEnrolledAt);
        Assert.Equal(identity, theOperator.Id);
        Assert.Equal(Now, theOperator.ClaimedAt);
        Assert.Equal(Hash, theOperator.PasswordHash);
    }

    [Fact]
    public void Re_enrolling_with_nothing_sealed_is_refused() =>
        Assert.Throws<ArgumentException>(
            () => Operator.Claim(Hash, SealedSecret, Now).ReEnrolSecondFactor([], Now));
}
