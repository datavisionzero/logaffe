using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Application.Operations;

/// <summary>
/// How a password change ended.
/// </summary>
/// <remarks>
/// Unlike a sign-in, which answers every refusal with one refusal, this says
/// which half was wrong. There is nothing to protect: the request came from a
/// session, so the person on the other end has already proved they are the
/// operator, and "that is not your current password" is what they need to hear
/// to try again.
/// </remarks>
public enum PasswordChangeOutcome
{
    Changed,

    /// <summary>
    /// The current password was not it — or, in the moment after a Host
    /// Recovery, there is no account to compare against. Nothing was written
    /// and nothing was locked (ADR 0017).
    /// </summary>
    CurrentPasswordRefused,

    /// <summary>
    /// The chosen one is shorter than a password may be, or longer than one is
    /// hashed (<see cref="Password"/>).
    /// </summary>
    ChosenPasswordNotOne,
}

/// <summary>
/// The operator choosing a new password, which requires the current one and
/// ends every other session.
/// </summary>
/// <remarks>
/// <para>
/// Requiring the current password is what makes the change the operator's
/// rather than that of whoever is sitting at an unlocked browser, and ending
/// every other session is what makes it worth doing after a cookie has gone
/// somewhere it should not have (<c>docs/sign-in.md</c>).
/// </para>
/// <para>
/// The order is what each step costs: the shape of the chosen password is free,
/// and hashing is deliberately slow, so it happens once everything else has said
/// yes. The sessions go last, because a change that did not happen must not end
/// anything.
/// </para>
/// <para>
/// <b>It writes a fresh hash at today's cost</b> and therefore never needs the
/// rehash a sign-in does — but it is <see cref="Operator.ChangePasswordTo"/>
/// rather than <see cref="Operator.RehashedTo"/>, which is the distinction that
/// says one of these is something the operator did and the other is maintenance
/// nobody asked for.
/// </para>
/// </remarks>
public sealed class ChangePassword(
    IOperators operators,
    ISessions sessions,
    IPasswordHasher hasher)
{
    /// <param name="keeping">
    /// The session making the request, which is the one that survives.
    /// </param>
    public async Task<PasswordChangeOutcome> ExecuteAsync(
        string? currentPassword,
        string? chosenPassword,
        Session keeping,
        CancellationToken cancellationToken)
    {
        if (!Password.TryCreate(chosenPassword, out var chosen))
        {
            return PasswordChangeOutcome.ChosenPasswordNotOne;
        }

        // Read rather than created: the minimum is a rule about the password
        // being chosen above and not about the one being proved here, so an
        // operator whose password was long enough when they set it can still
        // change it after the minimum has risen (ADR 0042).
        if (!Password.TryRead(currentPassword, out var presented))
        {
            return PasswordChangeOutcome.CurrentPasswordRefused;
        }

        var theOperator = await operators.FindAsync(cancellationToken);
        if (theOperator is null
            || hasher.Verify(theOperator.PasswordHash, presented) is PasswordCheck.Wrong)
        {
            return PasswordChangeOutcome.CurrentPasswordRefused;
        }

        theOperator.ChangePasswordTo(hasher.Hash(chosen));
        await operators.RecordAsync(theOperator, cancellationToken);

        await sessions.RemoveEveryOtherAsync(keeping, cancellationToken);

        return PasswordChangeOutcome.Changed;
    }
}
