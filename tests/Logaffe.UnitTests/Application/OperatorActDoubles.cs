using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The account row and the backup codes beside it, in memory. It behaves as the
/// real store does in the ways the acts turn on — there is one account or there
/// is none, and the codes are read whole — and in no other way.
/// </summary>
internal sealed class InMemoryOperators : IOperators
{
    private readonly List<BackupCode> _backupCodes = [];
    private Operator? _operator;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public IReadOnlyList<BackupCode> BackupCodes => _backupCodes;

    /// <summary>
    /// Puts an installation in the state a claim leaves it in — an account and
    /// no second factor (ADR 0041) — with a sheet of backup codes beside it,
    /// which is the state an enrolment leaves behind and the starting point of
    /// most of what is asserted here.
    /// </summary>
    public MintedBackupCodes Claim(Operator theOperator, DateTimeOffset issuedAt)
    {
        var minted = BackupCode.MintSet(theOperator.Id, issuedAt);

        _operator = theOperator;
        _backupCodes.AddRange(minted.Stored);

        return minted;
    }

    /// <summary>
    /// Puts an account in place that has enrolled nothing, which is what a claim
    /// on its own leaves behind.
    /// </summary>
    public void ClaimWithoutASecondFactor(Operator theOperator) => _operator = theOperator;

    public Task<bool> IsClaimedAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_operator is not null);

    public Task<Operator?> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_operator);

    public Task<bool> TryClaimAsync(
        Operator theOperator, CancellationToken cancellationToken)
    {
        if (_operator is not null)
        {
            return Task.FromResult(false);
        }

        _operator = theOperator;
        Writes++;

        return Task.FromResult(true);
    }

    public Task RecordAsync(Operator theOperator, CancellationToken cancellationToken) =>
        Write();

    public Task RemoveAsync(Operator theOperator, CancellationToken cancellationToken)
    {
        _operator = null;
        _backupCodes.Clear();

        return Write();
    }

    public Task<IReadOnlyList<BackupCode>> ListBackupCodesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BackupCode>>([.. _backupCodes]);

    public Task ReplaceBackupCodesAsync(
        IReadOnlyList<BackupCode> backupCodes, CancellationToken cancellationToken)
    {
        _backupCodes.Clear();
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
/// The one row an installation holds about itself, in memory. It behaves as the
/// real store does in the one way the acts turn on: the first run is written
/// once, and every start after it reads what is there.
/// </summary>
internal sealed class InMemoryInstallation : IInstallation
{
    private ClaimGuard? _guard;

    /// <summary>How many statements the store was asked to write.</summary>
    public int Writes { get; private set; }

    public Task<ClaimGuard?> ReadClaimGuardAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_guard);

    public Task<ClaimGuard> OpenClaimAsync(
        DateTimeOffset firstRunAt, CancellationToken cancellationToken)
    {
        if (_guard is not null)
        {
            return Task.FromResult(_guard);
        }

        _guard = ClaimGuard.OpenedOnFirstRun(firstRunAt);
        Writes++;

        return Task.FromResult(_guard);
    }

    public Task<ClaimGuard> ArmClaimAsync(
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (_guard is null)
        {
            _guard = ClaimGuard.OpenedOnFirstRun(at);
        }
        else
        {
            _guard.ArmAt(at);
        }

        Writes++;

        return Task.FromResult(_guard);
    }

    public Task RecordClaimAsync(ClaimGuard guard, CancellationToken cancellationToken)
    {
        _guard = guard;
        Writes++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// The file a drawn claim secret is handed over in, in memory. What the acts
/// turn on is that it is written when one is drawn and removed by the claim
/// (ADR 0040), and this holds exactly that.
/// </summary>
internal sealed class InMemoryClaimSecretHandover : IClaimSecretHandover
{
    public string Path => "/var/lib/logaffe/claim-secret.txt";

    /// <summary>The secret as it was handed over, or <c>null</c> once removed.</summary>
    public string? HandedOver { get; private set; }

    public int Removals { get; private set; }

    public Task WriteAsync(ClaimSecret secret, CancellationToken cancellationToken)
    {
        HandedOver = secret.Text;

        return Task.CompletedTask;
    }

    public void Remove()
    {
        HandedOver = null;
        Removals++;
    }
}

/// <summary>
/// The operator's signed-in browsers, in memory. The real store reads the table
/// whole because authenticating one compares against all of them, and this does
/// the same.
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

    public Task AddAsync(Session session, CancellationToken cancellationToken) =>
        Write(() => _sessions.Add(session));

    public Task RemoveAsync(Session session, CancellationToken cancellationToken) =>
        Write(() => _sessions.Remove(session));

    public Task RemoveEveryOtherAsync(Session kept, CancellationToken cancellationToken) =>
        Write(() => _sessions.RemoveAll(session => session.Id != kept.Id));

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
/// owe the row a rewrite (ADR 0032).
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
