using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// PBKDF2-HMAC-SHA512, out of the ASP.NET Core shared framework.
/// </summary>
/// <remarks>
/// <para>
/// Argon2id is the stronger answer and was not taken: there is none in .NET 10,
/// so it would be a third-party package on the sign-in path of a product whose
/// case is being small, and what makes that trade affordable is the second
/// factor being mandatory (ADR 0032). A stolen dump can be ground offline, and
/// that is stated rather than argued away.
/// </para>
/// <para>
/// The format carries its own version marker and its own iteration count, which
/// is what lets this read what an older installation wrote and what makes
/// <see cref="PasswordCheck.RightAndOutOfDate"/> a thing the framework can tell
/// us rather than a thing we would have to remember.
/// </para>
/// </remarks>
public sealed class FrameworkPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// OWASP's floor for PBKDF2-HMAC-SHA512 at the time of writing, and above
    /// the framework's own default. Raising it later costs one line here: every
    /// sign-in against an older hash then comes back out of date and rewrites
    /// itself.
    /// </summary>
    public const int IterationCount = 210_000;

    private static readonly PasswordHasher<Nobody> Hasher = new(Options.Create(
        new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = IterationCount,
        }));

    /// <summary>
    /// The framework's hasher is generic over a user it never looks at, and
    /// there is no user type here to give it — <see cref="Operator"/> has no
    /// part in how its own password is hashed. This stands in its place so that
    /// nothing is implied by what is passed.
    /// </summary>
    private static readonly Nobody NotUsed = new();

    public string Hash(Password password) => Hasher.HashPassword(NotUsed, password.Text);

    public PasswordCheck Verify(string storedHash, Password presented)
    {
        try
        {
            return Hasher.VerifyHashedPassword(NotUsed, storedHash, presented.Text) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Right,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.RightAndOutOfDate,
                _ => PasswordCheck.Wrong,
            };
        }
        catch (FormatException)
        {
            // A stored hash that is not base64 at all: a row somebody wrote over
            // rather than a password anybody typed. It is refused as a wrong
            // password, because there is nothing else this could truthfully be
            // turned into — and no reset over the network to offer instead
            // (ADR 0015).
            return PasswordCheck.Wrong;
        }
    }

    private sealed class Nobody;
}
