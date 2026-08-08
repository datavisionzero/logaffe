using Logaffe.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Logaffe.IntegrationTests;

/// <summary>
/// One binary is the server and the command line both, so the two have to read
/// one configuration.
/// </summary>
/// <remarks>
/// This is asked of the composition root rather than of the line that was
/// supposed to say so, which is why it sits in the project that references
/// <c>Logaffe.Api</c>. It needs no database: what it checks is which files a
/// host layers in and where it looks for them.
/// </remarks>
public sealed class HostConfigurationTests
{
    [Fact]
    public void The_server_and_a_verb_are_told_the_same_environment()
    {
        var server = HostConfiguration.ForTheServer([]);
        var verb = HostConfiguration.ForAVerb();

        Assert.Equal(server.EnvironmentName, verb.EnvironmentName);
        Assert.Equal(HostConfiguration.EnvironmentName, verb.EnvironmentName);
    }

    /// <summary>
    /// The failure the fix is for: a verb in a working clone reported a missing
    /// connection string, because its host resolved the environment from a
    /// variable nothing sets and never layered
    /// <c>appsettings.Development.json</c> in.
    /// </summary>
    [Fact]
    public void A_verb_reads_the_settings_file_of_its_environment()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = HostConfiguration.ForAVerb().ContentRootPath,
            EnvironmentName = Environments.Development,
        });

        Assert.NotNull(builder.Configuration.GetConnectionString("Postgres"));
        Assert.Equal(
            "../../volume",
            HostConfiguration.VolumePath(builder.Configuration));
    }
}
