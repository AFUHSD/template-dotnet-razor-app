using System.Security.Claims;

namespace App.Web.Services.Access;

/// <summary>
/// Decides, on every request, what the signed-in person may do in THIS app.
///
/// The platform cookie carries identity only (email, name). It deliberately carries no roles: every app on
/// the origin reads the same cookie, so a role written into it would travel to all of them and go stale.
/// Authorization is therefore re-resolved per request from the app's own source of truth, and the result is
/// assigned to <c>HttpContext.User</c> for that request only. It is never written back into the cookie.
/// </summary>
public interface IAccessResolver
{
    /// <summary>
    /// Returns the roles this identity holds in the app, or an empty set for "signed in, but no access".
    /// Must not throw for an unknown person; throw only when the source of truth is unreachable.
    /// </summary>
    Task<IReadOnlyCollection<string>> ResolveRolesAsync(ClaimsPrincipal identity, CancellationToken ct);
}

/// <summary>Role names used by the authorization policies. Add the app's own here.</summary>
public static class AppRoles
{
    /// <summary>May use the app at all. The fallback policy requires it on every page.</summary>
    public const string User = "app-user";
}
