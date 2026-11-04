using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Controllers;

public class PhotosController(UserManager<ApplicationUser> users, MatchService matches, PhotoService photos) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var me = await CurrentAsync();
        if (me.Profile is null)
        {
            return NeedsProfile();
        }

        return View(await photos.ForOwnerAsync(me.Id));
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        var outcome = await photos.UploadAsync(UserId, file);
        if (outcome == UploadOutcome.NeedsProfile)
        {
            return NeedsProfile();
        }

        Status(outcome switch
        {
            UploadOutcome.Uploaded => "Photo uploaded. It will be visible to others once a moderator has approved it.",
            UploadOutcome.TooMany => "You have reached the maximum number of photos.",
            UploadOutcome.TooLarge => "The photo is too large (max 5 MB).",
            _ => "Please upload a JPEG, PNG or WebP image.",
        });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        await photos.DeleteAsync(UserId, id);
        Status("Photo deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Primary(Guid id)
    {
        await photos.SetPrimaryAsync(UserId, id);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/Photos/File/{id:guid}")]
    public async Task<IActionResult> File(Guid id)
    {
        var file = await photos.OpenAsync(await CurrentAsync(), id, User.IsInRole(AdminService.Role));
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=300";
        return PhysicalFile(file.Value.Path, file.Value.ContentType);
    }
}
