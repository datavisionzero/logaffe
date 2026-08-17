using Logaffe.Application.Ports;

namespace Logaffe.Application.Operations;

/// <summary>
/// Whether this installation's operator has a second factor, which is the one
/// thing the interface cannot work out for itself.
/// </summary>
/// <remarks>
/// The second factor is optional (ADR 0041), so an installation running without
/// one has to say so — on the screen that offers the enrolment, and in the banner
/// that keeps saying it until there is one. Neither is a fact the browser holds:
/// a session says who is signed in and nothing about what they enrolled.
/// </remarks>
/// <param name="EnrolledAt">
/// When it became the current one, and <c>null</c> when there is none. It is the
/// whole of the history kept of an enrolment (ADR 0032).
/// </param>
public sealed record SecondFactorState(bool IsEnrolled, DateTimeOffset? EnrolledAt);

/// <inheritdoc cref="SecondFactorState"/>
public sealed class CheckTheSecondFactor(IOperators operators)
{
    /// <returns>
    /// The state, or <c>null</c> when there is no account — which behind a
    /// session means Host Recovery ran a moment ago.
    /// </returns>
    public async Task<SecondFactorState?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var theOperator = await operators.FindAsync(cancellationToken);

        return theOperator is null
            ? null
            : new SecondFactorState(
                theOperator.HasSecondFactor, theOperator.SecondFactorEnrolledAt);
    }
}
