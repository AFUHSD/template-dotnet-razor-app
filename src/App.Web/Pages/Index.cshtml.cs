using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.Web.Pages;

public sealed class IndexModel : PageModel
{
    public string Email { get; private set; } = "";

    public void OnGet()
    {
        Email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "";
    }
}
