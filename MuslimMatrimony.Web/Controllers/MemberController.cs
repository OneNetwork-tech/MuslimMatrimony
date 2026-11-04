using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Controllers;

[Authorize]
public abstract class MemberController(UserManager<ApplicationUser> users, MatchService matches) : Controller
{
    protected MatchService Matches { get; } = matches;

    protected string UserId => users.GetUserId(User) ?? throw new InvalidOperationException("Not signed in");

    protected async Task<ApplicationUser> CurrentAsync() =>
        await Matches.GetMemberAsync(UserId) ?? throw new InvalidOperationException("Signed-in user not found");

    protected IActionResult NeedsProfile()
    {
        TempData["Status"] = "Please complete your profile first.";
        return RedirectToAction("Edit", "Profile");
    }

    protected void Status(string message) => TempData["Status"] = message;

    protected IActionResult BackOr(string? returnUrl, string action, string controller, object? values = null) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(action, controller, values);
}
