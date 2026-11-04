using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

public class InterestsController(UserManager<ApplicationUser> users, MatchService matches) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Index(string tab = "received")
    {
        var me = await CurrentAsync();
        if (me.Profile is null)
        {
            return NeedsProfile();
        }

        var received = await Matches.ReceivedAsync(me);
        var items = tab switch
        {
            "matches" => await Matches.MatchesAsync(me),
            "sent" => await Matches.SentAsync(me),
            _ => received,
        };
        return View(new InterestsPage(tab is "matches" or "sent" ? tab : "received", items, received.Count));
    }

    [HttpPost]
    [EnableRateLimiting("member-actions")]
    public async Task<IActionResult> Express(string userId, string? returnUrl)
    {
        var outcome = await Matches.ExpressInterestAsync(await CurrentAsync(), userId);
        switch (outcome)
        {
            case InterestOutcome.NeedsProfile:
                return NeedsProfile();
            case InterestOutcome.NotAvailable:
                return NotFound();
            case InterestOutcome.Matched:
                Status("It's a match! You can now send messages to each other.");
                break;
            case InterestOutcome.Sent:
                Status("Your interest has been sent.");
                break;
            default:
                break;
        }

        return BackOr(returnUrl, "Show", "Profile", new { id = userId });
    }

    [HttpPost]
    public async Task<IActionResult> Withdraw(string userId, string? returnUrl)
    {
        await Matches.WithdrawInterestAsync(UserId, userId);
        Status("Your interest has been withdrawn.");
        return BackOr(returnUrl, nameof(Index), "Interests");
    }
}
