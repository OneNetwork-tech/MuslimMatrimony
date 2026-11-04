using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

public class MessagesController(UserManager<ApplicationUser> users, MatchService matches, MessageService messages) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await messages.ConversationsAsync(UserId));

    [HttpGet("/Messages/With/{id}")]
    public async Task<IActionResult> Thread(string id)
    {
        var thread = await messages.ThreadAsync(UserId, id);
        var other = thread is null ? null : await Matches.GetMemberAsync(id);
        if (thread is null || other is null)
        {
            return NotFound();
        }

        return View(new ThreadPage(id, other.FirstName, await Matches.IsMatchAsync(UserId, id), thread));
    }

    [HttpPost]
    [EnableRateLimiting("member-actions")]
    public async Task<IActionResult> Send(string userId, string? body)
    {
        switch (await messages.SendAsync(UserId, userId, body))
        {
            case SendOutcome.NotAllowed:
                return NotFound();
            case SendOutcome.Invalid:
                Status("Messages must be between 1 and 2000 characters.");
                break;
            default:
                break;
        }

        return RedirectToAction(nameof(Thread), new { id = userId });
    }
}
