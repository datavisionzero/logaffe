using Logaffe.Application.Operations;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The acts an administrator performs on somebody else's account (ADR 0052).
/// </summary>
public sealed class UserActsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryIdentities _identities = new();
    private readonly InMemorySessions _sessions = new();
    private readonly InMemoryProjectAccess _access = new();

    private readonly User _administrator;
    private readonly User _somebody;

    public UserActsTests()
    {
        _administrator = _identities.Seed(Active("admin@example.com", administrator: true));
        _somebody = _identities.Seed(Active("somebody@example.com", administrator: false));
    }

    [Fact]
    public async Task Deactivating_keeps_the_row_and_ends_every_session_it_had()
    {
        _sessions.Seed(Session.Start(_somebody.Id, SessionSecret.Mint(), "203.0.113.7", Now));
        var mine = _sessions.Seed(
            Session.Start(_administrator.Id, SessionSecret.Mint(), "198.51.100.4", Now));

        Assert.Equal(
            UserActOutcome.Done,
            await Changing().DeactivateAsync(_somebody.Id, TestContext.Current.CancellationToken));

        Assert.Equal(UserState.Deactivated, _somebody.State);

        // The row stays: everything pointing at somebody has to keep pointing at
        // something (ADR 0052). Only their sessions go, and nobody else's.
        Assert.Contains(_somebody, _identities.Stored);
        Assert.Equal([mine], _sessions.Stored);
    }

    [Fact]
    public async Task Reactivating_leaves_an_invitation_an_invitation()
    {
        var newcomer = _identities.Seed(
            User.Invite("Newcomer", "newcomer@example.com", administrator: false, Now));

        await Changing().DeactivateAsync(newcomer.Id, TestContext.Current.CancellationToken);
        await Changing().ReactivateAsync(newcomer.Id, TestContext.Current.CancellationToken);

        // An account with no password must not become active on the way back:
        // there is nothing for it to sign in with.
        Assert.Equal(UserState.Invited, newcomer.State);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated()
    {
        Assert.Equal(
            UserActOutcome.TheLastAdministrator,
            await Changing().DeactivateAsync(
                _administrator.Id, TestContext.Current.CancellationToken));

        Assert.True(_administrator.IsActive);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_lose_the_role()
    {
        Assert.Equal(
            UserActOutcome.TheLastAdministrator,
            await Changing().ChangeRoleAsync(
                _administrator.Id, administrator: false, TestContext.Current.CancellationToken));

        Assert.True(_administrator.Administrator);

        // With a second one there is nothing to hold: the rule is a count, not a
        // rule about who.
        await Changing().ChangeRoleAsync(
            _somebody.Id, administrator: true, TestContext.Current.CancellationToken);

        Assert.Equal(
            UserActOutcome.Done,
            await Changing().ChangeRoleAsync(
                _administrator.Id, administrator: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_invited_administrator_does_not_count_as_one()
    {
        _identities.Seed(User.Invite("Newcomer", "newcomer@example.com", true, Now));

        // Invited is not arrived, so the last *active* administrator is still
        // the last one.
        Assert.Equal(
            UserActOutcome.TheLastAdministrator,
            await Changing().DeactivateAsync(
                _administrator.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_list_says_what_each_account_is_and_how_much_it_reaches()
    {
        _somebody.EnrolSecondFactor([1, 2, 3], Now);
        await _access.GrantAsync(
            ProjectAccess.Grant(
                Guid.CreateVersion7(), _somebody.Id, _administrator.Id, Now),
            TestContext.Current.CancellationToken);

        var listed = await new ListUsers(_identities, _access)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var theirs = listed.Single(user => user.Id == _somebody.Id);
        Assert.Equal(UserState.Active, theirs.State);
        Assert.False(theirs.Administrator);
        Assert.True(theirs.HasSecondFactor);
        Assert.Equal(1, theirs.Projects);

        // What an administrator sees of somebody else's second factor is whether
        // there is one, and nothing else (docs/sign-in.md).
        Assert.Equal(0, listed.Single(user => user.Id == _administrator.Id).Projects);
    }

    [Fact]
    public async Task An_identity_that_is_not_a_user_is_not_found_here() =>
        Assert.Equal(
            UserActOutcome.NoSuchUser,
            await Changing().DeactivateAsync(
                Guid.CreateVersion7(), TestContext.Current.CancellationToken));

    private ChangeAUser Changing() => new(_identities, _sessions);

    private static User Active(string email, bool administrator)
    {
        var user = administrator
            ? User.Bootstrap("The Administrator", email, Now)
            : User.Invite("Somebody", email, administrator: false, Now);

        user.ActivateWith("$argon2id$v=19$m=19456,t=2,p=1$not-a-real-hash");

        return user;
    }
}
