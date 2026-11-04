using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MuslimMatrimony.Areas.Identity.Pages.Account;

/// <summary>Same response for every address, so it can't be used to find out who is registered.</summary>
[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    public void OnGet()
    {
    }
}
