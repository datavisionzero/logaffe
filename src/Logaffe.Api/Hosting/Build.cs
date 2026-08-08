using System.Reflection;

namespace Logaffe.Api.Hosting;

/// <summary>
/// Which logaffe this is.
/// </summary>
/// <remarks>
/// It comes off the assembly rather than out of a constant, so that there is one
/// number and the build is what sets it. A backup artifact records it (ADR 0024)
/// and an operator reading one wants to know what wrote it.
/// </remarks>
public static class Build
{
    public static string Version { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(Build).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // The informational version carries the commit after a `+`, which is the
        // build's business rather than the artifact's.
        return informational?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
