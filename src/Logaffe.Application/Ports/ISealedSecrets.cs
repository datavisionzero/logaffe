namespace Logaffe.Application.Ports;

/// <summary>
/// The secrets an installation holds sealed, without regard for which of them
/// it is.
/// </summary>
/// <remarks>
/// This exists for one question — whether the key on the volume opens what the
/// database holds — so it takes a handful rather than all of them, and it says
/// nothing about ingest tokens against agent tokens. Anything that needs a
/// particular secret asks for it by name somewhere else.
/// </remarks>
public interface ISealedSecrets
{
    /// <summary>
    /// Takes up to <paramref name="count"/> sealed secrets, or none when the
    /// installation holds none at all.
    /// </summary>
    Task<IReadOnlyList<byte[]>> SampleAsync(int count, CancellationToken cancellationToken);
}
