using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

public static class Age
{
    public static int On(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>A profile summary as another member sees it.</summary>
public sealed record ProfileCard(
    string UserId,
    string FirstName,
    int Age,
    string City,
    string Country,
    MaritalStatus MaritalStatus,
    ReligiousPractice ReligiousPractice,
    string? Profession,
    Guid? PhotoId,
    DateTime? LastActiveAt);

public sealed class SearchFilter
{
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public Sect? Sect { get; set; }
    public ReligiousPractice? Practice { get; set; }
    public bool? WillingToRelocate { get; set; }
    public bool? WithPhoto { get; set; }
    public string Sort { get; set; } = "active";
    public int Page { get; set; } = 1;
}

public sealed record PagedCards(IReadOnlyList<ProfileCard> Items, int Page, int TotalPages, int Total);

public enum InterestOutcome
{
    Sent,
    Matched,
    AlreadySent,
    NotAvailable,
    NeedsProfile,
}

/// <summary>
/// Who can see whom. All rules live here so every page applies the same ones:
/// opposite gender, confirmed and not suspended, not blocked in either direction, and
/// hidden profiles are visible only to existing matches.
/// </summary>
public sealed class MatchService(ApplicationDbContext db, IEmailSender email, ILogger<MatchService> logger)
{
    public const int PageSize = 20;

    public async Task<ApplicationUser?> GetMemberAsync(string userId) =>
        await db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == userId);

    private IQueryable<Profile> Eligible(ApplicationUser viewer) =>
        db.Profiles.Where(p =>
            p.UserId != viewer.Id &&
            p.User!.Gender != viewer.Gender &&
            p.User.EmailConfirmed &&
            p.User.SuspensionReason == null &&
            !db.Blocks.Any(b => (b.BlockerId == viewer.Id && b.BlockedId == p.UserId) || (b.BlockerId == p.UserId && b.BlockedId == viewer.Id)));

    /// <summary>Profiles that appear in search and can receive new interests.</summary>
    public IQueryable<Profile> Discoverable(ApplicationUser viewer) => Eligible(viewer).Where(p => !p.IsHidden);

    private IQueryable<string> MatchIds(string userId) =>
        db.Interests.Where(i => i.FromUserId == userId && db.Interests.Any(back => back.FromUserId == i.ToUserId && back.ToUserId == userId))
            .Select(i => i.ToUserId);

    public async Task<bool> IsMatchAsync(string a, string b)
    {
        if (a == b)
        {
            return false;
        }

        return await db.Interests.AnyAsync(i => i.FromUserId == a && i.ToUserId == b) &&
               await db.Interests.AnyAsync(i => i.FromUserId == b && i.ToUserId == a) &&
               !await db.Blocks.AnyAsync(x => (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a)) &&
               await db.Users.CountAsync(u => (u.Id == a || u.Id == b) && u.SuspensionReason == null) == 2;
    }

    /// <summary>The profile another member may open, or null (treated as "not found").</summary>
    public async Task<Profile?> GetViewableProfileAsync(ApplicationUser viewer, string targetId)
    {
        if (viewer.Profile is null)
        {
            return null;
        }

        var isMatch = await IsMatchAsync(viewer.Id, targetId);
        return await Eligible(viewer)
            .Include(p => p.User)
            .Include(p => p.Photos)
            .Where(p => p.UserId == targetId && (!p.IsHidden || isMatch))
            .AsSplitQuery()
            .FirstOrDefaultAsync();
    }

    /// <summary>Approved photos of <paramref name="owner"/> the viewer may see.</summary>
    public static bool CanSeePhotos(Profile owner, bool isMatch) => isMatch || owner.PhotoVisibility == PhotoVisibility.AllMembers;

    public async Task<PagedCards> SearchAsync(ApplicationUser viewer, SearchFilter f)
    {
        var today = Age.Today;
        var q = Discoverable(viewer);

        if (f.AgeMin is int min)
        {
            var latestBirth = today.AddYears(-min);
            q = q.Where(p => p.User!.DateOfBirth <= latestBirth);
        }

        if (f.AgeMax is int max)
        {
            var earliestBirth = today.AddYears(-(max + 1)).AddDays(1);
            q = q.Where(p => p.User!.DateOfBirth >= earliestBirth);
        }

        if (!string.IsNullOrWhiteSpace(f.Country))
        {
            var country = f.Country.Trim().ToLower();
            q = q.Where(p => p.Country.ToLower() == country);
        }

        if (!string.IsNullOrWhiteSpace(f.City))
        {
            var city = f.City.Trim().ToLower();
            q = q.Where(p => p.City.ToLower() == city);
        }

        if (f.MaritalStatus is { } ms)
        {
            q = q.Where(p => p.MaritalStatus == ms);
        }

        if (f.Sect is { } sect)
        {
            q = q.Where(p => p.Sect == sect);
        }

        if (f.Practice is { } practice)
        {
            q = q.Where(p => p.ReligiousPractice == practice);
        }

        if (f.WillingToRelocate == true)
        {
            q = q.Where(p => p.WillingToRelocate);
        }

        if (f.WithPhoto == true)
        {
            q = q.Where(p => p.Photos.Any(ph => ph.Status == PhotoStatus.Approved));
        }

        q = f.Sort switch
        {
            "newest" => q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.UserId),
            _ => q.OrderByDescending(p => p.User!.LastActiveAt).ThenBy(p => p.UserId),
        };

        var total = await q.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(f.Page, 1, pages);
        var items = await ToCards(viewer.Id, q.Skip((page - 1) * PageSize).Take(PageSize));
        return new PagedCards(items, page, pages, total);
    }

    private async Task<List<ProfileCard>> ToCards(string viewerId, IQueryable<Profile> q)
    {
        var matchIds = MatchIds(viewerId);
        var rows = await q.Select(p => new
        {
            p.UserId,
            p.User!.FirstName,
            p.User.DateOfBirth,
            p.City,
            p.Country,
            p.MaritalStatus,
            p.ReligiousPractice,
            p.Profession,
            p.User.LastActiveAt,
            PhotoAllowed = p.PhotoVisibility == PhotoVisibility.AllMembers || matchIds.Contains(p.UserId),
            PhotoId = p.Photos.Where(ph => ph.Status == PhotoStatus.Approved)
                .OrderByDescending(ph => ph.IsPrimary).ThenBy(ph => ph.CreatedAt)
                .Select(ph => (Guid?)ph.Id).FirstOrDefault(),
        }).ToListAsync();

        var today = Age.Today;
        return rows.Select(r => new ProfileCard(r.UserId, r.FirstName, Age.On(r.DateOfBirth, today), r.City, r.Country,
            r.MaritalStatus, r.ReligiousPractice, r.Profession, r.PhotoAllowed ? r.PhotoId : null, r.LastActiveAt)).ToList();
    }

    public async Task<InterestOutcome> ExpressInterestAsync(ApplicationUser viewer, string targetId)
    {
        if (viewer.Profile is null)
        {
            return InterestOutcome.NeedsProfile;
        }

        if (!await Discoverable(viewer).AnyAsync(p => p.UserId == targetId))
        {
            return InterestOutcome.NotAvailable;
        }

        if (await db.Interests.AnyAsync(i => i.FromUserId == viewer.Id && i.ToUserId == targetId))
        {
            return InterestOutcome.AlreadySent;
        }

        db.Interests.Add(new Interest { FromUserId = viewer.Id, ToUserId = targetId, CreatedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return InterestOutcome.AlreadySent; // a concurrent duplicate hit the unique index
        }

        if (!await IsMatchAsync(viewer.Id, targetId))
        {
            return InterestOutcome.Sent;
        }

        // Notify both without including any profile details in the email.
        var emails = await db.Users.Where(u => u.Id == viewer.Id || u.Id == targetId).Select(u => u.Email).ToListAsync();
        foreach (var to in emails.OfType<string>())
        {
            try
            {
                await email.SendEmailAsync(to, "You have a new match on Nikah Noor",
                    "<p>Assalamu alaikum,</p><p>You have a new mutual match. Sign in to Nikah Noor to view the profile and start a conversation.</p>");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Could not send match notification");
            }
        }

        return InterestOutcome.Matched;
    }

    public async Task WithdrawInterestAsync(string userId, string targetId) =>
        await db.Interests.Where(i => i.FromUserId == userId && i.ToUserId == targetId).ExecuteDeleteAsync();

    public async Task<bool> HasSentInterestAsync(string from, string to) =>
        await db.Interests.AnyAsync(i => i.FromUserId == from && i.ToUserId == to);

    public async Task<List<ProfileCard>> MatchesAsync(ApplicationUser viewer)
    {
        var ids = MatchIds(viewer.Id);
        return await ToCards(viewer.Id, Eligible(viewer).Where(p => ids.Contains(p.UserId)).OrderBy(p => p.User!.FirstName));
    }

    /// <summary>Members who are interested in the viewer and haven't been answered yet.</summary>
    public async Task<List<ProfileCard>> ReceivedAsync(ApplicationUser viewer)
    {
        var from = db.Interests.Where(i => i.ToUserId == viewer.Id && !db.Interests.Any(b => b.FromUserId == viewer.Id && b.ToUserId == i.FromUserId))
            .Select(i => i.FromUserId);
        return await ToCards(viewer.Id, Discoverable(viewer).Where(p => from.Contains(p.UserId)).OrderByDescending(p => p.User!.LastActiveAt));
    }

    /// <summary>Interests the viewer sent that haven't been returned.</summary>
    public async Task<List<ProfileCard>> SentAsync(ApplicationUser viewer)
    {
        var to = db.Interests.Where(i => i.FromUserId == viewer.Id && !db.Interests.Any(b => b.FromUserId == i.ToUserId && b.ToUserId == viewer.Id))
            .Select(i => i.ToUserId);
        return await ToCards(viewer.Id, Eligible(viewer).Where(p => to.Contains(p.UserId)).OrderByDescending(p => p.User!.LastActiveAt));
    }

    public async Task<int> PendingReceivedCountAsync(ApplicationUser viewer)
    {
        var from = db.Interests.Where(i => i.ToUserId == viewer.Id && !db.Interests.Any(b => b.FromUserId == viewer.Id && b.ToUserId == i.FromUserId))
            .Select(i => i.FromUserId);
        return await Discoverable(viewer).CountAsync(p => from.Contains(p.UserId));
    }
}
