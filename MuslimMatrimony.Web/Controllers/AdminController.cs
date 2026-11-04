using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

[Authorize(Roles = AdminService.Role)]
public class AdminController(AdminService admin, PhotoService photos, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() =>
        View(new AdminPage(await admin.StatsAsync(), await admin.PendingPhotosAsync(), await admin.OpenReportsAsync(), await admin.SuspendedAsync()));

    [HttpPost]
    public async Task<IActionResult> Photo(Guid id, bool approve)
    {
        await photos.ModerateAsync(id, approve);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Dismiss(long id, string? note)
    {
        await admin.DismissReportAsync(id, note);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Suspend(string userId, string? reason)
    {
        TempData["Status"] = await admin.SuspendAsync(users.GetUserId(User)!, userId, reason) ? "Member suspended." : "That member can't be suspended.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Unsuspend(string userId)
    {
        await admin.UnsuspendAsync(userId);
        TempData["Status"] = "Suspension lifted.";
        return RedirectToAction(nameof(Index));
    }
}
