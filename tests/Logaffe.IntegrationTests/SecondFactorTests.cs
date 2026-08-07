using Logaffe.Infrastructure.Secrets;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The TOTP adapter, checked against RFC 6238's own test vector before anything
/// else.
/// </summary>
/// <remarks>
/// It is the one part of the sign-in the product does not get to define: a code
/// is right when the authenticator app in the operator's pocket says it is, so
/// agreeing with the specification everybody else implemented is the whole
/// requirement.
/// </remarks>
public sealed class SecondFactorTests
{
    /// <summary>
    /// RFC 6238's SHA-1 secret — the twenty ASCII bytes <c>12345678901234567890</c>
    /// — written in base32 the way an authenticator app is given it.
    /// </summary>
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    /// <summary>
    /// The specification's third vector, <c>T = 1111111109</c>, whose
    /// eight-digit code is 07081804. The drift is measured around this one
    /// rather than around the first, because a minute either side of the first
    /// is on the far side of the epoch.
    /// </summary>
    private static readonly DateTimeOffset Later = DateTimeOffset.FromUnixTimeSeconds(1111111109);

    private const string LaterCode = "081804";

    private readonly Rfc6238SecondFactor secondFactor = new();

    [Fact]
    public void The_specification_s_own_vector_verifies() =>
        // T = 59 seconds is RFC 6238's first vector, whose eight-digit code is
        // 94287082 and whose six-digit code is therefore this.
        Assert.True(secondFactor.Verifies(
            RfcSecret, "287082", DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void A_minted_secret_is_the_size_an_app_expects_and_is_its_own()
    {
        var secret = secondFactor.MintSecret();

        // A hundred and sixty bits in base32 is thirty-two characters — the line
        // under the QR code for an operator whose camera will not focus.
        Assert.Equal(32, secret.Length);
        Assert.NotEqual(secret, secondFactor.MintSecret());
        // And a code that is right for another secret is nothing here.
        Assert.False(secondFactor.Verifies(
            secret, "287082", DateTimeOffset.FromUnixTimeSeconds(59)));
    }

    [Fact]
    public void A_secret_typed_back_the_way_it_was_shown_still_works() =>
        // Lowercase, in groups, out of an app that shows it that way: it is the
        // same secret.
        Assert.True(secondFactor.Verifies(
            "gezd gnbv gy3t qojq gezd gnbv gy3t qojq",
            "287082",
            DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void A_phone_whose_clock_is_half_a_minute_out_still_works()
    {
        Assert.True(secondFactor.Verifies(RfcSecret, LaterCode, Later.AddSeconds(-30)));
        Assert.True(secondFactor.Verifies(RfcSecret, LaterCode, Later.AddSeconds(30)));
    }

    [Fact]
    public void A_clock_that_is_further_out_than_that_is_not_carried()
    {
        // Every step of slack is a step an attacker gets to guess in as well.
        Assert.False(secondFactor.Verifies(RfcSecret, LaterCode, Later.AddSeconds(-60)));
        Assert.False(secondFactor.Verifies(RfcSecret, LaterCode, Later.AddSeconds(60)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("28708")]
    [InlineData("2870820")]
    [InlineData("28708a")]
    [InlineData("287081")]
    public void What_is_not_this_code_is_refused(string? code) =>
        Assert.False(secondFactor.Verifies(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void A_secret_that_is_not_base32_verifies_nothing() =>
        Assert.False(secondFactor.Verifies(
            "not base32 at all!", "287082", DateTimeOffset.FromUnixTimeSeconds(59)));

    [Fact]
    public void The_enrolment_address_is_what_an_app_reads()
    {
        var uri = secondFactor.EnrolmentUri(RfcSecret, "logs.example.com");

        Assert.StartsWith("otpauth://totp/logaffe%3Alogs.example.com?", uri);
        Assert.Contains($"secret={RfcSecret}", uri);
        Assert.Contains("issuer=logaffe", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}
