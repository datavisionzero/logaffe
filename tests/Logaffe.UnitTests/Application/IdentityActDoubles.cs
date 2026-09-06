using Logaffe.Application.Ports;
using Logaffe.Domain.Alerts;
using Logaffe.Domain.Hosts;
using Logaffe.Domain.Identities;
using Logaffe.Domain.Projects;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The identity table and the backup codes beside it, in memory. It behaves as
/// the real store does in the ways the acts turn on — an address is held once,
/// and the codes are read whole for one user — and in no other way.
/// </summary>
internal sealed class InMemoryIdentities : IIdentities
{
    private readonly List<BackupCode> _backupCodes = [];
    private readonly List<Identity> _identities = [];

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public IReadOnlyList<BackupCode> BackupCodes => _backupCodes;

    public IReadOnlyList<Identity> Stored => _identities;

    /// <summary>
    /// Puts a user in place without counting it as a write, so that what a test
    /// asks about <see cref="Writes"/> is what the act under it wrote.
    /// </summary>
    public User Seed(User user)
    {
        _identities.Add(user);

        return user;
    }

    /// <summary>
    /// Puts a user in place with a sheet of backup codes beside them, which is
    /// the state an enrolment leaves behind and the starting point of most of
    /// what is asserted here.
    /// </summary>
    public MintedBackupCodes SeedWithBackupCodes(User user, DateTimeOffset issuedAt)
    {
        var minted = BackupCode.MintSet(user.Id, issuedAt);

        _identities.Add(user);
        _backupCodes.AddRange(minted.Stored);

        return minted;
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_identities.Count > 0);

    public Task<User?> FindByEmailAsync(
        string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_identities.OfType<User>()
            .SingleOrDefault(user => user.NormalizedEmail == normalizedEmail));

    public Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_identities.OfType<User>().SingleOrDefault(user => user.Id == id));

    public Task<Identity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_identities.SingleOrDefault(identity => identity.Id == id));

    public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(
            [.. _identities.OfType<User>().OrderBy(user => user.Name)]);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_identities.OfType<User>()
            .Count(user => user.Administrator && user.IsActive));

    public Task<bool> TryAddAsync(Identity identity, CancellationToken cancellationToken)
    {
        // The address decides it, as the unique index does in the real store.
        if (identity is User adding
            && _identities.OfType<User>()
                .Any(held => held.NormalizedEmail == adding.NormalizedEmail))
        {
            return Task.FromResult(false);
        }

        _identities.Add(identity);
        Writes++;

        return Task.FromResult(true);
    }

    public Task RecordAsync(Identity identity, CancellationToken cancellationToken) => Write();

    public Task<int> RemoveEveryIdentityAsync(CancellationToken cancellationToken)
    {
        var removed = _identities.Count;

        _identities.Clear();
        _backupCodes.Clear();
        Writes++;

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(
        Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BackupCode>>(
            [.. _backupCodes.Where(code => code.UserId == userId)]);

    public Task ReplaceBackupCodesAsync(
        Guid userId, IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken)
    {
        _backupCodes.RemoveAll(code => code.UserId == userId);
        _backupCodes.AddRange(backupCodes);

        return Write();
    }

    public Task RecordConsumptionAsync(BackupCode code, CancellationToken cancellationToken) =>
        Write();

    private Task Write()
    {
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The one row an installation holds about itself, in memory: the sample window,
/// the machine it sits on, the alert switches and the notifier.
/// </summary>
internal sealed class InMemoryInstallation : IInstallation
{
    private RetentionWindow _sampleRetention =
        RetentionWindow.OfDays(Sampling.RetentionDaysByDefault);

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public Task<RetentionWindow> ReadSampleRetentionAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(_sampleRetention);

    public Task RecordSampleRetentionAsync(
        RetentionWindow window, CancellationToken cancellationToken)
    {
        _sampleRetention = window;
        Writes++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// The machine the installation names, which is none until something names
    /// one — the ordinary state of every installation.
    /// </summary>
    public InstallationHost? Host { get; private set; }

    public Task<InstallationHost?> ReadHostAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Host);

    public Task RecordHostAsync(InstallationHost? host, CancellationToken cancellationToken)
    {
        Host = host;
        Writes++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Which conditions are switched on, which is none of them until something
    /// switches one on — every installation's ordinary state.
    /// </summary>
    public AlertSwitches Switches { get; set; } = AlertSwitches.AllOff;

    public Task<AlertSwitches> ReadAlertSwitchesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Switches);

    public Task RecordAlertSwitchesAsync(
        AlertSwitches switches, CancellationToken cancellationToken)
    {
        Switches = switches;
        Writes++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Where notifications go, which is nowhere until the operator says
    /// otherwise.
    /// </summary>
    public Notifier? Notifier { get; set; }

    public Task<Notifier?> ReadNotifierAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Notifier);

    public Task RecordNotifierAsync(Notifier? notifier, CancellationToken cancellationToken)
    {
        Notifier = notifier;
        Writes++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// The signed-in browsers of an installation, in memory. The real store reads
/// the table whole to authenticate, because a session secret names no row, and
/// this does the same — while every other read narrows to one user, as it does
/// there.
/// </summary>
internal sealed class InMemorySessions : ISessions
{
    private readonly List<Session> _sessions = [];

    public IReadOnlyList<Session> Stored => _sessions;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    /// <summary>How many times the table was read, which some acts must not do.</summary>
    public int Reads { get; private set; }

    /// <summary>
    /// Puts a session in place without counting it as a write, so that what a
    /// test asks about <see cref="Writes"/> is what the act under it wrote.
    /// </summary>
    public Session Seed(Session session)
    {
        _sessions.Add(session);

        return session;
    }

    public Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<Session>>(
            [.. _sessions.OrderByDescending(session => session.StartedAt)]);
    }

    public Task<IReadOnlyList<Session>> ListForAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<Session>>(
            [.. _sessions.Where(session => session.UserId == userId)
                .OrderByDescending(session => session.StartedAt)]);
    }

    public Task AddAsync(Session session, CancellationToken cancellationToken) =>
        Write(() => _sessions.Add(session));

    public Task RemoveAsync(Session session, CancellationToken cancellationToken) =>
        Write(() => _sessions.Remove(session));

    public Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken) =>
        Write(() => _sessions.RemoveAll(
            session => session.UserId == kept.UserId && session.Id != kept.Id));

    public Task RemoveEveryOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Write(() => _sessions.RemoveAll(session => session.UserId == userId));

    public Task RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Write(() => _sessions.RemoveAll(session => session.HasExpiredAt(asOf)));

    public Task RecordUseAsync(Session session, CancellationToken cancellationToken) =>
        Write(() => { });

    private Task Write(Action write)
    {
        write();
        Writes++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// A hasher that is not one: it writes what it was given with a marker in front,
/// so that a row holding the password in the clear is a failing assertion.
/// </summary>
/// <remarks>
/// What it does model is the one thing the acts read from it — that a hash can
/// be right and still be out of date, which is what makes a successful sign-in
/// owe the row a rewrite (ADR 0057).
/// </remarks>
internal sealed class StubPasswordHasher : IPasswordHasher
{
    private const string Marker = "hashed:";

    /// <summary>What a matching password is answered with.</summary>
    public PasswordCheck Answer { get; set; } = PasswordCheck.Right;

    public int Hashes { get; private set; }

    public int Verifications { get; private set; }

    public static string HashOf(string password) => Marker + password;

    public string Hash(Password password)
    {
        Hashes++;

        return HashOf(password.Text);
    }

    public PasswordCheck Verify(string storedHash, Password presented)
    {
        Verifications++;

        return storedHash == HashOf(presented.Text) ? Answer : PasswordCheck.Wrong;
    }
}

/// <summary>
/// A second factor that accepts one code, and remembers which secret it was
/// asked to check against — which is how a test says the sealed one was opened
/// first.
/// </summary>
internal sealed class StubSecondFactor(string accepted) : ISecondFactor
{
    public const string Secret = "the-enrolled-secret";

    public string? CheckedAgainst { get; private set; }

    public string MintSecret() => Secret;

    public bool Verifies(string secret, string? code, DateTimeOffset at)
    {
        CheckedAgainst = secret;

        return code == accepted;
    }

    public string EnrolmentUri(string secret, string account) =>
        $"otpauth://totp/logaffe:{account}?secret={secret}";
}

/// <summary>
/// The two rolling windows in front of the sign-in, in memory and without a
/// clock of their own: what a test needs of them is that a failure counts, a
/// success clears the account's window and not the source's, and an exhausted
/// window refuses (ADR 0056).
/// </summary>
internal sealed class InMemorySignInThrottle : ISignInThrottle
{
    private readonly Dictionary<string, int> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sources = new(StringComparer.Ordinal);

    public int Failures { get; private set; }

    public int Successes { get; private set; }

    public bool Admits(string account, string source, DateTimeOffset now) =>
        Count(_accounts, account) < SignInThrottle.AttemptsPerAccount
        && Count(_sources, source) < SignInThrottle.AttemptsPerSource;

    public void Failed(string account, string source, DateTimeOffset now)
    {
        _accounts[account] = Count(_accounts, account) + 1;
        _sources[source] = Count(_sources, source) + 1;
        Failures++;
    }

    public void Succeeded(string account)
    {
        _accounts.Remove(account);
        Successes++;
    }

    /// <summary>How many failures that source has, which a test asserts on.</summary>
    public int FailuresFrom(string source) => Count(_sources, source);

    private static int Count(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var held) ? held : 0;
}
