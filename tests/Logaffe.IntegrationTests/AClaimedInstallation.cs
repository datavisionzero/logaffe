using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Logaffe.IntegrationTests;

/// <summary>
/// Putting an installation in the state every surface but the claim is reached
/// from: claimed, with a second factor enrolled.
/// </summary>
/// <remarks>
/// <para>
/// It is two acts and not one. The claim establishes a password and nothing else
/// (ADR 0041); the second factor is enrolled afterwards by the operator, and
/// most of what these tests are about is an installation whose operator did.
/// <see cref="ClaimEndpointTests"/> is where the other state — an account with
/// no second factor — is asserted.
/// </para>
/// <para>
/// The secret is read off the volume, which is where the installation wrote it
/// on its first start and how an operator gets it (ADR 0040).
/// </para>
/// </remarks>
internal static class AClaimedInstallation
{
    public const string TheirPassword = "a passphrase they typed";

    /// <returns>
    /// The enrolled second factor's secret, for the codes it produces, and the
    /// sheet that came with it.
    /// </returns>
    public static async Task<Enrolled> ClaimAsync(
        WebApplicationFactory<Program> installation, string volume)
    {
        using var client = installation.CreateClient();

        using var claimed = await client.PostAsJsonAsync(
            "/claim",
            new
            {
                password = TheirPassword,
                secret = File.ReadAllText(Path.Combine(volume, "claim-secret.txt")).Trim(),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);

        var cookie = Assert.Single(claimed.Headers.GetValues("Set-Cookie"));
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

    /// <summary>What the operator holds once the two acts are done.</summary>
    internal sealed record Enrolled(
        string SecondFactorSecret, IReadOnlyList<string> BackupCodes);

    private sealed record Enrolment(
        string SecondFactorSecret,
        string EnrolmentUri,
        IReadOnlyList<string> BackupCodes,
        string Ticket);
}
