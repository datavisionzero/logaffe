using Logaffe.Application.Ports;
using Logaffe.Domain.Operators;

namespace Logaffe.Api.Hosting;

/// <summary>
/// The one place either host settles what configuration it is reading.
/// </summary>
/// <remarks>
/// <para>
/// One binary is the server and the command line both (<c>docs/codebase.md</c>),
/// and the two are built by two different hosts: the verbs by the generic host,
/// the server by the web one. Left to themselves they do not agree on where the
/// environment comes from — the generic host reads <c>DOTNET_ENVIRONMENT</c>,
/// the web host reads <c>ASPNETCORE_ENVIRONMENT</c> — so a clone whose
/// <c>launchSettings.json</c> sets one of them has a server that layers in
/// <c>appsettings.Development.json</c> and verbs that do not. What the verb then
/// reports is not a missing environment but a missing connection string, which
/// is a confusing sentence to meet at the moment <c>recover</c> is reached for.
/// </para>
/// <para>
/// So neither host resolves it. Both are handed the answer from here, and both
/// variables are read, in the order the framework would have read the one it
/// knows. Setting the other variable in <c>launchSettings.json</c> would have
/// fixed the case that was observed and left the cause — two hosts reading two
/// different variables — waiting for the next verb.
/// </para>
/// </remarks>
public static class HostConfiguration
{
    /// <summary>
    /// Which environment both hosts are in, or <see langword="null"/> for the
    /// framework's own default.
    /// </summary>
    /// <remarks>
    /// In a container this is empty and nothing is layered in — configuration
    /// arrives as environment variables, which both hosts read alike. It is the
    /// clone this exists for.
    /// </remarks>
    public static string? EnvironmentName =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

    /// <summary>
    /// What <c>Program</c> builds the server from.
    /// </summary>
    public static WebApplicationOptions ForTheServer(string[] args) => new()
    {
        Args = args,
        EnvironmentName = EnvironmentName,
    };

    /// <summary>
    /// What a verb builds its own host from.
    /// </summary>
    /// <remarks>
    /// Without the arguments: a bare verb is not a configuration argument, and
    /// the command line provider refuses one. The content root is the directory
    /// the binary sits in, which is where the settings files are published
    /// beside it — a verb is run from wherever the operator happens to be.
    /// </remarks>
    public static HostApplicationBuilderSettings ForAVerb() => new()
    {
        ContentRootPath = AppContext.BaseDirectory,
        EnvironmentName = EnvironmentName,
    };

    /// <summary>
    /// Where everything that is not in the database lives.
    /// </summary>
    /// <remarks>
    /// Read the same way by the server and by every verb, because a verb that
    /// looked somewhere else would write its half of a backup into a directory
    /// the installation does not use (ADR 0024).
    /// </remarks>
    public static string VolumePath(IConfiguration configuration) =>
        configuration["Logaffe:VolumePath"]
        ?? throw new InvalidOperationException("Logaffe:VolumePath is not configured.");

    /// <summary>
    /// How this installation guards its claim, as the compose file says it
    /// (ADR 0040).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the server and by <c>recover</c> alike, because the command opens
    /// whichever door the installation is configured for and a verb reading this
    /// differently would open the other one.
    /// </para>
    /// <para>
    /// <b>Both mistakes stop the start.</b> A mode that is not one of the two is
    /// a typo, and a secret below the minimum is the one public door a guess
    /// opens — neither is a thing to accept quietly and serve on.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The mode is not a mode, or the supplied secret is too short.
    /// </exception>
    public static ClaimSettings Claim(IConfiguration configuration)
    {
        var mode = configuration["Logaffe:Claim:Mode"];
        var secret = configuration["Logaffe:Claim:Secret"];

        var chosen = string.IsNullOrWhiteSpace(mode)
            ? ClaimSettings.Default.Mode
            : Enum.TryParse<ClaimMode>(mode, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Logaffe:Claim:Mode is '{mode}', which is neither `secret` nor "
                    + "`window`. See docs/setup.md.");

        if (string.IsNullOrEmpty(secret))
        {
            return new ClaimSettings(chosen, null);
        }

        if (chosen is ClaimMode.Window)
        {
            throw new InvalidOperationException(
                "Logaffe:Claim:Secret is set and Logaffe:Claim:Mode is `window`, which "
                + "would guard the claim with nothing while looking as though it guarded "
                + "it with that. Pick one.");
        }

        return ClaimSecret.TryCreate(secret, out var supplied)
            ? new ClaimSettings(chosen, supplied)
            : throw new InvalidOperationException(
                $"Logaffe:Claim:Secret is shorter than {ClaimSecret.MinimumLength} "
                + "characters. It is the whole of what stands in front of the claim, so "
                + "draw it rather than think of it — or leave it empty and let the "
                + "installation draw one.");
    }
}
