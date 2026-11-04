using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

public class SafetyController(UserManager<ApplicationUser> users, MatchService matches, SafetyService safety) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Blocked() => View(await safety.BlockedAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Block(string userId)
    {
        if (!await safety.BlockAsync(UserId, userId))
        {
            return NotFound();
        }

        Status("Member blocked. You will no longer see each other.");
        return RedirectToAction("Index", "Search");
    }

    [HttpPost]
    public async Task<IActionResult> Unblock(string userId)
    {
        await safety.UnblockAsync(UserId, userId);
        Status("Member unblocked.");
        return RedirectToAction(nameof(Blocked));
    }

    [HttpGet]
    public async Task<IActionResult> Report(string id)
    {
        var target = await Matches.GetMemberAsync(id);
        if (target is null || id == UserId)
        {
            return NotFound();
        }

        ViewData["Name"] = target.FirstName;
        return View(new ReportForm { UserId = id });
    }

    [HttpPost]
    [EnableRateLimiting("member-actions")]
    public async Task<IActionResult> Report(ReportForm form)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(form.Reason))
        {
            ViewData["Name"] = (await Matches.GetMemberAsync(form.UserId))?.FirstName;
            return View(form);
        }

        if (!await safety.ReportAsync(UserId, form.UserId, form.Reason, form.Details))
        {
            return NotFound();
        }

        if (form.AlsoBlock)
        {
            await safety.BlockAsync(UserId, form.UserId);
        }

        Status("Thank you. Our moderators will review your report.");
        return RedirectToAction("Index", "Search");
    }
}
