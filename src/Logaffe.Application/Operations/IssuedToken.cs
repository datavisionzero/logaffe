using Logaffe.Domain.Tokens;

namespace Logaffe.Application.Operations;

/// <summary>
/// A token as it comes back from being issued: the row it went into, and the
/// whole token in the clear.
/// </summary>
/// <remarks>
/// Handing the token back is not a convenience — it is the act. A token nobody
/// receives is a row that admits a delivery no sender can make, and the operator
/// is meant to paste this into a configuration. What makes that affordable is
/// that it is not the only chance to see it:
/// <see cref="ReadTokenBack"/> produces the same value at any time (ADR 0022).
/// </remarks>
/// <param name="Id">
/// What the operator's later acts name this token by — revoking it, renaming it,
/// reading it back. It is not the <see cref="TokenIdentifier"/>, which is the
/// part of the token's own text that names the row to the ingest path.
/// </param>
/// <param name="Token">The token itself, prefix and identifier and secret.</param>
public sealed record IssuedToken(Guid Id, TokenText Token, DateTimeOffset IssuedAt);
