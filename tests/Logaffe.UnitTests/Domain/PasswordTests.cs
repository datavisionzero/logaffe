using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class PasswordTests
{
    [Fact]
    public void A_password_is_what_was_typed()
    {
        var password = Password.Create(" correct horse battery ");

        // Not trimmed and not normalized: what the operator typed is what they
        // will type again.
        Assert.Equal(" correct horse battery ", password.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("fifteencharacte")]
    public void A_password_that_is_too_short_is_refused(string value) =>
        Assert.False(Password.TryCreate(value, out _));

    [Theory]
    [InlineData("short")]
    [InlineData("fifteencharacte")]
    public void A_password_being_presented_is_read_however_short_it_is(string value)
    {
        // The minimum is a rule about choosing (ADR 0042). Applying it to a
        // password being proved would mean that raising it locks out every
        // operator whose password was long enough when they set it, which is a
        // rule that arrives with an upgrade and takes the installation with it.
        Assert.True(Password.TryRead(value, out var presented));
        Assert.Equal(value, presented.Text);
    }

    [Fact]
    public void A_password_that_was_not_typed_at_all_is_not_one() =>
        Assert.False(Password.TryRead(string.Empty, out _));

    [Fact]
    public void The_bound_on_the_hasher_holds_for_a_presented_password_too() =>
        Assert.False(
            Password.TryRead(new string('x', Password.MaximumLength + 1), out _));

    [Fact]
    public void The_minimum_is_a_length_the_operator_can_reach() =>
        Assert.True(Password.TryCreate(new string('x', Password.MinimumLength), out _));

    [Fact]
    public void A_password_that_would_be_a_denial_of_service_is_refused() =>
        // Hashing is deliberately slow and the sign-in surface is public, so the
        // bound is on the work rather than on the password.
        Assert.False(Password.TryCreate(new string('x', Password.MaximumLength + 1), out _));

    [Fact]
    public void Creating_an_impossible_password_says_so() =>
        Assert.Throws<ArgumentException>(() => Password.Create("too short"));

    [Fact]
    public void A_password_carries_nothing_into_a_log_line() =>
        // Reached by an interpolation somewhere, this must not be the password.
        Assert.DoesNotContain(
            "correct horse battery", Password.Create("correct horse battery").ToString());
}
