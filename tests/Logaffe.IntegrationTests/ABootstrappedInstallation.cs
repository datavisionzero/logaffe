using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// Putting an installation in the state every surface but the bootstrap is
/// reached from: an administrator with a password and a second factor enrolled.
/// </summary>
/// <remarks>
/// <para>
/// It is two acts and not one. The exchange establishes a password and nothing
/// else (ADR 0041); the second factor is enrolled afterwards by the user, and
/// most of what these tests are about is an installation whose administrator
/// did. <see cref="BootstrapEndpointTests"/> is where the other state — an
/// account with no second factor — is asserted.
/// </para>
/// <para>
/// The three keys are set for the whole assembly rather than by each test,
/// because every test but that one wants the same answer to them and setting
/// them has to happen before the host is built (ADR 0054).
/// </para>
/// </remarks>
internal static class ABootstrappedInstallation
{
    public const string TheirPassword = "a passphrase they typed";

    public const string TheirAddress = "administrator@example.com";

    public const string TheirName = "The Administrator";

    public const string TheToken = "the-bootstrap-token-this-installation-names";

    [ModuleInitializer]
    internal static void Configure()
    {
        Environment.SetEnvironmentVariable("Logaffe__Bootstrap__Administrator", TheirName);
        Environment.SetEnvironmentVariable("Logaffe__Bootstrap__Email", TheirAddress);
        Environment.SetEnvironmentVariable("Logaffe__Bootstrap__Token", TheToken);
    }

    /// <returns>
    /// The enrolled second factor's secret, for the codes it produces, and the
    /// sheet that came with it.
    /// </returns>
    public static async Task<Enrolled> SignInAsync(WebApplicationFactory<Program> installation)
    {
        using var client = installation.CreateClient();

        using var exchanged = await client.PostAsJsonAsync(
            "/bootstrap",
            new { token = TheToken, password = TheirPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, exchanged.StatusCode);

        var cookie = Assert.Single(exchanged.Headers.GetValues("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        using var drawing = await client.PostAsync(
            "/second-factor/enrolment", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, drawing.StatusCode);

        var enrolment = (await drawing.Content.ReadFromJsonAsync<Enrolment>(
            TestContext.Current.CancellationToken))!;

        using var enrolled = await client.PutAsJsonAsync(
            "/second-factor",
            new
            {
                password = TheirPassword,
                newSecondFactorCode = Authenticator.CodeFor(enrolment.SecondFactorSecret),
                ticket = enrolment.Ticket,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, enrolled.StatusCode);

        return new Enrolled(enrolment.SecondFactorSecret, enrolment.BackupCodes);
    }

    /// <summary>What the administrator holds once the two acts are done.</summary>
    internal sealed record Enrolled(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes);

    private sealed record Enrolment(
        string SecondFactorSecret,
        string EnrolmentUri,
        IReadOnlyList<string> BackupCodes,
        string Ticket);
}
