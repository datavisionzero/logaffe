using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// What the operator wrote into the configuration before the first start, any
/// of which may be missing.
/// </summary>
/// <remarks>
/// Read on the one start where it matters and ignored on every one after it. The
/// keys are logaffe's own shape, so in a compose file they are
/// <c>Logaffe__Bootstrap__Administrator</c>, <c>Logaffe__Bootstrap__Email</c>
/// and <c>Logaffe__Bootstrap__Token</c>.
/// </remarks>
public sealed record BootstrapSettings(string? Administrator, string? Email, string? Token)
{
    public const string AdministratorKey = "Logaffe:Bootstrap:Administrator";

    public const string EmailKey = "Logaffe:Bootstrap:Email";

    public const string TokenKey = "Logaffe:Bootstrap:Token";

    /// <summary>
    /// How long a bootstrap token has to be. It is pasted rather than recited —
    /// nobody has to remember it and it is spent once — so the installation can
    /// ask for a real value and refuse a word somebody typed.
    /// </summary>
    public const int TokenMinimumLength = 32;

    /// <summary>Whether all three are there to bootstrap from.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Administrator)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Token);
}

/// <summary>
/// How the bootstrap ended, for the host to log.
/// </summary>
public enum BootstrapOutcome
{
    /// <summary>
    /// The first administrator was created from the configuration, and the
    /// bootstrap token is waiting to be exchanged for their password.
    /// </summary>
    Bootstrapped,

    /// <summary>
    /// The installation already holds an identity. The configuration was ignored,
    /// whatever it said (ADR 0054).
    /// </summary>
    AlreadyBootstrapped,

    /// <summary>
    /// The operator this installation had before it had users was carried over,
    /// and the address in the configuration is now theirs. Their password, second
    /// factor and backup codes are untouched.
    /// </summary>
    AddressAdopted,

    /// <summary>
    /// No identity exists and the configuration does not name one. The
    /// installation starts anyway — ingestion needs no identity — and nothing
    /// can sign in until somebody sets the three keys.
    /// </summary>
    NothingToBootstrapFrom,
}

/// <summary>
/// Thrown when the installation will not start on what it was given: the start
/// stops with this message, the way a failed migration does, and nothing was
/// written.
/// </summary>
public sealed class BootstrapRefusedException(string message) : Exception(message);

/// <summary>
/// The first administrator, out of the configuration, on the one start where
/// there is nobody (ADR 0054).
/// </summary>
/// <remarks>
/// <para>
/// There is no first-run screen and no claim. Whoever installs names the
/// administrator before the first start, and what they name instead of a
/// password is a <b>bootstrap token</b>: a password in a compose file is a
/// password in a shell history and in <c>docker inspect</c>, and it is the
/// credential a human reuses. The token is exchanged once in the browser for a
/// password and a session, and it is spent by that exchange.
/// </para>
/// <para>
/// <b>The token is not stored anywhere.</b> It is compared against what
/// configuration says, exactly as the claim secret set in a compose file was, so
/// there is no second copy to disagree with the file and changing it is editing
/// the file. What makes it single-use is the administrator it names:
/// the bootstrap writes them <em>invited</em> and without a password, and the
/// exchange is the act that gives them one — so the moment it succeeds there is
/// no account left for a token to be exchanged against
/// (<see cref="ExchangeBootstrapToken"/>).
/// </para>
/// <para>
/// <b>On the second start the configuration is ignored</b>, whatever it says.
/// Changing the token there changes nothing, and an installation that already
/// has an administrator cannot be bootstrapped again by editing a file. The
/// check for emptiness comes first for that reason: a short token on a
/// bootstrapped installation is not a refusal, it is noise.
/// </para>
/// <para>
/// <b>It also finishes the upgrade of an installation that had an operator.</b>
/// That account had a password and no address, so the schema migration carried
/// it over under an address nobody can deliver to or sign in with, and this is
/// where the real one arrives. An installation upgraded without an address in
/// the configuration does not start — the alternative is a placeholder somebody
/// signs in with once and never changes.
/// </para>
/// </remarks>
public sealed class BootstrapTheInstallation(IIdentities identities, TimeProvider clock)
{
    /// <summary>
    /// The address a carried-over operator is parked under until the
    /// configuration says what it really is. It is in the reserved
    /// <c>.invalid</c> domain, so nothing can be delivered to it and no real
    /// address can collide with it.
    /// </summary>
    public const string PendingAddressDomain = "@bootstrap.invalid";

    /// <exception cref="BootstrapRefusedException">
    /// The installation will not accept what it was given, and stops rather than
    /// starting with a credential that cannot work.
    /// </exception>
    public async Task<BootstrapOutcome> ExecuteAsync(
        BootstrapSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var carriedOver = await FindCarriedOverOperatorAsync(cancellationToken);
        if (carriedOver is not null)
        {
            carriedOver.ChangeEmailTo(ReadAddress(settings, carriedOver));

            // The name comes along if the configuration offers one. It is not
            // required — the migration wrote a usable one, and a name is
            // something its owner can change from their own settings — so this
            // is a courtesy rather than a second thing that can stop a start.
            if (!string.IsNullOrWhiteSpace(settings.Administrator))
            {
                carriedOver.Rename(settings.Administrator);
            }

            await identities.RecordAsync(carriedOver, cancellationToken);

            return BootstrapOutcome.AddressAdopted;
        }

        if (await identities.AnyAsync(cancellationToken))
        {
            return BootstrapOutcome.AlreadyBootstrapped;
        }

        if (!settings.IsComplete)
        {
            return BootstrapOutcome.NothingToBootstrapFrom;
        }

        if (settings.Token!.Trim().Length < BootstrapSettings.TokenMinimumLength)
        {
            throw new BootstrapRefusedException(
                $"{BootstrapSettings.TokenKey} is "
                + $"{settings.Token.Trim().Length} characters; it has to be at least "
                + $"{BootstrapSettings.TokenMinimumLength}.");
        }

        var administrator = ReadAdministrator(settings, clock.GetUtcNow());
        await identities.TryAddAsync(administrator, cancellationToken);

        return BootstrapOutcome.Bootstrapped;
    }

    /// <summary>
    /// The account the schema migration carried over from the operator, or
    /// <c>null</c> when there was none — which is every installation that was
    /// created after the operator stopped existing.
    /// </summary>
    private async Task<User?> FindCarriedOverOperatorAsync(CancellationToken cancellationToken)
    {
        var users = await identities.ListUsersAsync(cancellationToken);

        return users.FirstOrDefault(user =>
            user.NormalizedEmail.EndsWith(PendingAddressDomain, StringComparison.Ordinal));
    }

    private static User ReadAdministrator(BootstrapSettings settings, DateTimeOffset now)
    {
        try
        {
            return User.Bootstrap(settings.Administrator!, settings.Email!, now);
        }
        catch (ArgumentException exception)
        {
            throw new BootstrapRefusedException(
                $"{BootstrapSettings.AdministratorKey} and {BootstrapSettings.EmailKey} do "
                + $"not name an administrator this installation can create: {exception.Message}");
        }
    }

    private static string ReadAddress(BootstrapSettings settings, User carriedOver)
    {
        try
        {
            return User.NormalizeEmail(settings.Email);
        }
        catch (ArgumentException exception)
        {
            throw new BootstrapRefusedException(
                $"This installation was upgraded from one that had an operator, and "
                + $"{carriedOver.Name} needs the email address they will sign in with. Set "
                + $"{BootstrapSettings.EmailKey} and start again. ({exception.Message})");
        }
    }
}
