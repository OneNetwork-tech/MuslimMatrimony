using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

public sealed record BlockedMember(string UserId, string FirstName, DateTime BlockedAt);

public sealed class SafetyService(ApplicationDbContext db)
{
    public async Task<bool> BlockAsync(string blockerId, string blockedId)
    {
        if (blockerId == blockedId || !await db.Users.AnyAsync(u => u.Id == blockedId))
        {
            return false;
        }

        if (!await db.Blocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId))
        {
            db.Blocks.Add(new Block { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = DateTime.UtcNow });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // already blocked by a concurrent request
            }
        }

        return true;
    }

    public Task UnblockAsync(string blockerId, string blockedId) =>
        db.Blocks.Where(b => b.BlockerId == blockerId && b.BlockedId == blockedId).ExecuteDeleteAsync();

    public Task<List<BlockedMember>> BlockedAsync(string blockerId) =>
        db.Blocks.Where(b => b.BlockerId == blockerId).OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlockedMember(b.BlockedId, b.Blocked!.FirstName, b.CreatedAt)).ToListAsync();

    /// <summary>Files a report. One open report per reporter and member; returns false for unknown members or self-reports.</summary>
    public async Task<bool> ReportAsync(string reporterId, string reportedId, ReportReason reason, string? details)
    {
        if (reporterId == reportedId || !await db.Users.AnyAsync(u => u.Id == reportedId))
        {
            return false;
        }

        var text = (details ?? string.Empty).Trim();
        if (text.Length > 2000)
        {
            text = text[..2000];
        }

        var open = await db.Reports.FirstOrDefaultAsync(r => r.ReporterId == reporterId && r.ReportedUserId == reportedId && r.Status == ReportStatus.Open);
        if (open is not null)
        {
            open.Reason = reason;
            open.Details = text;
        }
        else
        {
            db.Reports.Add(new Report { ReporterId = reporterId, ReportedUserId = reportedId, Reason = reason, Details = text, Status = ReportStatus.Open, CreatedAt = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
        return true;
    }
}

public sealed record AdminStats(int Members, int Profiles, int PendingPhotos, int OpenReports, int Suspended);

public sealed record PendingPhoto(Guid Id, string UserId, string FirstName, DateTime CreatedAt);

public sealed record OpenReport(long Id, string ReporterId, string ReporterName, string ReportedUserId, string ReportedName, ReportReason Reason, string Details, DateTime CreatedAt, int ReportsAgainst);

public sealed record SuspendedMember(string UserId, string FirstName, string? Email, string Reason);

public sealed class AdminService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public const string Role = "Admin";

    public async Task<AdminStats> StatsAsync() => new(
        await db.Users.CountAsync(),
        await db.Profiles.CountAsync(),
        await db.Photos.CountAsync(p => p.Status == PhotoStatus.Pending),
        await db.Reports.CountAsync(r => r.Status == ReportStatus.Open),
        await db.Users.CountAsync(u => u.SuspensionReason != null));

    public Task<List<PendingPhoto>> PendingPhotosAsync() =>
        db.Photos.Where(p => p.Status == PhotoStatus.Pending).OrderBy(p => p.CreatedAt).Take(100)
            .Select(p => new PendingPhoto(p.Id, p.UserId, p.Profile!.User!.FirstName, p.CreatedAt)).ToListAsync();

    public Task<List<OpenReport>> OpenReportsAsync() =>
        db.Reports.Where(r => r.Status == ReportStatus.Open).OrderBy(r => r.CreatedAt).Take(100)
            .Select(r => new OpenReport(r.Id, r.ReporterId, r.Reporter!.FirstName, r.ReportedUserId, r.ReportedUser!.FirstName, r.Reason, r.Details, r.CreatedAt,
                db.Reports.Count(x => x.ReportedUserId == r.ReportedUserId)))
            .ToListAsync();

    public Task<List<SuspendedMember>> SuspendedAsync() =>
        db.Users.Where(u => u.SuspensionReason != null).OrderBy(u => u.FirstName)
            .Select(u => new SuspendedMember(u.Id, u.FirstName, u.Email, u.SuspensionReason!)).ToListAsync();

    public async Task<bool> DismissReportAsync(long reportId, string? note)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.Open);
        if (report is null)
        {
            return false;
        }

        report.Status = ReportStatus.Dismissed;
        report.ResolvedAt = DateTime.UtcNow;
        report.ResolutionNote = Trim(note, 1000);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Suspends a member: they can no longer sign in, existing sessions end within a minute, and they disappear from search.</summary>
    public async Task<bool> SuspendAsync(string adminId, string userId, string? reason)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || userId == adminId || await users.IsInRoleAsync(user, Role))
        {
            return false;
        }

        user.SuspensionReason = Trim(reason, 500) ?? "Suspended by moderator";
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);

        var now = DateTime.UtcNow;
        await db.Reports.Where(r => r.ReportedUserId == userId && r.Status == ReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReportStatus.ActionTaken).SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.ResolutionNote, "Member suspended"));
        return true;
    }

    public async Task<bool> UnsuspendAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.SuspensionReason is null)
        {
            return false;
        }

        user.SuspensionReason = null;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await users.UpdateAsync(user);
        return true;
    }

    private static string? Trim(string? s, int max)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
    }
}
