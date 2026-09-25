using System.Security.Claims;
using App.Web.Services.Events;

namespace App.Web.Services.Access;

/// <summary>
/// Runs between authentication and authorization, so policies see the roles the source of truth grants
/// today rather than anything decided at sign-in.
///
/// A person with no roles stays signed in (the platform cookie is shared, and signing it out here would sign
/// them out of every app) but fails the fallback policy, which sends them to the Denied page.
/// </summary>
public sealed partial class AccessResolutionMiddleware(RequestDelegate next, ILogger<AccessResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAccessResolver resolver, IEventLog events)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var roles = await resolver.ResolveRolesAsync(context.User, context.RequestAborted);

        // Identity claims only from the cookie; roles only from the resolver.
        var identity = new ClaimsIdentity(
            context.User.Claims.Where(c => c.Type != ClaimTypes.Role),
            context.User.Identity.AuthenticationType,
            ClaimTypes.Name,
            ClaimTypes.Role);
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        context.User = new ClaimsPrincipal(identity);

        if (roles.Count == 0)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? "(no email claim)";
            LogDenied(logger, email, context.Request.Path.Value);
            await events.SafeWriteAsync(
                new AppEvent("access-denied", "Denied", email, new { path = context.Request.Path.Value }),
                logger, context.RequestAborted);
        }

        await next(context);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Access denied for {Email} on {Path}")]
    private static partial void LogDenied(ILogger logger, string email, string? path);
}
