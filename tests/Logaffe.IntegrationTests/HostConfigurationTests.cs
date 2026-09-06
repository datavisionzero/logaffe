using Logaffe.Api.Hosting;
using Logaffe.Domain.Identities;
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

    [Fact]
    public void An_installation_told_nothing_bootstraps_nobody()
    {
        var bootstrap = HostConfiguration.Bootstrap(Configured([]));

        // Not a refusal: an installation that was told nothing starts anyway and
        // says so, because ingestion needs no identity (ADR 0054).
        Assert.Null(bootstrap.Administrator);
        Assert.Null(bootstrap.Email);
        Assert.Null(bootstrap.Token);
        Assert.False(bootstrap.IsComplete);
    }

    [Fact]
    public void The_three_keys_are_read_together()
    {
        var bootstrap = HostConfiguration.Bootstrap(Configured(
        [
            new("Logaffe:Bootstrap:Administrator", "The Administrator"),
            new("Logaffe:Bootstrap:Email", "somebody@example.com"),
            new("Logaffe:Bootstrap:Token", "a-bootstrap-token-long-enough-to-be-one"),
        ]));

        Assert.Equal("The Administrator", bootstrap.Administrator);
        Assert.Equal("somebody@example.com", bootstrap.Email);
        Assert.Equal("a-bootstrap-token-long-enough-to-be-one", bootstrap.Token);
        Assert.True(bootstrap.IsComplete);
    }

    [Theory]
    [InlineData(null, "somebody@example.com", "a-bootstrap-token-long-enough-to-be-one")]
    [InlineData("The Administrator", null, "a-bootstrap-token-long-enough-to-be-one")]
    [InlineData("The Administrator", "somebody@example.com", null)]
    public void Two_of_the_three_bootstrap_nobody(
        string? administrator, string? email, string? token)
    {
        var settings = new List<KeyValuePair<string, string?>>();

        if (administrator is not null)
        {
            settings.Add(new("Logaffe:Bootstrap:Administrator", administrator));
        }

        if (email is not null)
        {
            settings.Add(new("Logaffe:Bootstrap:Email", email));
        }

        if (token is not null)
        {
            settings.Add(new("Logaffe:Bootstrap:Token", token));
        }

        // Reading them is not where a partial configuration is judged: the
        // bootstrap decides what to do about it, and says so in the log.
        Assert.False(HostConfiguration.Bootstrap(Configured(settings)).IsComplete);
    }

    private static IConfiguration Configured(
        IEnumerable<KeyValuePair<string, string?>> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
