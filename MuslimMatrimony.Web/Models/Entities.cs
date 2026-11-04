using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MuslimMatrimony.Models;

public enum Gender
{
    Male,
    Female,
}

public enum MaritalStatus
{
    NeverMarried,
    Divorced,
    Widowed,
    Annulled,
}

public enum Sect
{
    Sunni,
    Shia,
    JustMuslim,
    Other,
    PreferNotToSay,
}

public enum ReligiousPractice
{
    VeryPracticing,
    Practicing,
    ModeratelyPracticing,
    NotPracticing,
}

public enum PrayerFrequency
{
    FiveTimesDaily,
    Usually,
    Sometimes,
    Rarely,
    PreferNotToSay,
}

public enum ChildrenWish
{
    Yes,
    No,
    OpenToIt,
}

/// <summary>Who may see a member's approved photos.</summary>
public enum PhotoVisibility
{
    /// <summary>Only mutual matches (default).</summary>
    MatchesOnly,
    AllMembers,
}

/// <summary>Login account. Properties marked [PersonalData] are included in GDPR exports.</summary>
public class ApplicationUser : IdentityUser
{
    [PersonalData, MaxLength(60)]
    public string FirstName { get; set; } = string.Empty;

    [PersonalData]
    public Gender Gender { get; set; }

    [PersonalData]
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Explicit consent to processing special-category data (GDPR Art. 9(2)(a)).</summary>
    [PersonalData]
    public DateTime ConsentGivenAt { get; set; }

    [PersonalData, MaxLength(20)]
    public string ConsentVersion { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }

    [MaxLength(500)]
    public string? SuspensionReason { get; set; }

    public Profile? Profile { get; set; }
}

/// <summary>The matrimonial profile. Contains special-category data (religion).</summary>
public class Profile
{
    [Key]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public MaritalStatus MaritalStatus { get; set; }
    public Sect Sect { get; set; }
    public ReligiousPractice ReligiousPractice { get; set; }
    public PrayerFrequency PrayerFrequency { get; set; }
    public int? HeightCm { get; set; }

    [MaxLength(120)] public string? Education { get; set; }
    [MaxLength(120)] public string? Profession { get; set; }
    [MaxLength(80)] public string? Ethnicity { get; set; }
    [MaxLength(200)] public string? Languages { get; set; }
    [MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(100)] public string Country { get; set; } = string.Empty;
    [MaxLength(2000)] public string? AboutMe { get; set; }
    [MaxLength(2000)] public string? LookingFor { get; set; }

    public bool HasChildren { get; set; }
    public ChildrenWish WantsChildren { get; set; }
    public bool WillingToRelocate { get; set; }

    /// <summary>Optional guardian (wali) contact, shown only to mutual matches.</summary>
    [MaxLength(100)] public string? WaliName { get; set; }
    [MaxLength(254)] public string? WaliContact { get; set; }

    public int PreferredAgeMin { get; set; } = 18;
    public int PreferredAgeMax { get; set; } = 99;

    public PhotoVisibility PhotoVisibility { get; set; } = PhotoVisibility.MatchesOnly;

    /// <summary>Paused profiles are hidden from search and new interests.</summary>
    public bool IsHidden { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Photo> Photos { get; } = new();
}

public enum PhotoStatus
{
    Pending,
    Approved,
    Rejected,
}

public class Photo
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Profile? Profile { get; set; }

    /// <summary>Server-generated random file name.</summary>
    [MaxLength(80)] public string FileName { get; set; } = string.Empty;
    [MaxLength(40)] public string ContentType { get; set; } = string.Empty;
    public PhotoStatus Status { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>"I'm interested". Two interests in opposite directions make a match.</summary>
public class Interest
{
    public long Id { get; set; }
    public string FromUserId { get; set; } = string.Empty;
    public ApplicationUser? FromUser { get; set; }
    public string ToUserId { get; set; } = string.Empty;
    public ApplicationUser? ToUser { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Message
{
    public long Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public ApplicationUser? Sender { get; set; }
    public string RecipientId { get; set; } = string.Empty;
    public ApplicationUser? Recipient { get; set; }
    [MaxLength(2000)] public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class Block
{
    public string BlockerId { get; set; } = string.Empty;
    public ApplicationUser? Blocker { get; set; }
    public string BlockedId { get; set; } = string.Empty;
    public ApplicationUser? Blocked { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ReportReason
{
    FakeProfile,
    InappropriatePhotos,
    Harassment,
    Scam,
    Underage,
    Other,
}

public enum ReportStatus
{
    Open,
    ActionTaken,
    Dismissed,
}

public class Report
{
    public long Id { get; set; }
    public string ReporterId { get; set; } = string.Empty;
    public ApplicationUser? Reporter { get; set; }
    public string ReportedUserId { get; set; } = string.Empty;
    public ApplicationUser? ReportedUser { get; set; }
    public ReportReason Reason { get; set; }
    [MaxLength(2000)] public string Details { get; set; } = string.Empty;
    public ReportStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    [MaxLength(1000)] public string? ResolutionNote { get; set; }
}
