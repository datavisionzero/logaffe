using Logaffe.Domain.Operators;

namespace Logaffe.Api.Http;

/// <summary>
/// The cookie a session secret travels in, and the only place it is written
/// down.
/// </summary>
/// <remarks>
/// <para>
/// A cookie rather than a header the single-page application keeps, because
/// anything the script can read is anything an injected script can read, and
/// this value is the whole of the operator's standing permission. <c>HttpOnly</c>
/// is the point of choosing it.
/// </para>
/// <para>
/// <c>Secure</c> is unconditional. An installation on the public internet is
/// behind TLS by <c>docs/operations.md</c>, and a browser already treats
/// <c>localhost</c> as a secure origin, so the one case this refuses is a
/// deployment serving plain HTTP under a real name — which is a deployment that
/// should not be holding this cookie.
/// </para>
/// <para>
/// <c>SameSite=Strict</c> because there is no cross-site anything: the operator
/// arrives at the installation's own address and everything they do is same
/// origin. Nothing in the product is linked to from elsewhere, so the usual cost
/// of <c>Strict</c> — a link from another site landing signed out — is not paid
/// here.
/// </para>
/// </remarks>
public static class SessionCookie
{
    /// <summary>
    /// Named for the product rather than for the framework, so that what it is
    /// is legible in a browser's storage inspector.
    /// </summary>
    public const string Name = "logaffe_session";

    /// <summary>
    /// Gives the browser a secret to present from now on, with the same sliding
    /// deadline the row carries.
    /// </summary>
    /// <remarks>
    /// It is written again whenever the session's last use was written, which is
    /// at most once every <see cref="Application.Operations.AuthenticateSession.UseWriteInterval"/>.
    /// Setting it on every response would keep the two deadlines in exact step
    /// and put a <c>Set-Cookie</c> on the tail request the log view makes every
    /// few seconds; letting the cookie be the coarser of the two costs the
    /// operator nothing, since the row is what admits anything.
    /// </remarks>
    public static void Issue(HttpResponse response, string secret) =>
        response.Cookies.Append(Name, secret, Options(Session.SlidingLifetime));

    /// <summary>
    /// Takes the cookie back — a sign-out, and a secret that named no live
    /// session, which is a browser that would otherwise present a dead value on
    /// every request for the next thirty days.
    /// </summary>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(TimeSpan.Zero));

    /// <remarks>
    /// <c>Delete</c> has to be given the same attributes the cookie was written
    /// with, or the browser keeps the one it has and takes a second one beside
    /// it.
    /// </remarks>
    private static CookieOptions Options(TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = lifetime,
    };
}
