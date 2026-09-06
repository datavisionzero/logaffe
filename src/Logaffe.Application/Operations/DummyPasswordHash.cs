using System.Security.Cryptography;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

namespace Logaffe.Application.Operations;

/// <summary>
/// A password hash belonging to nobody, which an address nobody holds is
/// verified against.
/// </summary>
/// <remarks>
/// <para>
/// The sign-in answers an unknown address, a wrong password and a deactivated
/// account with one refusal, and it has to answer them in one time class as well
/// — otherwise the timing is the disclosure the wording avoided, and the surface
/// becomes a way of asking who has an account on this installation
/// (ADR 0056). Hashing is deliberately expensive, so the branch that finds
/// nobody has to spend it too.
/// </para>
/// <para>
/// It is hashed once and held for the life of the process, at the parameters in
/// use today, so that it costs exactly what a real row costs. Hashing one per
/// request would make the miss the more expensive of the two cases and hand
/// back, as a delay, the difference this exists to remove.
/// </para>
/// <para>
/// This is <see cref="DummySecret"/>'s counterpart on the human door, and it
/// exists for the reason ADR 0031 gives on the machine one.
/// </para>
/// </remarks>
public sealed class DummyPasswordHash(IPasswordHasher hasher)
{
    private readonly Lazy<string> hash = new(
        () => hasher.Hash(Password.Create(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The encoded hash of a password nobody knows and nobody can present.
    /// </summary>
    public string Value => hash.Value;
}
