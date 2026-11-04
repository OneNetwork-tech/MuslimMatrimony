using MuslimMatrimony.Models;

namespace MuslimMatrimony;

/// <summary>Marker for the shared string table (Resources/SharedResource.sv.resx). English text is the key.</summary>
public sealed class SharedResource
{
}

/// <summary>English labels for enum values; views pass them through the localizer.</summary>
public static class Labels
{
    public static string For(Enum value) => value switch
    {
        Gender.Male => "Male",
        Gender.Female => "Female",
        MaritalStatus.NeverMarried => "Never married",
        MaritalStatus.Divorced => "Divorced",
        MaritalStatus.Widowed => "Widowed",
        MaritalStatus.Annulled => "Annulled",
        Sect.Sunni => "Sunni",
        Sect.Shia => "Shia",
        Sect.JustMuslim => "Just Muslim",
        Sect.Other => "Other",
        Sect.PreferNotToSay => "Prefer not to say",
        ReligiousPractice.VeryPracticing => "Very practising",
        ReligiousPractice.Practicing => "Practising",
        ReligiousPractice.ModeratelyPracticing => "Moderately practising",
        ReligiousPractice.NotPracticing => "Not practising",
        PrayerFrequency.FiveTimesDaily => "Five times daily",
        PrayerFrequency.Usually => "Usually",
        PrayerFrequency.Sometimes => "Sometimes",
        PrayerFrequency.Rarely => "Rarely",
        PrayerFrequency.PreferNotToSay => "Prefer not to say",
        ChildrenWish.Yes => "Yes",
        ChildrenWish.No => "No",
        ChildrenWish.OpenToIt => "Open to it",
        PhotoVisibility.MatchesOnly => "Only my matches",
        PhotoVisibility.AllMembers => "All signed-in members",
        PhotoStatus.Pending => "Awaiting review",
        PhotoStatus.Approved => "Approved",
        PhotoStatus.Rejected => "Rejected",
        ReportReason.FakeProfile => "Fake profile",
        ReportReason.InappropriatePhotos => "Inappropriate photos",
        ReportReason.Harassment => "Harassment",
        ReportReason.Scam => "Scam or asking for money",
        ReportReason.Underage => "Under 18",
        ReportReason.Other => "Other",
        _ => value.ToString(),
    };

    public static IEnumerable<Enum> Options<T>() where T : struct, Enum => Enum.GetValues<T>().Cast<Enum>();
}
