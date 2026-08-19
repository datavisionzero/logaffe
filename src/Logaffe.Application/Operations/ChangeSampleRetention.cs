using Logaffe.Application.Ports;
using Logaffe.Domain.Projects;

namespace Logaffe.Application.Operations;

/// <summary>
/// How long the installation keeps samples, and changing it.
/// </summary>
/// <remarks>
/// <para>
/// One window for the installation rather than one per host: there is no reason
/// to keep one machine's numbers longer than another's, and it is one field
/// fewer on every host that is ever created.
/// </para>
/// <para>
/// It is capped for the reason a project's is (ADR 0020). A settings box without
/// a ceiling is how a product that is not a multi-year archive becomes one
/// without anyone deciding it should, and samples are the easiest place to let
/// that happen — they are small, so "keep them longer, they cost nothing" is an
/// argument that sounds reasonable right up until the shape of the product has
/// changed.
/// </para>
/// </remarks>
public sealed class ChangeSampleRetention(
    IInstallation installation, ISamples samples, TimeProvider clock)
{
    /// <summary>The window as it stands.</summary>
    public Task<RetentionWindow> ReadAsync(CancellationToken cancellationToken) =>
        installation.ReadSampleRetentionAsync(cancellationToken);

    /// <summary>
    /// How many samples a window of <paramref name="days"/> would put outside
    /// the installation's keeping.
    /// </summary>
    /// <remarks>
    /// Asked before the change takes effect, because a settings field that
    /// silently destroys data is a bad settings field. Raising the window brings
    /// nothing back, and this is what says so in a number.
    /// </remarks>
    public Task<long> CountOutsideAsync(
        RetentionWindow window, CancellationToken cancellationToken) =>
        samples.CountReceivedBeforeAsync(
            clock.GetUtcNow() - window.Duration, cancellationToken);

    public Task ExecuteAsync(RetentionWindow window, CancellationToken cancellationToken) =>
        installation.RecordSampleRetentionAsync(window, cancellationToken);
}
