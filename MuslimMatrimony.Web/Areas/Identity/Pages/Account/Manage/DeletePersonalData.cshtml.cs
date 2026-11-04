using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, GdprService gdpr, IStringLocalizer<SharedResource> l, ILogger<DeletePersonalDataModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RequirePassword { get; set; }

    public sealed class InputModel
    {
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        RequirePassword = await users.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        RequirePassword = await users.HasPasswordAsync(user);
        if (RequirePassword && !await users.CheckPasswordAsync(user, Input.Password ?? string.Empty))
        {
            ModelState.AddModelError(string.Empty, l["Incorrect password."]);
            return Page();
        }

        if (!await gdpr.DeleteAccountAsync(user))
        {
            throw new InvalidOperationException("Unexpected error deleting the account.");
        }

        await signIn.SignOutAsync();
        logger.LogInformation("Member deleted their account");
        TempData["Status"] = "Your account and all your data have been deleted.";
        return Redirect("~/");
    }
}
