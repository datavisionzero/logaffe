using Logaffe.Domain.Identities;

namespace Logaffe.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] SealedSecret = [1, 2, 3, 4];

    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$not-a-real-hash";

    private const string Address = "Somebody@Example.COM";

    [Fact]
    public void A_bootstrapped_administrator_is_invited_until_the_token_is_exchanged()
    {
        var user = User.Bootstrap("The Administrator", Address, Now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(IdentityKind.User, user.Kind);
        Assert.True(user.Administrator);
        Assert.Equal(Now, user.CreatedAt);

        // Invited and without a password, which is what makes the bootstrap
        // token single-use without anything being stored (ADR 0054).
        Assert.Equal(UserState.Invited, user.State);
        Assert.Null(user.PasswordHash);
        Assert.False(user.IsActive);

        // The second factor is each user's to enrol afterwards (ADR 0041), so an
        // account that has none is an ordinary account rather than a half-built
        // one.
        Assert.False(user.HasSecondFactor);
        Assert.Null(user.EncryptedSecondFactorSecret);
        Assert.Null(user.SecondFactorEnrolledAt);
    }

    [Fact]
    public void The_address_is_kept_as_written_and_folded_for_comparison()
    {
        var user = User.Bootstrap("The Administrator", "  " + Address + " ", Now);

        // What is shown is what was typed; what is looked up is what two
        // spellings of it have in common (ADR 0052).
        Assert.Equal(Address, user.Email);
        Assert.Equal("somebody@example.com", user.NormalizedEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    [InlineData("Someone <someone@example.com>")]
    public void Something_that_is_not_an_address_is_refused(string email) =>
        Assert.Throws<ArgumentException>(() => User.Bootstrap("Somebody", email, Now));

    [Fact]
    public void An_address_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => User.Bootstrap(
            "Somebody", new string('x', User.EmailMaxLength) + "@example.com", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_identity_has_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => User.Bootstrap(name, Address, Now));

    [Fact]
    public void A_name_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => User.Bootstrap(
            new string('x', Identity.NameMaxLength + 1), Address, Now));

    [Fact]
    public void Redeeming_an_invitation_sets_the_first_password_and_activates()
    {
        var user = User.Invite("Somebody", Address, administrator: false, Now);

        user.ActivateWith(Hash);

        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(UserState.Active, user.State);
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_user_holds_their_password_hashed(string hash) =>
        Assert.Throws<ArgumentException>(() => Active().ChangePasswordTo(hash));

    [Fact]
    public void A_hash_that_will_not_fit_the_column_is_refused_here() =>
        Assert.Throws<ArgumentException>(() => Active().ChangePasswordTo(
            new string('x', User.PasswordHashMaxLength + 1)));

    [Fact]
    public void Deactivating_keeps_the_row_and_reactivating_restores_the_state()
    {
        var user = Active();

        user.Deactivate();

        Assert.Equal(UserState.Deactivated, user.State);
        Assert.False(user.IsActive);

        user.Reactivate();

        Assert.Equal(UserState.Active, user.State);
    }

    [Fact]
    public void Reactivating_somebody_who_never_arrived_leaves_them_invited()
    {
        var user = User.Invite("Somebody", Address, administrator: false, Now);
        user.Deactivate();

        user.Reactivate();

        // An account with no password must not become active on the way back:
        // there is nothing for it to sign in with.
        Assert.Equal(UserState.Invited, user.State);
    }

    [Fact]
    public void Enrolling_takes_the_secret_and_the_date_together()
    {
        var user = Active();

        user.EnrolSecondFactor(SealedSecret, Now.AddDays(1));

        Assert.True(user.HasSecondFactor);
        Assert.Equal(SealedSecret, user.EncryptedSecondFactorSecret);
        Assert.Equal(Now.AddDays(1), user.SecondFactorEnrolledAt);
    }

    [Fact]
    public void Rehashing_keeps_the_same_password_at_the_current_cost()
    {
        var user = Enrolled();

        user.RehashedTo("$argon2id$v=19$m=19456,t=3,p=1$rewritten");

        // Maintenance nobody asked for: the account is otherwise exactly as it
        // was, and in particular the second factor is untouched. This is the
        // step that carries an installation off PBKDF2 (ADR 0057).
        Assert.Equal("$argon2id$v=19$m=19456,t=3,p=1$rewritten", user.PasswordHash);
        Assert.Equal(Now, user.SecondFactorEnrolledAt);
        Assert.Equal(SealedSecret, user.EncryptedSecondFactorSecret);
    }

    [Fact]
    public void Changing_the_password_leaves_the_second_factor_alone()
    {
        var user = Enrolled();

        user.ChangePasswordTo("$argon2id$v=19$m=19456,t=2,p=1$chosen");

        Assert.Equal("$argon2id$v=19$m=19456,t=2,p=1$chosen", user.PasswordHash);
        Assert.Equal(SealedSecret, user.EncryptedSecondFactorSecret);
    }

    [Fact]
    public void Changing_the_address_moves_both_forms_together()
    {
        var user = Active();

        user.ChangeEmailTo("Someone.Else@Example.com");

        Assert.Equal("Someone.Else@Example.com", user.Email);
        Assert.Equal("someone.else@example.com", user.NormalizedEmail);
    }

    [Fact]
    public void Enrolling_over_one_overwrites_the_secret_and_keeps_the_account()
    {
        var user = Enrolled();
        var identity = user.Id;

        user.EnrolSecondFactor([9, 9, 9], Now.AddYears(1));

        // An overwrite, not a second enrolment beside the first: nothing of the
        // previous secret survives, and what is kept of it is the date it
        // stopped being current (ADR 0057).
        Assert.Equal(new byte[] { 9, 9, 9 }, user.EncryptedSecondFactorSecret);
        Assert.Equal(Now.AddYears(1), user.SecondFactorEnrolledAt);
        Assert.Equal(identity, user.Id);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Hash, user.PasswordHash);
    }

    [Fact]
    public void Enrolling_with_nothing_sealed_is_refused() =>
        // A row without it is an account that cannot verify a code, which is a
        // corrupt account rather than one with no second factor.
        Assert.Throws<ArgumentException>(() => Active().EnrolSecondFactor([], Now));

    [Fact]
    public void Removing_it_leaves_the_account_behind_its_password_alone()
    {
        var user = Enrolled();

        user.RemoveSecondFactor();

        Assert.False(user.HasSecondFactor);
        Assert.Null(user.EncryptedSecondFactorSecret);

        // Both together, so that a date without a secret is not a state this
        // type can be in.
        Assert.Null(user.SecondFactorEnrolledAt);

        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(Now, user.CreatedAt);
    }

    [Fact]
    public void An_agent_belongs_to_a_user_and_is_never_an_administrator()
    {
        var owner = Active();

        var agent = Agent.Create("the terminal agent", owner.Id, Now);

        Assert.Equal(IdentityKind.Agent, agent.Kind);
        Assert.Equal(owner.Id, agent.OwnerId);
        Assert.False(agent.Administrator);
    }

    /// <summary>An account that has been through the exchange or an invitation.</summary>
    private static User Active()
    {
        var user = User.Bootstrap("The Administrator", Address, Now);
        user.ActivateWith(Hash);

        return user;
    }

    /// <summary>An account with a second factor, which is what enrolling makes.</summary>
    private static User Enrolled()
    {
        var user = Active();
        user.EnrolSecondFactor(SealedSecret, Now);

        return user;
    }
}
