using Logaffe.Api.Hosting;
using Logaffe.Domain.Operators;
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
    public void An_installation_told_nothing_draws_its_own_claim_secret()
    {
        var claim = HostConfiguration.Claim(Configured([]));

        // The default, and the one an unattended installation runs with
        // (ADR 0040).
        Assert.Equal(ClaimMode.Secret, claim.Mode);
        Assert.Null(claim.SuppliedSecret);
        Assert.True(claim.DrawsItsOwnSecret);
    }

    [Theory]
    [InlineData("window")]
    [InlineData("Window")]
    [InlineData("WINDOW")]
    public void The_other_guard_is_named_however_it_is_written(string mode)
    {
        var claim = HostConfiguration.Claim(
            Configured([new("Logaffe:Claim:Mode", mode)]));

        Assert.Equal(ClaimMode.Window, claim.Mode);
        Assert.False(claim.DrawsItsOwnSecret);
    }

    [Fact]
    public void A_supplied_secret_is_read_and_kept_out_of_the_database()
    {
        var claim = HostConfiguration.Claim(Configured(
        [
            new("Logaffe:Claim:Mode", "secret"),
            new("Logaffe:Claim:Secret", "the-one-the-compose-file-names"),
        ]));

        Assert.NotNull(claim.SuppliedSecret);
        Assert.False(claim.DrawsItsOwnSecret);
    }

    /// <summary>
    /// Both mistakes stop the start rather than being served on: a mode that is
    /// not one is a typo, and a short secret is the one public door a guess
    /// opens.
    /// </summary>
    [Theory]
    [InlineData("sceret", null)]
    [InlineData("secret", "short")]
    [InlineData("window", "a secret that is never presented to anything")]
    public void A_claim_nobody_could_have_meant_stops_the_start(string mode, string? secret)
    {
        var settings = new List<KeyValuePair<string, string?>>
        {
            new("Logaffe:Claim:Mode", mode),
        };

        if (secret is not null)
        {
            settings.Add(new("Logaffe:Claim:Secret", secret));
        }

        Assert.Throws<InvalidOperationException>(
            () => HostConfiguration.Claim(Configured(settings)));
    }

    private static IConfiguration Configured(
        IEnumerable<KeyValuePair<string, string?>> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
