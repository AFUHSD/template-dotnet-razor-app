namespace App.Web.Options;

/// <summary>
/// Platform sign-in settings, bound from the <c>Auth</c> section (environment: <c>Auth__*</c>).
///
/// The platform model: one sign-in app on the same origin issues a cookie, and every app under that origin
/// reads it. For the cookie to decrypt here, three values must be byte-identical to the sign-in app's:
/// <see cref="CookieName"/>, <see cref="ApplicationName"/> (mixed into key derivation) and the key ring at
/// <see cref="KeyRingPath"/>. Any mismatch shows up as an endless redirect to sign-in with nothing in any log,
/// which is why the readiness check reports a missing key ring by name.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Read the shared platform cookie. When false, the app runs with a fixed development identity, and that
    /// is refused at startup outside the Development environment.
    /// </summary>
    public bool UsePlatformLogin { get; set; } = true;

    /// <summary>Shared across every app on the origin.</summary>
    public string CookieName { get; set; } = "Platform.Auth";

    /// <summary>DataProtection application name. Must match the sign-in app exactly.</summary>
    public string ApplicationName { get; set; } = "Platform.Apps";

    /// <summary>Folder holding the shared DataProtection key ring. The app identity needs read access.</summary>
    public string? KeyRingPath { get; set; }

    /// <summary>
    /// Origin-absolute path of the platform sign-in page. NOT PathBase-relative: this app lives under a
    /// sub-path, and the sign-in app lives outside it.
    /// </summary>
    public string PlatformLoginPath { get; set; } = "/login";

    /// <summary>Origin-absolute path of the platform sign-out page, shown in the header when set.</summary>
    public string? PlatformLogoutPath { get; set; } = "/login/logout";

    /// <summary>
    /// Shared session length in hours. Must match every app on the origin: with sliding expiration, whichever
    /// app renews the cookie stamps its own lifetime onto it for all of them.
    /// </summary>
    public int SessionHours { get; set; } = 12;

    /// <summary>Development-only identity used when <see cref="UsePlatformLogin"/> is false.</summary>
    public string? DevelopmentEmail { get; set; }
}
