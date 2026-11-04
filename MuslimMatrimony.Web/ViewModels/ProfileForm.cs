using MuslimMatrimony.Services;
using System.ComponentModel.DataAnnotations;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.ViewModels;

public sealed class ProfileForm : IValidatableObject
{
    public MaritalStatus MaritalStatus { get; set; }
    public Sect Sect { get; set; }
    public ReligiousPractice ReligiousPractice { get; set; }
    public PrayerFrequency PrayerFrequency { get; set; }

    [Range(120, 230, ErrorMessage = "Height must be between 120 and 230 cm.")]
    public int? HeightCm { get; set; }

    [StringLength(120, ErrorMessage = "Use at most 120 characters.")] public string? Education { get; set; }
    [StringLength(120, ErrorMessage = "Use at most 120 characters.")] public string? Profession { get; set; }
    [StringLength(80, ErrorMessage = "Use at most 80 characters.")] public string? Ethnicity { get; set; }
    [StringLength(200, ErrorMessage = "Use at most 200 characters.")] public string? Languages { get; set; }

    [Required(ErrorMessage = "Please enter your city.")]
    [StringLength(100, ErrorMessage = "Use at most 100 characters.")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your country.")]
    [StringLength(100, ErrorMessage = "Use at most 100 characters.")]
    public string Country { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Use at most 2000 characters.")] public string? AboutMe { get; set; }
    [StringLength(2000, ErrorMessage = "Use at most 2000 characters.")] public string? LookingFor { get; set; }

    public bool HasChildren { get; set; }
    public ChildrenWish WantsChildren { get; set; }
    public bool WillingToRelocate { get; set; }

    [StringLength(100, ErrorMessage = "Use at most 100 characters.")] public string? WaliName { get; set; }
    [StringLength(254, ErrorMessage = "Use at most 254 characters.")] public string? WaliContact { get; set; }

    [Range(18, 99, ErrorMessage = "Ages must be between 18 and 99.")] public int PreferredAgeMin { get; set; } = 18;
    [Range(18, 99, ErrorMessage = "Ages must be between 18 and 99.")] public int PreferredAgeMax { get; set; } = 99;

    public PhotoVisibility PhotoVisibility { get; set; } = PhotoVisibility.MatchesOnly;
    public bool IsHidden { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PreferredAgeMax < PreferredAgeMin)
        {
            yield return new ValidationResult("The maximum age must not be lower than the minimum age.", [nameof(PreferredAgeMax)]);
        }

        foreach (var (value, name) in new[] { (MaritalStatus as Enum, nameof(MaritalStatus)), (Sect, nameof(Sect)), (ReligiousPractice, nameof(ReligiousPractice)),
                     (PrayerFrequency, nameof(PrayerFrequency)), (WantsChildren, nameof(WantsChildren)), (PhotoVisibility, nameof(PhotoVisibility)) })
        {
            if (!Enum.IsDefined(value.GetType(), value))
            {
                yield return new ValidationResult("Please choose a valid option.", [name]);
            }
        }
    }

    public static ProfileForm From(Profile p) => new()
    {
        MaritalStatus = p.MaritalStatus, Sect = p.Sect, ReligiousPractice = p.ReligiousPractice, PrayerFrequency = p.PrayerFrequency,
        HeightCm = p.HeightCm, Education = p.Education, Profession = p.Profession, Ethnicity = p.Ethnicity, Languages = p.Languages,
        City = p.City, Country = p.Country, AboutMe = p.AboutMe, LookingFor = p.LookingFor, HasChildren = p.HasChildren,
        WantsChildren = p.WantsChildren, WillingToRelocate = p.WillingToRelocate, WaliName = p.WaliName, WaliContact = p.WaliContact,
        PreferredAgeMin = p.PreferredAgeMin, PreferredAgeMax = p.PreferredAgeMax, PhotoVisibility = p.PhotoVisibility, IsHidden = p.IsHidden,
    };

    public void ApplyTo(Profile p)
    {
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        p.MaritalStatus = MaritalStatus; p.Sect = Sect; p.ReligiousPractice = ReligiousPractice; p.PrayerFrequency = PrayerFrequency;
        p.HeightCm = HeightCm; p.Education = Clean(Education); p.Profession = Clean(Profession); p.Ethnicity = Clean(Ethnicity);
        p.Languages = Clean(Languages); p.City = City.Trim(); p.Country = Country.Trim(); p.AboutMe = Clean(AboutMe);
        p.LookingFor = Clean(LookingFor); p.HasChildren = HasChildren; p.WantsChildren = WantsChildren; p.WillingToRelocate = WillingToRelocate;
        p.WaliName = Clean(WaliName); p.WaliContact = Clean(WaliContact); p.PreferredAgeMin = PreferredAgeMin; p.PreferredAgeMax = PreferredAgeMax;
        p.PhotoVisibility = PhotoVisibility; p.IsHidden = IsHidden;
    }
}

public sealed record ProfilePage(Profile Profile, int Age, bool IsMatch, bool InterestSent, bool InterestReceived, IReadOnlyList<Guid> PhotoIds);

public sealed record SearchPage(SearchFilter Filter, PagedCards Results);

public sealed record InterestsPage(string Tab, IReadOnlyList<ProfileCard> Items, int ReceivedCount);

public sealed record ThreadPage(string OtherUserId, string OtherFirstName, bool CanSend, IReadOnlyList<ChatMessage> Messages);

public sealed record AdminPage(AdminStats Stats, IReadOnlyList<PendingPhoto> Photos, IReadOnlyList<OpenReport> Reports, IReadOnlyList<SuspendedMember> Suspended);

public sealed class ReportForm
{
    [Required] public string UserId { get; set; } = string.Empty;
    public ReportReason Reason { get; set; }
    [StringLength(2000, ErrorMessage = "Use at most 2000 characters.")] public string? Details { get; set; }
    public bool AlsoBlock { get; set; } = true;
}
