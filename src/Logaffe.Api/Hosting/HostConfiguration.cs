using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.Identities;

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
    /// What the first start creates its first administrator from, as the compose
    /// file says it (ADR 0054).
    /// </summary>
    /// <remarks>
    /// Any of the three may be missing, and what that means is the bootstrap's
    /// to decide rather than this method's: an installation that was never told
    /// anything starts and says so, while one that was told something it cannot
    /// use does not start at all.
    /// </remarks>
    public static BootstrapSettings Bootstrap(IConfiguration configuration) =>
        new(
            configuration[BootstrapSettings.AdministratorKey],
            configuration[BootstrapSettings.EmailKey],
            configuration[BootstrapSettings.TokenKey]);

    /// <summary>
    /// The SMTP account this installation sends through, and the address its
    /// links are built from (ADR 0053).
    /// </summary>
    /// <remarks>
    /// Read at startup rather than at the moment somebody presses invite, so
    /// that a mail configuration that is wrong is found by whoever brought the
    /// installation up rather than by the person waiting for a message.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Something is set that this installation will not send with.
    /// </exception>
    public static SmtpSettings Smtp(IConfiguration configuration, bool development) =>
        SmtpSettings.Read(
            configuration[SmtpSettings.HostKey],
            configuration[SmtpSettings.PortKey],
            configuration[SmtpSettings.UsernameKey],
            configuration[SmtpSettings.PasswordKey],
            configuration[SmtpSettings.SecurityKey],
            configuration[SmtpSettings.FromKey],
            configuration[SmtpSettings.FromNameKey],
            configuration[SmtpSettings.PublicUrlKey],
            development);
}
