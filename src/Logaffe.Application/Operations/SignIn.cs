using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// A session, and what the user is told about the code they spent to get it.
/// </summary>
/// <remarks>
/// The secret is here because handing it over is the act, exactly as it is for a
/// token — but unlike a token this is the only time it exists. A session secret
/// is stored as a fast hash and is not readable back (ADR 0057), so a browser
/// that loses it signs in again.
/// </remarks>
/// <param name="Secret">
/// What the browser holds from now on. It is put into a cookie by the adapter
/// and appears nowhere else.
/// </param>
/// <param name="Session">The row, which is what its owner sees in their list.</param>
/// <param name="BackupCodesRemaining">
/// How many codes are left, when one was spent getting in, and <c>null</c> when
/// the second factor itself was used. <c>docs/sign-in.md</c> requires the count
/// to be said whenever one is spent, because a set that quietly runs out ends at
/// Host Recovery.
/// </param>
public sealed record SignedIn(
    SessionSecret Secret, Session Session, int? BackupCodesRemaining);

/// <summary>
/// A user proving what their account asks for and getting a session for it.
/// </summary>
/// <remarks>
/// <para>
/// What arrives is an address, a password and — when the account has a second
/// factor — either the six digits or a backup code standing in for them
/// (<c>docs/sign-in.md</c>). The second factor is each user's to enrol
/// (ADR 0041), so an account that has none signs in on the password alone.
/// </para>
/// <para>
/// <b>Every refusal is the same refusal.</b> An address nobody holds, a wrong
/// password, a wrong code, a code already spent, an account that was invited and
/// never arrived, and an account that has been deactivated are one <c>null</c>,
/// and the screen says one thing. Which of them it was is not a fact this
/// surface gives away — the first of them in particular, because saying it
/// turns the sign-in into a way of asking who has an account here.
/// </para>
/// <para>
/// <b>They also cost the same.</b> An address nobody holds is verified against a
/// hash that belongs to nobody, so that the absence of an account cannot be read
/// off how quickly the refusal came back; and it counts against the throttle for
/// that address exactly as a real one does (ADR 0056).
/// </para>
/// <para>
/// <b>Nothing here latches.</b> A failed attempt writes nothing to the account.
/// What holds the guessing back is the throttle in the adapter, which drains on
/// its own, and the second factor where one is enrolled.
/// </para>
/// </remarks>
public sealed class SignIn(
    IIdentities identities,
    ISessions sessions,
    IPasswordHasher hasher,
    DummyPasswordHash absent,
    ISecondFactor secondFactor,
    ISecretCipher cipher,
    TimeProvider clock)
{
    /// <summary>
    /// The session the credentials bought, or <c>null</c> when they did not.
    /// </summary>
    /// <param name="secondFactorCode">
    /// The six digits from the authenticator app, or <c>null</c> when a backup
    /// code is being given instead.
    /// </param>
    /// <param name="backupCode">
    /// A backup code standing in for the second factor, read however it was
    /// typed. It is consumed by getting in, and it is refused if it was consumed
    /// before.
    /// </param>
    /// <param name="seenFrom">
    /// Where the request came from, which is the column that makes the session
    /// list a security surface rather than a convenience.
    /// </param>
    public async Task<SignedIn?> ExecuteAsync(
        string? email,
        string? password,
        string? secondFactorCode,
        string? backupCode,
        string? seenFrom,
        CancellationToken cancellationToken)
    {
        // The shape first, and before the hasher: hashing is deliberately slow
        // and this surface is public, so a megabyte of input is refused here
        // rather than inside Argon2id. The minimum length is not applied — it is
        // a rule about choosing a password, and applying it to one being
        // presented would lock out somebody whose password was long enough when
        // they set it (ADR 0042).
        if (!Password.TryRead(password, out var presented))
        {
            return null;
        }

        var user = await FindByAddressAsync(email, cancellationToken);

        // A user with no password is one who was invited and never arrived. It
        // is refused like everything else here, and it still costs a
        // verification, so that the shape of the account cannot be read off the
        // clock.
        var storedHash = user?.PasswordHash ?? absent.Value;
        var check = hasher.Verify(storedHash, presented);

        if (user is null || user.PasswordHash is null || !user.IsActive
            || check is PasswordCheck.Wrong)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        // An account with no second factor has nothing to prove past the
        // password, and anything sent alongside it is ignored rather than
        // refused: what the user decided is what the sign-in asks for
        // (ADR 0041).
        var spent = !user.HasSecondFactor
            ? NoCodeWasSpent
            : secondFactorCode is null
                ? await SpendBackupCodeAsync(user, backupCode, now, cancellationToken)
                : VerifiesSecondFactor(user, secondFactorCode, now)
                    ? NoCodeWasSpent
                    : null;

        if (spent is null)
        {
            return null;
        }

        // Only now, and only on the way in. A correct password with a wrong
        // second factor is not a sign-in and must not leave a trace on the row,
        // which is what keeps `RehashedTo` maintenance nobody asked for rather
        // than something an attempt can trigger. This is also the step that
        // carries an installation off PBKDF2 one account at a time (ADR 0057).
        if (check is PasswordCheck.RightAndOutOfDate)
        {
            user.RehashedTo(hasher.Hash(presented));
            await identities.RecordAsync(user, cancellationToken);
        }

        var secret = SessionSecret.Mint();
        var session = Session.Start(user.Id, secret, seenFrom, now);
        await sessions.AddAsync(session, cancellationToken);

        return new SignedIn(secret, session, spent.Remaining);
    }

    /// <summary>
    /// The user behind the address presented, or <c>null</c> when it is not an
    /// address at all or nobody holds it.
    /// </summary>
    /// <remarks>
    /// Something that cannot be normalized is not an address and names nobody,
    /// which is one of the refusals this surface does not tell apart from the
    /// others.
    /// </remarks>
    private async Task<User?> FindByAddressAsync(
        string? email, CancellationToken cancellationToken)
    {
        string normalized;
        try
        {
            normalized = User.NormalizeEmailForComparison(email);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return await identities.FindByEmailAsync(normalized, cancellationToken);
    }

    /// <summary>
    /// What a sign-in that used the authenticator app itself has to say about
    /// backup codes, which is nothing.
    /// </summary>
    private static SpentBackupCode NoCodeWasSpent => new(null);

    private bool VerifiesSecondFactor(User user, string code, DateTimeOffset now) =>
        secondFactor.Verifies(cipher.Decrypt(user.EncryptedSecondFactorSecret!), code, now);

    /// <summary>
    /// Spends the presented backup code, or answers <c>null</c> when it is not
    /// one, is not this user's, or has been spent already.
    /// </summary>
    /// <remarks>
    /// The set is read whole and every code in it is compared, because there is
    /// no identifier naming a row here the way there is on a token (ADR 0031):
    /// a code carries all of its own entropy and a user holds ten of them. The
    /// loop does not stop at the one that matched — returning early is exactly
    /// what would say where in the set it sat.
    /// </remarks>
    private async Task<SpentBackupCode?> SpendBackupCodeAsync(
        User user, string? backupCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!BackupCodeText.TryParse(backupCode, out var presented))
        {
            return null;
        }

        var codes = await identities.ListBackupCodesAsync(user.Id, cancellationToken);

        BackupCode? matched = null;
        foreach (var code in codes)
        {
            if (code.Matches(presented))
            {
                matched ??= code;
            }
        }

        // A spent code matches exactly as a fresh one does — the domain says so
        // on purpose — so refusing it is here, and a code offered twice costs
        // what a code offered once costs.
        if (matched is null || matched.IsSpent)
        {
            return null;
        }

        matched.ConsumeAt(now);
        await identities.RecordConsumptionAsync(matched, cancellationToken);

        return new SpentBackupCode(codes.Count(code => !code.IsSpent));
    }

    /// <summary>
    /// That the second factor was satisfied, and how many codes are left if it
    /// was satisfied by spending one.
    /// </summary>
    private sealed record SpentBackupCode(int? Remaining);
}
