using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.Web.Pages;

public sealed class DeniedModel : PageModel
{
    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
