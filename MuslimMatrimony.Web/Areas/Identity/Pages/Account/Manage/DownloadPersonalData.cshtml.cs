using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Areas.Identity.Pages.Account.Manage;

/// <summary>Replaces Identity's export (which only covers account fields) with a full export of profile, photos, interests and messages.</summary>
public class DownloadPersonalDataModel(UserManager<ApplicationUser> users, GdprService gdpr, ILogger<DownloadPersonalDataModel> logger) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = users.GetUserId(User);
        var zip = userId is null ? null : await gdpr.ExportAsync(userId);
        if (zip is null)
        {
            return NotFound();
        }

        logger.LogInformation("Member exported their personal data");
        Response.Headers.CacheControl = "no-store";
        return File(zip, "application/zip", "nikahnoor-personal-data.zip");
    }
}
