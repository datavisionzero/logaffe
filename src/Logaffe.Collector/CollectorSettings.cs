namespace Logaffe.Collector;

/// <summary>
/// Everything a collector is told, which is an address, a token and the mounts
/// to watch.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is nothing else to configure</b>, and that is a decision rather than
/// an omission (ADR 0043): the interval is the product's, the numbers are a
/// closed set (ADR 0044), and the host a delivery belongs to is the token. What
/// is left is where to post, what to post with, and which filesystems to
/// measure — and the first two arrive already filled in, in the
/// <c>docker run</c> line the installation hands over.
/// </para>
/// <para>
/// From the environment and from nowhere else. A container is configured by its
/// environment, the command an operator is handed sets exactly these three, and
/// a configuration file would be a fourth thing to mount for a program with
/// three settings.
/// </para>
/// </remarks>
internal sealed record CollectorSettings(
    Uri Endpoint,
    string Token,
    IReadOnlyList<string> Mounts,
    string ProcPath,
    string RootPath)
{
    public const string EndpointVariable = "LOGAFFE_ENDPOINT";
    public const string TokenVariable = "LOGAFFE_HOST_TOKEN";
    public const string MountsVariable = "LOGAFFE_MOUNTS";
    public const string ProcVariable = "LOGAFFE_PROC_PATH";
    public const string RootVariable = "LOGAFFE_ROOT_PATH";

    /// <summary>
    /// Where the host's own <c>/proc</c> is mounted inside the container, and
    /// where its root filesystem is. They are the two bind mounts of the command
    /// the installation hands over, so they are defaults rather than settings —
    /// they exist as variables so that this can be run outside a container at
    /// all, which is what a person debugging it does.
    /// </summary>
    public const string ProcByDefault = "/host/proc";

    public const string RootByDefault = "/rootfs";

    /// <summary>The prefix that says a token admits a sample (ADR 0031).</summary>
    private const string TokenPrefix = "logaffe_host_";

    /// <summary>
    /// The settings, or the one sentence that says which of them is wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every reason names the variable, because the only person who ever reads
    /// one is looking at a <c>docker run</c> line they pasted and edited. A
    /// collector that started with a bad address and reported nothing would be
    /// a host with no last-reported and nothing to explain it, which is exactly
    /// the state <c>docs/deployment.md</c> tells an operator to read as *the
    /// command is wrong*.
    /// </para>
    /// <para>
    /// The token's prefix is checked here even though the installation checks it
    /// too. An ingest token pasted into this command is the mistake worth
    /// catching, and catching it before the first post turns a machine that
    /// quietly 401s once a minute into a container that says what is wrong and
    /// stops.
    /// </para>
    /// </remarks>
    public static bool TryRead(
        Func<string, string?> environment, out CollectorSettings settings, out string reason)
    {
        settings = null!;

        var endpoint = Trimmed(environment(EndpointVariable));
        if (endpoint is null)
        {
            reason = $"{EndpointVariable} is not set. It is the address of the installation.";
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            reason = $"{EndpointVariable} is not an http or https address: {endpoint}";
            return false;
        }

        var token = Trimmed(environment(TokenVariable));
        if (token is null)
        {
            reason = $"{TokenVariable} is not set. Issue one in the installation's settings.";
            return false;
        }

        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            reason =
                $"{TokenVariable} is not a host token: one begins {TokenPrefix}. "
                + "An ingest token admits log entries and a sample is not one.";
            return false;
        }

        // A collector told to watch nothing still reports the machine — the
        // processor, the memory and the load are not filesystems — so an empty
        // list is a setting rather than a mistake.
        var mounts = (Trimmed(environment(MountsVariable)) ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var mount in mounts)
        {
            if (!mount.StartsWith('/'))
            {
                reason = $"{MountsVariable} names absolute paths, and '{mount}' is not one.";
                return false;
            }
        }

        settings = new CollectorSettings(
            address,
            token,
            mounts,
            Trimmed(environment(ProcVariable)) ?? ProcByDefault,
            Trimmed(environment(RootVariable)) ?? RootByDefault);

        reason = string.Empty;
        return true;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
