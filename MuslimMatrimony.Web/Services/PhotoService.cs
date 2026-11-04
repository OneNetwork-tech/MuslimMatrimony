using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

public sealed class StorageOptions
{
    /// <summary>Outside wwwroot: photos are only served through an authorised endpoint.</summary>
    public string PhotoDirectory { get; set; } = "App_Data/photos";
    public int MaxPhotosPerMember { get; set; } = 6;
    public long MaxPhotoBytes { get; set; } = 5 * 1024 * 1024;
}

public enum UploadOutcome
{
    Uploaded,
    NeedsProfile,
    TooMany,
    TooLarge,
    InvalidImage,
}

public sealed class PhotoService(ApplicationDbContext db, IOptions<StorageOptions> options, IWebHostEnvironment env, MatchService matches)
{
    private StorageOptions O => options.Value;

    public string Directory
    {
        get
        {
            var dir = Path.IsPathRooted(O.PhotoDirectory) ? O.PhotoDirectory : Path.Combine(env.ContentRootPath, O.PhotoDirectory);
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private string PathFor(Photo p) => Path.Combine(Directory, p.FileName);

    public Task<List<Photo>> ForOwnerAsync(string userId) =>
        db.Photos.Where(p => p.UserId == userId).OrderByDescending(p => p.IsPrimary).ThenBy(p => p.CreatedAt).ToListAsync();

    public async Task<UploadOutcome> UploadAsync(string userId, IFormFile? file)
    {
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId))
        {
            return UploadOutcome.NeedsProfile;
        }

        if (file is null || file.Length == 0)
        {
            return UploadOutcome.InvalidImage;
        }

        if (file.Length > O.MaxPhotoBytes)
        {
            return UploadOutcome.TooLarge;
        }

        if (await db.Photos.CountAsync(p => p.UserId == userId && p.Status != PhotoStatus.Rejected) >= O.MaxPhotosPerMember)
        {
            return UploadOutcome.TooMany;
        }

        using var buffer = new MemoryStream();
        await using (var s = file.OpenReadStream())
        {
            await s.CopyToAsync(buffer);
        }

        var clean = ImageSanitizer.Sanitize(buffer.ToArray());
        if (clean is null)
        {
            return UploadOutcome.InvalidImage;
        }

        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = Guid.NewGuid().ToString("N") + clean.Extension,
            ContentType = clean.ContentType,
            Status = PhotoStatus.Pending,
            IsPrimary = !await db.Photos.AnyAsync(p => p.UserId == userId),
            CreatedAt = DateTime.UtcNow,
        };
        await File.WriteAllBytesAsync(PathFor(photo), clean.Data);
        db.Photos.Add(photo);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            File.Delete(PathFor(photo));
            throw;
        }

        return UploadOutcome.Uploaded;
    }

    public async Task<bool> DeleteAsync(string userId, Guid photoId)
    {
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId);
        if (photo is null)
        {
            return false;
        }

        db.Photos.Remove(photo);
        if (photo.IsPrimary)
        {
            var next = await db.Photos.Where(p => p.UserId == userId && p.Id != photoId).OrderBy(p => p.CreatedAt).FirstOrDefaultAsync();
            if (next is not null)
            {
                next.IsPrimary = true;
            }
        }

        await db.SaveChangesAsync();
        File.Delete(PathFor(photo));
        return true;
    }

    public async Task<bool> SetPrimaryAsync(string userId, Guid photoId)
    {
        var photos = await db.Photos.Where(p => p.UserId == userId).ToListAsync();
        if (!photos.Any(p => p.Id == photoId))
        {
            return false;
        }

        foreach (var p in photos)
        {
            p.IsPrimary = p.Id == photoId;
        }

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Returns the file if the viewer may see it: the owner and moderators always, others only approved photos under the owner's visibility setting.</summary>
    public async Task<(string Path, string ContentType)?> OpenAsync(ApplicationUser viewer, Guid photoId, bool isModerator)
    {
        var photo = await db.Photos.Include(p => p.Profile).FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo is null || photo.Status == PhotoStatus.Rejected)
        {
            return null;
        }

        var allowed = photo.UserId == viewer.Id || isModerator;
        if (!allowed && photo.Status == PhotoStatus.Approved)
        {
            var profile = await matches.GetViewableProfileAsync(viewer, photo.UserId);
            allowed = profile is not null && MatchService.CanSeePhotos(profile, await matches.IsMatchAsync(viewer.Id, photo.UserId));
        }

        var path = PathFor(photo);
        return allowed && File.Exists(path) ? (path, photo.ContentType) : null;
    }

    public async Task<bool> ModerateAsync(Guid photoId, bool approve)
    {
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.Status == PhotoStatus.Pending);
        if (photo is null)
        {
            return false;
        }

        photo.Status = approve ? PhotoStatus.Approved : PhotoStatus.Rejected;
        photo.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        if (!approve)
        {
            File.Delete(PathFor(photo)); // rejected images are not kept
        }

        return true;
    }

    public void DeleteFiles(IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames)
        {
            File.Delete(Path.Combine(Directory, Path.GetFileName(name)));
        }
    }
}
