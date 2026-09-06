using System.Security.Cryptography;
using System.Text;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// How the exchange ended.
/// </summary>
/// <remarks>
/// Unlike a sign-in, which answers every refusal with one refusal, this says
/// which half was wrong. There is little to protect and much to lose by being
/// unhelpful: the person on the other end is setting up their own installation
/// with the compose file open in another window, and telling them the token did
/// not match rather than "no" is the difference between finishing and giving up.
/// </remarks>
public enum ExchangeOutcome
{
    Exchanged,

    /// <summary>
    /// There is nothing to exchange against: the installation was never
    /// bootstrapped, or its first administrator already has a password. It is
    /// the same answer either way, because the second is what the first becomes
    /// and neither is a state a stranger can act on.
    /// </summary>
    NothingToExchange,

    /// <summary>
    /// Not the token this installation's configuration names (ADR 0054). It is
    /// the only thing this refusal says.
    /// </summary>
    TokenRefused,

    /// <summary>Shorter than a password may be, or longer than one is hashed.</summary>
    PasswordNotOne,
}

/// <summary>
/// The exchange and the session it hands out when it succeeded.
/// </summary>
/// <remarks>
/// It signs the administrator in, because the alternative is a screen that
/// congratulates somebody and then asks them for the password they chose four
/// seconds ago.
/// </remarks>
public sealed record BootstrapExchange(
    ExchangeOutcome Outcome, SessionSecret? Secret, Session? Session);

/// <summary>
/// The one act the bootstrap token buys: a password for the first
/// administrator, and a session to go on with (ADR 0054).
/// </summary>
/// <remarks>
/// <para>
/// This is the only anonymous act on the installation besides the sign-in, and
/// it is narrower than the claim it replaces in every direction. It needs a
/// value that never travelled over the network to get where it is used; it works
/// only while the account it names has no password; and it establishes a
/// password and nothing else — the second factor is that user's to enrol
/// afterwards (ADR 0041).
/// </para>
/// <para>
/// <b>The token is compared in constant time</b> against what configuration
/// says, and nothing about it is stored. The comparison happens before the
/// password is hashed, because hashing is deliberately slow and this surface is
/// public.
/// </para>
/// </remarks>
public sealed class ExchangeBootstrapToken(
    IIdentities identities,
    ISessions sessions,
    IPasswordHasher hasher,
    BootstrapSettings settings,
    TimeProvider clock)
{
    /// <param name="seenFrom">
    /// Where the request came from, which the session this hands out is listed
    /// with from its first moment.
    /// </param>
    public async Task<BootstrapExchange> ExecuteAsync(
        string? token, string? password, string? seenFrom, CancellationToken cancellationToken)
    {
        var configured = settings.Token?.Trim();
        if (string.IsNullOrEmpty(configured)
            || configured.Length < BootstrapSettings.TokenMinimumLength)
        {
            return Refused(ExchangeOutcome.NothingToExchange);
        }

        if (!Matches(configured, token))
        {
            return Refused(ExchangeOutcome.TokenRefused);
        }

        // The shape before the hasher, as on the sign-in: a megabyte of input is
        // refused here rather than inside Argon2id.
        if (!Password.TryCreate(password, out var chosen))
        {
            return Refused(ExchangeOutcome.PasswordNotOne);
        }

        // The administrator the bootstrap wrote, recognized by having no
        // password: that is what makes this act single-use, and it is why a
        // second exchange finds nothing rather than being refused by a flag
        // somebody has to remember to set.
        var users = await identities.ListUsersAsync(cancellationToken);
        var administrator = users.FirstOrDefault(user =>
            user is { Administrator: true, PasswordHash: null, State: UserState.Invited });

        if (administrator is null)
        {
            return Refused(ExchangeOutcome.NothingToExchange);
        }

        var now = clock.GetUtcNow();

        administrator.ActivateWith(hasher.Hash(chosen));
        await identities.RecordAsync(administrator, cancellationToken);

        var secret = SessionSecret.Mint();
        var session = Session.Start(administrator.Id, secret, seenFrom, now);
        await sessions.AddAsync(session, cancellationToken);

        return new BootstrapExchange(ExchangeOutcome.Exchanged, secret, session);
    }

    /// <summary>
    /// Whether the presented value is the configured one, compared over its
    /// bytes in constant time so that the comparison says nothing about how much
    /// of the token was right.
    /// </summary>
    private static bool Matches(string configured, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));

    private static BootstrapExchange Refused(ExchangeOutcome outcome) =>
        new(outcome, null, null);
}
