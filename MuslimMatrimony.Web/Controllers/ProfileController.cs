using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using MuslimMatrimony.ViewModels;

namespace MuslimMatrimony.Controllers;

public class ProfileController(UserManager<ApplicationUser> users, MatchService matches, ApplicationDbContext db) : MemberController(users, matches)
{
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var me = await CurrentAsync();
        return View(me.Profile is null ? new ProfileForm() : ProfileForm.From(me.Profile));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ProfileForm form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var me = await CurrentAsync();
        var now = DateTime.UtcNow;
        var isNew = me.Profile is null;
        var profile = me.Profile ?? new Profile { UserId = me.Id, CreatedAt = now };
        form.ApplyTo(profile);
        profile.UpdatedAt = now;
        if (isNew)
        {
            db.Profiles.Add(profile);
        }

        await db.SaveChangesAsync();
        Status(isNew ? "Your profile has been created. Add a photo and start searching." : "Your profile has been saved.");
        return isNew ? RedirectToAction("Index", "Photos") : RedirectToAction(nameof(Edit));
    }

    /// <summary>Another member's profile. Anything not viewable is a 404 so IDs can't be probed.</summary>
    [HttpGet("/Profile/View/{id}")]
    public async Task<IActionResult> Show(string id)
    {
        var me = await CurrentAsync();
        if (me.Profile is null)
        {
            return NeedsProfile();
        }

        if (id == me.Id)
        {
            return RedirectToAction(nameof(Edit));
        }

        var profile = await Matches.GetViewableProfileAsync(me, id);
        if (profile is null)
        {
            return NotFound();
        }

        var isMatch = await Matches.IsMatchAsync(me.Id, id);
        var photoIds = MatchService.CanSeePhotos(profile, isMatch)
            ? profile.Photos.Where(p => p.Status == PhotoStatus.Approved).OrderByDescending(p => p.IsPrimary).ThenBy(p => p.CreatedAt).Select(p => p.Id).ToList()
            : [];
        return View("Show", new ProfilePage(profile, Age.On(profile.User!.DateOfBirth, Age.Today), isMatch,
            await Matches.HasSentInterestAsync(me.Id, id), await Matches.HasSentInterestAsync(id, me.Id), photoIds));
    }
}
