using System.Security.Claims;
using System.Text.Encodings.Web;
using App.Web.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace App.Web.Services.Access;

/// <summary>
/// Development only: every request is signed in as <see cref="AuthOptions.DevelopmentEmail"/>, so the app can
/// be run on a workstation that cannot reach the shared key ring. Program.cs refuses to register it outside
/// the Development environment. Authorization still runs through <see cref="IAccessResolver"/>, so the
/// development identity is subject to the same access rule as anyone else.
/// </summary>
public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<AuthOptions> auth) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = auth.Value.DevelopmentEmail;
        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, email)],
            SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect(Request.PathBase + "/denied");
        return Task.CompletedTask;
    }
}
