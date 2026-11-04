using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

public class SearchController(UserManager<ApplicationUser> users, MatchService matches) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Index(SearchFilter filter, bool submitted = false)
    {
        var me = await CurrentAsync();
        if (me.Profile is null)
        {
            return NeedsProfile();
        }

        if (!submitted)
        {
            // First visit: start from the member's own preferences.
            filter.AgeMin ??= me.Profile.PreferredAgeMin;
            filter.AgeMax ??= me.Profile.PreferredAgeMax;
        }

        filter.AgeMin = filter.AgeMin is { } a ? Math.Clamp(a, 18, 99) : null;
        filter.AgeMax = filter.AgeMax is { } b ? Math.Clamp(b, 18, 99) : null;
        return View(new SearchPage(filter, await Matches.SearchAsync(me, filter)));
    }
}
