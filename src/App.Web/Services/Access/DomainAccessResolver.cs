using System.Security.Claims;
using App.Web.Options;
using Microsoft.Extensions.Options;

namespace App.Web.Services.Access;

/// <summary>
/// EXAMPLE access rule: anyone whose email is in <see cref="AccessOptions.AllowedEmailDomain"/> is a user.
///
/// TODO: replace this with the app's own rule, such as a lookup of the person in a staff table or directory
/// group. Keep the shape: resolve on every request, fail closed (no match means no roles), and never cache a
/// grant for longer than you would tolerate a removed person keeping access.
/// </summary>
public sealed class DomainAccessResolver(IOptionsMonitor<AccessOptions> options) : IAccessResolver
{
    public Task<IReadOnlyCollection<string>> ResolveRolesAsync(ClaimsPrincipal identity, CancellationToken ct)
    {
        var domain = options.CurrentValue.AllowedEmailDomain?.Trim().TrimStart('@');
        var email = identity.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(email))
            return Task.FromResult<IReadOnlyCollection<string>>([]);

        var allowed = email.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyCollection<string>>(allowed ? [AppRoles.User] : []);
    }
}
