using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Controllers;

public sealed record DashboardModel(ApplicationUser Member, int Matches, int Received, int Unread);

public class HomeController(UserManager<ApplicationUser> users, MatchService matches, MessageService messages) : Controller
{
    public async Task<IActionResult> Index()
    {
        var id = users.GetUserId(User);
        var member = id is null ? null : await matches.GetMemberAsync(id);
        if (member is null)
        {
            return View();
        }

        var model = member.Profile is null
            ? new DashboardModel(member, 0, 0, 0)
            : new DashboardModel(member, (await matches.MatchesAsync(member)).Count, await matches.PendingReceivedCountAsync(member), await messages.UnreadCountAsync(member.Id));
        return View("Dashboard", model);
    }

    public IActionResult Privacy() => View();

    public IActionResult Terms() => View();

    [HttpPost]
    [AllowAnonymous]
    public IActionResult SetLanguage(string? culture, string? returnUrl)
    {
        if (culture is not null && Cultures.Supported.Contains(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Secure = Request.IsHttps, HttpOnly = true, SameSite = SameSiteMode.Lax });
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [Route("/Home/Status/{code:int}")]
    public IActionResult Status(int code)
    {
        ViewData["Code"] = code;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
