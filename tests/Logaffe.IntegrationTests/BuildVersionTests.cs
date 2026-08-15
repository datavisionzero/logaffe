using System.Reflection;
using Logaffe.Api.Hosting;

namespace Logaffe.IntegrationTests;

/// <summary>
/// What a build calls itself, and where the commit behind it stops.
/// </summary>
/// <remarks>
/// <para>
/// This sits here for the reason <see cref="HostConfigurationTests"/> does: it
/// asks something of <c>Logaffe.Api</c>, and this is the project that references
/// it. It needs no database.
/// </para>
/// <para>
/// The cut in <see cref="Build"/> used to be defensive — nothing in a container
/// produced a commit for it to remove, because <c>Dockerfile.dockerignore</c>
/// excludes <c>.git/</c> and the workflows passed no revision. Both now do, so
/// that a trunk build says which one it is, and this is the line that keeps that
/// out of a backup manifest (ADR 0024).
/// </para>
/// </remarks>
public sealed class BuildVersionTests
{
    [Fact]
    public void The_commit_is_the_builds_business_and_not_the_artifacts()
    {
        var informational = typeof(Build).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // A build that can see a repository carries the commit behind a `+`, and
        // one made from a source tree without one does not — so this asserts what
        // is cut rather than that there was something to cut.
        Assert.Equal(informational.Split('+')[0], Build.Version);

        // The load-bearing half: an artifact records this number, and a version
        // that grew a commit would change what a manifest says without anyone
        // deciding it should.
        Assert.DoesNotContain('+', Build.Version);
    }
}
