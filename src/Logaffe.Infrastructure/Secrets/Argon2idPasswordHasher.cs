using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logaffe.Infrastructure.Secrets;

/// <summary>
/// Argon2id, written as a PHC string that carries its own parameters — and a
/// reader for the PBKDF2 hashes an installation from before
/// <see href="../../../docs/adr/0057-a-users-secrets-are-stored-for-what-they-are-and-the-password-is-argon2id.md">ADR 0057</see>
/// wrote.
/// </summary>
/// <remarks>
/// <para>
/// It is memory-hard, which is the property that takes the advantage away from
/// the hardware an offline attacker would bring, and against a human-chosen
/// password behind nothing but a minimum length that advantage is the whole
/// game. ADR 0032 refused it because .NET 10 carries no Argon2 anywhere and it
/// would have been a third-party package on the sign-in path of a product whose
/// case is being small; what changed is that the same package at the same
/// version is already in planaffe and vaultaffe, and that an installation now
/// holds several accounts behind one publicly reachable form.
/// </para>
/// <para>
/// <b>Nobody is locked out and nobody resets anything.</b> A stored value that
/// begins <c>$argon2id$</c> is verified here; anything else is handed to the
/// framework's verifier and, when it is right, comes back
/// <see cref="PasswordCheck.RightAndOutOfDate"/> — which is what makes the next
/// successful sign-in rewrite it. A password nobody signs in with keeps its old
/// hash, which costs nothing, because it is also a password nobody is signing in
/// with.
/// </para>
/// <para>
/// <b>The parameters are in the value and not in this file.</b> Raising them
/// later is one line here and a rewrite per sign-in, with no schema change and
/// nothing to migrate — which was the whole point of the version marker ADR 0032
/// insisted on, kept in a format that says more.
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// OWASP's second recommended option for Argon2id — 64 MiB, three passes,
    /// one lane — and the figures planaffe and vaultaffe hash at. One lane
    /// because the work happens on a request thread of an installation that is
    /// also taking deliveries, and parallelism here buys latency at the cost of
    /// the cores everything else is using.
    /// </summary>
    public const int MemoryKibibytes = 65_536;

    /// <inheritdoc cref="MemoryKibibytes"/>
    public const int Iterations = 3;

    /// <inheritdoc cref="MemoryKibibytes"/>
    public const int Parallelism = 1;

    /// <summary>What Argon2id is asked for, and what a stored value carries.</summary>
    private const int HashBytes = 32;

    /// <summary>
    /// Drawn per password, so that two people who chose the same one do not hold
    /// the same value.
    /// </summary>
    private const int SaltBytes = 16;

    /// <summary>The prefix that says a stored value is one of ours.</summary>
    private const string Prefix = "$argon2id$";

    /// <summary>The only version of the format this writes or reads.</summary>
    private const string Version = "v=19";

    /// <summary>
    /// What an installation from before ADR 0057 wrote, kept to read and never
    /// to write. Its own iteration count is in the value, so this stands in for
    /// nothing the stored hash does not already say.
    /// </summary>
    private static readonly PasswordHasher<Nobody> Framework = new(Options.Create(
        new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        }));

    /// <summary>
    /// The framework's hasher is generic over a user it never looks at, and
    /// there is no user type here to give it — a <see cref="User"/> has no part
    /// in how its own password is hashed. This stands in its place so that
    /// nothing is implied by what is passed.
    /// </summary>
    private static readonly Nobody NotUsed = new();

    public string Hash(Password password) =>
        Encode(
            password.Text,
            RandomNumberGenerator.GetBytes(SaltBytes),
            MemoryKibibytes,
            Iterations,
            Parallelism);

    public PasswordCheck Verify(string storedHash, Password presented) =>
        storedHash.StartsWith(Prefix, StringComparison.Ordinal)
            ? VerifyArgon2id(storedHash, presented)
            : VerifyWhatTheFrameworkWrote(storedHash, presented);

    /// <summary>
    /// Whether the presented password is behind this PHC string, and whether the
    /// parameters in it are still the ones in use.
    /// </summary>
    /// <remarks>
    /// The stored value's own parameters are what the candidate is hashed with,
    /// not this file's: that is what makes the format self-describing and what
    /// lets the figures above rise without anybody being locked out. A value this
    /// cannot read at all is <see cref="PasswordCheck.Wrong"/> rather than an
    /// exception — a row somebody wrote over is the same event as a forgotten
    /// password, and it has the same answer.
    /// </remarks>
    private static PasswordCheck VerifyArgon2id(string storedHash, Password presented)
    {
        var parts = storedHash.Split('$');

        if (parts.Length != 6
            || !string.Equals(parts[1], "argon2id", StringComparison.Ordinal)
            || !string.Equals(parts[2], Version, StringComparison.Ordinal))
        {
            return PasswordCheck.Wrong;
        }

        try
        {
            var parameters = parts[3]
                .Split(',')
                .Select(pair => pair.Split('='))
                .ToDictionary(
                    pair => pair[0],
                    pair => int.Parse(pair[1], CultureInfo.InvariantCulture),
                    StringComparer.Ordinal);

            var memory = parameters["m"];
            var iterations = parameters["t"];
            var parallelism = parameters["p"];
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);

            // Bounds on what a stored value may ask for, so that a row somebody
            // edited cannot turn a sign-in into an hour of memory-hard work.
            if (memory is < 8_192 or > 262_144
                || iterations is < 1 or > 10
                || parallelism is < 1 or > 16
                || salt.Length is < 16 or > 64
                || expected.Length != HashBytes)
            {
                return PasswordCheck.Wrong;
            }

            var actual = Derive(presented.Text, salt, memory, iterations, parallelism);

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PasswordCheck.Wrong;
            }

            return memory == MemoryKibibytes
                && iterations == Iterations
                && parallelism == Parallelism
                    ? PasswordCheck.Right
                    : PasswordCheck.RightAndOutOfDate;
        }
        catch (Exception exception)
            when (exception is FormatException or KeyNotFoundException
                or OverflowException or IndexOutOfRangeException)
        {
            return PasswordCheck.Wrong;
        }
    }

    /// <summary>
    /// A hash from before this product hashed with Argon2id. It admits exactly
    /// as a current one does, and it owes the row a rewrite — which is the whole
    /// of how an installation crosses over (ADR 0057).
    /// </summary>
    private static PasswordCheck VerifyWhatTheFrameworkWrote(
        string storedHash, Password presented)
    {
        try
        {
            return Framework.VerifyHashedPassword(NotUsed, storedHash, presented.Text)
                is PasswordVerificationResult.Failed
                    ? PasswordCheck.Wrong
                    : PasswordCheck.RightAndOutOfDate;
        }
        catch (FormatException)
        {
            // A stored hash that is not base64 at all: a row somebody wrote over
            // rather than a password anybody typed. It is refused as a wrong
            // password, because there is nothing else this could truthfully be
            // turned into.
            return PasswordCheck.Wrong;
        }
    }

    private static string Encode(
        string password, byte[] salt, int memory, int iterations, int parallelism) =>
        $"{Prefix}{Version}$m={memory},t={iterations},p={parallelism}$"
        + $"{Convert.ToBase64String(salt)}$"
        + Convert.ToBase64String(Derive(password, salt, memory, iterations, parallelism));

    private static byte[] Derive(
        string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon.GetBytes(HashBytes);
    }

    private sealed class Nobody;
}
