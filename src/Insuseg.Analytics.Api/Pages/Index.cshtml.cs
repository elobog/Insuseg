using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Insuseg.Analytics.Api.Pages;

[Authorize(AuthenticationSchemes = "Identity.Application")]
public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Ventas/Analisis");
}
