using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

/// <summary>Right of access (Art. 15/20: a ZIP with all data and photos) and right to erasure (Art. 17).</summary>
public sealed class GdprService(ApplicationDbContext db, UserManager<ApplicationUser> users, PhotoService photos)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<byte[]?> ExportAsync(string userId)
    {
        var user = await db.Users.Include(u => u.Profile).ThenInclude(p => p!.Photos).AsSplitQuery().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return null;
        }

        var names = db.Users.Select(u => new { u.Id, u.FirstName });
        var data = new
        {
            exportedAt = DateTime.UtcNow,
            account = new
            {
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.FirstName,
                Gender = user.Gender.ToString(),
                user.DateOfBirth,
                user.ConsentGivenAt,
                user.ConsentVersion,
                user.CreatedAt,
                user.LastActiveAt,
                user.TwoFactorEnabled,
                user.SuspensionReason,
                Logins = (await users.GetLoginsAsync(user)).Select(l => l.LoginProvider),
            },
            profile = user.Profile is null ? null : new
            {
                MaritalStatus = user.Profile.MaritalStatus.ToString(),
                Sect = user.Profile.Sect.ToString(),
                ReligiousPractice = user.Profile.ReligiousPractice.ToString(),
                PrayerFrequency = user.Profile.PrayerFrequency.ToString(),
                user.Profile.HeightCm,
                user.Profile.Education,
                user.Profile.Profession,
                user.Profile.Ethnicity,
                user.Profile.Languages,
                user.Profile.City,
                user.Profile.Country,
                user.Profile.AboutMe,
                user.Profile.LookingFor,
                user.Profile.HasChildren,
                WantsChildren = user.Profile.WantsChildren.ToString(),
                user.Profile.WillingToRelocate,
                user.Profile.WaliName,
                user.Profile.WaliContact,
                user.Profile.PreferredAgeMin,
                user.Profile.PreferredAgeMax,
                PhotoVisibility = user.Profile.PhotoVisibility.ToString(),
                user.Profile.IsHidden,
                user.Profile.CreatedAt,
                user.Profile.UpdatedAt,
            },
            photos = user.Profile?.Photos.Select(p => new { p.Id, File = "photos/" + p.FileName, Status = p.Status.ToString(), p.IsPrimary, p.CreatedAt }),
            interestsSent = await db.Interests.Where(i => i.FromUserId == userId)
                .Select(i => new { To = names.Where(n => n.Id == i.ToUserId).Select(n => n.FirstName).FirstOrDefault(), i.ToUserId, i.CreatedAt }).ToListAsync(),
            interestsReceived = await db.Interests.Where(i => i.ToUserId == userId)
                .Select(i => new { From = names.Where(n => n.Id == i.FromUserId).Select(n => n.FirstName).FirstOrDefault(), i.FromUserId, i.CreatedAt }).ToListAsync(),
            messages = await db.Messages.Where(m => m.SenderId == userId || m.RecipientId == userId).OrderBy(m => m.SentAt)
                .Select(m => new { Direction = m.SenderId == userId ? "sent" : "received", WithUserId = m.SenderId == userId ? m.RecipientId : m.SenderId, m.Body, m.SentAt, m.ReadAt })
                .ToListAsync(),
            blocks = await db.Blocks.Where(b => b.BlockerId == userId).Select(b => new { b.BlockedId, b.CreatedAt }).ToListAsync(),
            reportsFiled = await db.Reports.Where(r => r.ReporterId == userId)
                .Select(r => new { r.ReportedUserId, Reason = r.Reason.ToString(), r.Details, Status = r.Status.ToString(), r.CreatedAt }).ToListAsync(),
        };

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("data.json");
            await using (var s = entry.Open())
            {
                await JsonSerializer.SerializeAsync(s, data, Json);
            }

            foreach (var p in user.Profile?.Photos ?? [])
            {
                var path = Path.Combine(photos.Directory, p.FileName);
                if (File.Exists(path))
                {
                    archive.CreateEntryFromFile(path, "photos/" + p.FileName, CompressionLevel.NoCompression);
                }
            }
        }

        return zip.ToArray();
    }

    /// <summary>Deletes the account and, through cascades, everything that belongs to it, then the photo files.</summary>
    public async Task<bool> DeleteAccountAsync(ApplicationUser user)
    {
        var files = await db.Photos.Where(p => p.UserId == user.Id).Select(p => p.FileName).ToListAsync();
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return false;
        }

        photos.DeleteFiles(files);
        return true;
    }
}
