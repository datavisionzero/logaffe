using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class OperatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] SealedSecret = [1, 2, 3, 4];

    private const string Hash = "AQAAAAIAAYagAAAAE-not-a-real-hash";

    [Fact]
    public void A_claimed_account_is_a_password_and_nothing_else()
    {
        var theOperator = Operator.Claim(Hash, Now);

        Assert.NotEqual(Guid.Empty, theOperator.Id);
        Assert.Equal(Hash, theOperator.PasswordHash);
        Assert.Equal(Now, theOperator.ClaimedAt);

        // The second factor is the operator's to enrol afterwards (ADR 0041), so
        // an account that has none is an ordinary account rather than a
        // half-built one.
        Assert.False(theOperator.HasSecondFactor);
        Assert.Null(theOperator.EncryptedSecondFactorSecret);
        Assert.Null(theOperator.SecondFactorEnrolledAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_operator_holds_their_password_hashed(string hash) =>
        Assert.Throws<ArgumentException>(() => Operator.Claim(hash, Now));

    [Fact]
    public void A_hash_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => Operator.Claim(
            new string('x', Operator.PasswordHashMaxLength + 1), Now));

    [Fact]
    public void Enrolling_takes_the_secret_and_the_date_together()
    {
        var theOperator = Operator.Claim(Hash, Now);

        theOperator.EnrolSecondFactor(SealedSecret, Now.AddDays(1));

        Assert.True(theOperator.HasSecondFactor);
        Assert.Equal(SealedSecret, theOperator.EncryptedSecondFactorSecret);
        Assert.Equal(Now.AddDays(1), theOperator.SecondFactorEnrolledAt);
    }

    [Fact]
    public void Rehashing_keeps_the_same_password_at_the_current_cost()
    {
        var theOperator = Enrolled();

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
        var theOperator = Enrolled();

        theOperator.ChangePasswordTo("AQAAAAIAAyagAAAAE-chosen");

        Assert.Equal("AQAAAAIAAyagAAAAE-chosen", theOperator.PasswordHash);
        Assert.Equal(SealedSecret, theOperator.EncryptedSecondFactorSecret);
    }

    [Fact]
    public void Enrolling_over_one_overwrites_the_secret_and_keeps_the_account()
    {
        var theOperator = Enrolled();
        var identity = theOperator.Id;

        theOperator.EnrolSecondFactor([9, 9, 9], Now.AddYears(1));

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
    public void Enrolling_with_nothing_sealed_is_refused() =>
        // A row without it is an account that cannot verify a code, which is a
        // corrupt account rather than one with no second factor.
        Assert.Throws<ArgumentException>(
            () => Operator.Claim(Hash, Now).EnrolSecondFactor([], Now));

    [Fact]
    public void Removing_it_leaves_the_account_behind_its_password_alone()
    {
        var theOperator = Enrolled();

        theOperator.RemoveSecondFactor();

        Assert.False(theOperator.HasSecondFactor);
        Assert.Null(theOperator.EncryptedSecondFactorSecret);

        // Both together, so that a date without a secret is not a state this
        // type can be in.
        Assert.Null(theOperator.SecondFactorEnrolledAt);

        Assert.Equal(Hash, theOperator.PasswordHash);
        Assert.Equal(Now, theOperator.ClaimedAt);
    }

    /// <summary>An account with a second factor, which is what enrolling makes.</summary>
    private static Operator Enrolled()
    {
        var theOperator = Operator.Claim(Hash, Now);
        theOperator.EnrolSecondFactor(SealedSecret, Now);

        return theOperator;
    }
}
