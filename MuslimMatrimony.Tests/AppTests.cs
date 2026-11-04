using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Tests;

[Collection(AppFixtureGroup.Name)]
public class RegistrationTests(TestApp app)
{
    private static Dictionary<string, string> Form(string email, string dob, bool consent = true, bool terms = true) => new()
    {
        ["Input.FirstName"] = "Maryam",
        ["Input.Email"] = email,
        ["Input.Gender"] = "Female",
        ["Input.DateOfBirth"] = dob,
        ["Input.Password"] = TestApp.Password,
        ["Input.ConfirmPassword"] = TestApp.Password,
        ["Input.ConsentSpecialCategory"] = consent ? "true" : "false",
        ["Input.AcceptTerms"] = terms ? "true" : "false",
    };

    private static string Adult => DateTime.UtcNow.AddYears(-25).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Register_confirm_and_login()
    {
        var email = $"maryam-{Guid.NewGuid():N}@test.local";
        var browser = app.Browser();
        var response = await browser.PostFormAsync("/Identity/Account/Register", Form(email, Adult));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("RegisterConfirmation", response.Headers.Location!.ToString(), StringComparison.Ordinal);

        // Can't sign in before confirming.
        var login = new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password };
        var early = await browser.PostFormAsync("/Identity/Account/Login", login);
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);

        var mail = app.Outbox.LastTo(email);
        Assert.NotNull(mail);
        var link = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(mail.HtmlBody, "href='([^']+)'").Groups[1].Value);
        var confirm = await browser.GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var ok = await browser.PostFormAsync("/Identity/Account/Login", login);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        await app.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(Gender.Female, user.Gender);
            Assert.Equal("Maryam", user.FirstName);
            Assert.NotEqual(default, user.ConsentGivenAt);
            Assert.False(string.IsNullOrEmpty(user.ConsentVersion));
        });
    }

    [Fact]
    public async Task Under_18_is_rejected()
    {
        var email = $"young-{Guid.NewGuid():N}@test.local";
        var dob = DateTime.UtcNow.AddYears(-17).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var response = await app.Browser().PostFormAsync("/Identity/Account/Register", Form(email, dob));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("at least 18", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await app.WithDb(async db => Assert.False(await db.Users.AnyAsync(u => u.Email == email)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Consent_and_terms_are_required(bool consent, bool terms)
    {
        var email = $"noconsent-{Guid.NewGuid():N}@test.local";
        var response = await app.Browser().PostFormAsync("/Identity/Account/Register", Form(email, Adult, consent, terms));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.WithDb(async db => Assert.False(await db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Registering_an_existing_email_looks_the_same_as_a_new_one()
    {
        var existing = await app.CreateMemberAsync(Gender.Male);
        var response = await app.Browser().PostFormAsync("/Identity/Account/Register", Form(existing.Email, Adult));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("RegisterConfirmation", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains("tried to register", app.Outbox.LastTo(existing.Email)!.Subject, StringComparison.Ordinal);
    }
}

[Collection(AppFixtureGroup.Name)]
public class MatchmakingTests(TestApp app)
{
    [Fact]
    public async Task Search_shows_only_eligible_opposite_gender_profiles()
    {
        var man = await app.CreateMemberAsync(Gender.Male, profile: p => p.City = "Uppsala-" + Guid.NewGuid().ToString("N")[..6]);
        var city = await app.WithScope(async sp => (await sp.GetRequiredService<Data.ApplicationDbContext>().Profiles.FindAsync(man.Id))!.City);
        var woman = await app.CreateMemberAsync(Gender.Female, profile: p => p.City = city);
        var hidden = await app.CreateMemberAsync(Gender.Female, profile: p => { p.City = city; p.IsHidden = true; });
        var blocker = await app.CreateMemberAsync(Gender.Female, profile: p => p.City = city);
        var otherMan = await app.CreateMemberAsync(Gender.Male, profile: p => p.City = city);
        var noProfile = await app.CreateMemberAsync(Gender.Female, withProfile: false);

        Assert.Equal(HttpStatusCode.Redirect, (await blocker.PostAsync("/Safety/Block", new() { ["userId"] = man.Id })).StatusCode);

        var html = await (await man.GetAsync($"/Search?submitted=true&City={city}")).Content.ReadAsStringAsync();
        Assert.Contains(woman.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(blocker.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(otherMan.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(noProfile.Id, html, StringComparison.Ordinal);

        // Direct profile access follows the same rules.
        Assert.Equal(HttpStatusCode.OK, (await man.GetAsync($"/Profile/View/{woman.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Profile/View/{otherMan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Profile/View/{hidden.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Profile/View/{blocker.Id}")).StatusCode);
    }

    [Fact]
    public async Task Search_requires_a_profile()
    {
        var member = await app.CreateMemberAsync(Gender.Female, withProfile: false);
        var response = await member.GetAsync("/Search");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Profile/Edit", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Age_filter_is_applied()
    {
        var city = "Lund-" + Guid.NewGuid().ToString("N")[..6];
        var man = await app.CreateMemberAsync(Gender.Male, profile: p => p.City = city);
        var young = await app.CreateMemberAsync(Gender.Female, age: 22, profile: p => p.City = city);
        var older = await app.CreateMemberAsync(Gender.Female, age: 40, profile: p => p.City = city);

        var html = await (await man.GetAsync($"/Search?submitted=true&City={city}&AgeMin=20&AgeMax=30")).Content.ReadAsStringAsync();
        Assert.Contains(young.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(older.Id, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mutual_interest_creates_a_match_and_enables_messaging()
    {
        var man = await app.CreateMemberAsync(Gender.Male, profile: p => { p.WaliName = "Abdullah"; p.WaliContact = "wali@test.local"; });
        var woman = await app.CreateMemberAsync(Gender.Female, profile: p => { p.WaliName = "Ibrahim"; p.WaliContact = "+46700000000"; });

        // Not a match yet: messaging is refused and wali details are hidden.
        Assert.Equal(HttpStatusCode.Redirect, (await man.PostAsync("/Interests/Express", new() { ["userId"] = woman.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await man.PostAsync("/Messages/Send", new() { ["userId"] = woman.Id, ["body"] = "Salaam" })).StatusCode);
        Assert.DoesNotContain("Ibrahim", await (await man.GetAsync($"/Profile/View/{woman.Id}")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(man.Id, await (await woman.GetAsync("/Interests")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // She accepts: it's a match.
        Assert.Equal(HttpStatusCode.Redirect, (await woman.PostAsync("/Interests/Express", new() { ["userId"] = man.Id })).StatusCode);
        Assert.Contains("new match", app.Outbox.LastTo(man.Email)!.Subject, StringComparison.Ordinal);
        Assert.Contains(woman.Id, await (await man.GetAsync("/Interests?tab=matches")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Ibrahim", await (await man.GetAsync($"/Profile/View/{woman.Id}")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Redirect, (await man.PostAsync("/Messages/Send", new() { ["userId"] = woman.Id, ["body"] = "Assalamu alaikum <b>sister</b>" })).StatusCode);
        var inbox = await (await woman.GetAsync("/Messages")).Content.ReadAsStringAsync();
        Assert.Contains("Yusuf", inbox, StringComparison.Ordinal);
        var thread = await (await woman.GetAsync($"/Messages/With/{man.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("Assalamu alaikum &lt;b&gt;sister&lt;/b&gt;", thread, StringComparison.Ordinal); // HTML-encoded
        await app.WithDb(async db => Assert.NotNull((await db.Messages.SingleAsync(m => m.SenderId == man.Id)).ReadAt));

        // Withdrawing ends the match; history stays readable but no new messages.
        Assert.Equal(HttpStatusCode.Redirect, (await woman.PostAsync("/Interests/Withdraw", new() { ["userId"] = man.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await woman.PostAsync("/Messages/Send", new() { ["userId"] = man.Id, ["body"] = "Hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await woman.GetAsync($"/Messages/With/{man.Id}")).StatusCode);

        // Blocking hides the conversation entirely.
        await man.PostAsync("/Safety/Block", new() { ["userId"] = woman.Id });
        Assert.Equal(HttpStatusCode.NotFound, (await woman.GetAsync($"/Messages/With/{man.Id}")).StatusCode);
        Assert.DoesNotContain("Yusuf", await (await woman.GetAsync("/Messages")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cannot_message_strangers_or_same_gender()
    {
        var a = await app.CreateMemberAsync(Gender.Male);
        var b = await app.CreateMemberAsync(Gender.Male);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/Interests/Express", new() { ["userId"] = b.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/Messages/Send", new() { ["userId"] = b.Id, ["body"] = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/Messages/With/{b.Id}")).StatusCode);
    }

    [Fact]
    public async Task Profile_can_be_created_and_validated()
    {
        var member = await app.CreateMemberAsync(Gender.Female, withProfile: false);
        var bad = await member.PostAsync("/Profile/Edit", new()
        {
            ["City"] = "Malmö", ["Country"] = "Sweden", ["PreferredAgeMin"] = "40", ["PreferredAgeMax"] = "30",
        }, "/Profile/Edit");
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);

        var good = await member.PostAsync("/Profile/Edit", new()
        {
            ["City"] = "Malmö", ["Country"] = "Sweden", ["MaritalStatus"] = "Divorced", ["Sect"] = "Sunni", ["ReligiousPractice"] = "Practicing",
            ["PrayerFrequency"] = "FiveTimesDaily", ["WantsChildren"] = "OpenToIt", ["PhotoVisibility"] = "AllMembers", ["HeightCm"] = "165",
            ["PreferredAgeMin"] = "28", ["PreferredAgeMax"] = "38", ["AboutMe"] = "Hello", ["WillingToRelocate"] = "true",
        }, "/Profile/Edit");
        Assert.Equal(HttpStatusCode.Redirect, good.StatusCode);
        await app.WithDb(async db =>
        {
            var p = await db.Profiles.SingleAsync(x => x.UserId == member.Id);
            Assert.Equal(MaritalStatus.Divorced, p.MaritalStatus);
            Assert.Equal(PhotoVisibility.AllMembers, p.PhotoVisibility);
            Assert.True(p.WillingToRelocate);
            Assert.Equal(165, p.HeightCm);
        });
    }
}

[Collection(AppFixtureGroup.Name)]
public class PhotoTests(TestApp app)
{
    private static async Task<Guid> UploadAsync(TestApp app, Member member)
    {
        var response = await member.Client.PostFileAsync("/Photos/Upload", TestImages.JpegWithExif(), "me.jpg");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await app.WithScope(async sp => (await sp.GetRequiredService<Data.ApplicationDbContext>().Photos.OrderByDescending(p => p.CreatedAt).FirstAsync(p => p.UserId == member.Id)).Id);
    }

    [Fact]
    public async Task Photos_are_moderated_stripped_and_shown_only_to_allowed_members()
    {
        var woman = await app.CreateMemberAsync(Gender.Female);
        var man = await app.CreateMemberAsync(Gender.Male);
        var admin = await app.CreateAdminAsync();
        var photoId = await UploadAsync(app, woman);

        // Stored without EXIF; owner can see it while pending, others can't.
        var stored = await woman.Client.GetByteArrayAsync($"/Photos/File/{photoId}");
        Assert.False(TestImages.Contains(stored, "GPSLatitude"));
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Photos/File/{photoId}")).StatusCode);

        // Moderator approves.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Photos/File/{photoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/Photo", new() { ["id"] = photoId.ToString(), ["approve"] = "true" }, "/Admin")).StatusCode);

        // Default visibility is matches only.
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Photos/File/{photoId}")).StatusCode);
        await woman.PostAsync("/Interests/Express", new() { ["userId"] = man.Id });
        await man.PostAsync("/Interests/Express", new() { ["userId"] = woman.Id });
        var ok = await man.GetAsync($"/Photos/File/{photoId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/jpeg", ok.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", ok.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task All_members_visibility_and_rejection()
    {
        var woman = await app.CreateMemberAsync(Gender.Female, profile: p => p.PhotoVisibility = PhotoVisibility.AllMembers);
        var man = await app.CreateMemberAsync(Gender.Male);
        var otherWoman = await app.CreateMemberAsync(Gender.Female);
        var admin = await app.CreateAdminAsync();
        var approved = await UploadAsync(app, woman);
        var rejected = await UploadAsync(app, woman);
        await admin.PostAsync("/Admin/Photo", new() { ["id"] = approved.ToString(), ["approve"] = "true" }, "/Admin");
        await admin.PostAsync("/Admin/Photo", new() { ["id"] = rejected.ToString(), ["approve"] = "false" }, "/Admin");

        Assert.Equal(HttpStatusCode.OK, (await man.GetAsync($"/Photos/File/{approved}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherWoman.GetAsync($"/Photos/File/{approved}")).StatusCode); // same gender
        Assert.Equal(HttpStatusCode.NotFound, (await man.GetAsync($"/Photos/File/{rejected}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await woman.GetAsync($"/Photos/File/{rejected}")).StatusCode); // file deleted
    }

    [Fact]
    public async Task Non_images_are_refused()
    {
        var woman = await app.CreateMemberAsync(Gender.Female);
        await woman.Client.PostFileAsync("/Photos/Upload", "<script>alert(1)</script>"u8.ToArray(), "evil.jpg");
        await app.WithDb(async db => Assert.False(await db.Photos.AnyAsync(p => p.UserId == woman.Id)));
    }

    [Fact]
    public async Task Anonymous_users_are_sent_to_login()
    {
        var response = await app.Browser().GetAsync($"/Photos/File/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }
}

[Collection(AppFixtureGroup.Name)]
public class SafetyAndAdminTests(TestApp app)
{
    [Fact]
    public async Task Report_and_suspend_blocks_login()
    {
        var reporter = await app.CreateMemberAsync(Gender.Female);
        var offender = await app.CreateMemberAsync(Gender.Male);
        var admin = await app.CreateAdminAsync();

        var report = await reporter.PostAsync("/Safety/Report", new() { ["UserId"] = offender.Id, ["Reason"] = "Harassment", ["Details"] = "Rude messages", ["AlsoBlock"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, report.StatusCode);
        await app.WithDb(async db =>
        {
            Assert.True(await db.Reports.AnyAsync(r => r.ReporterId == reporter.Id && r.ReportedUserId == offender.Id && r.Status == ReportStatus.Open));
            Assert.True(await db.Blocks.AnyAsync(b => b.BlockerId == reporter.Id && b.BlockedId == offender.Id));
        });

        Assert.Contains("Rude messages", await (await admin.GetAsync("/Admin")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/Suspend", new() { ["userId"] = offender.Id, ["reason"] = "Harassment" }, "/Admin")).StatusCode);

        await app.WithDb(async db =>
        {
            Assert.Equal(ReportStatus.ActionTaken, (await db.Reports.SingleAsync(r => r.ReportedUserId == offender.Id)).Status);
            Assert.NotNull((await db.Users.SingleAsync(u => u.Id == offender.Id)).SuspensionReason);
        });

        // Can't sign in again, and no longer visible to others.
        var login = await app.Browser().PostFormAsync("/Identity/Account/Login", new() { ["Input.Email"] = offender.Email, ["Input.Password"] = TestApp.Password });
        Assert.Contains("Lockout", login.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var someone = await app.CreateMemberAsync(Gender.Female);
        Assert.Equal(HttpStatusCode.NotFound, (await someone.GetAsync($"/Profile/View/{offender.Id}")).StatusCode);

        // Lifting the suspension restores access.
        await admin.PostAsync("/Admin/Unsuspend", new() { ["userId"] = offender.Id }, "/Admin");
        await offender.LoginAsync();
    }

    [Fact]
    public async Task Members_cannot_open_moderation()
    {
        var member = await app.CreateMemberAsync(Gender.Male);
        var response = await member.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        var post = await member.PostAsync("/Admin/Suspend", new() { ["userId"] = member.Id });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("AccessDenied", post.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admins_cannot_be_suspended()
    {
        var admin = await app.CreateAdminAsync();
        await admin.PostAsync("/Admin/Suspend", new() { ["userId"] = admin.Id }, "/Admin");
        await app.WithDb(async db => Assert.Null((await db.Users.SingleAsync(u => u.Id == admin.Id)).SuspensionReason));
    }
}

[Collection(AppFixtureGroup.Name)]
public class GdprTests(TestApp app)
{
    [Fact]
    public async Task Export_contains_profile_messages_and_photos()
    {
        var woman = await app.CreateMemberAsync(Gender.Female, profile: p => p.AboutMe = "Export me");
        var man = await app.CreateMemberAsync(Gender.Male);
        await woman.Client.PostFileAsync("/Photos/Upload", TestImages.PngWithText(), "me.png");
        await woman.PostAsync("/Interests/Express", new() { ["userId"] = man.Id });
        await man.PostAsync("/Interests/Express", new() { ["userId"] = woman.Id });
        await man.PostAsync("/Messages/Send", new() { ["userId"] = woman.Id, ["body"] = "Message for export" });

        var response = await woman.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new(), "/Identity/Account/Manage/PersonalData");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);

        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync());
        using var json = JsonDocument.Parse(zip.GetEntry("data.json")!.Open());
        var root = json.RootElement;
        Assert.Equal(woman.Email, root.GetProperty("account").GetProperty("Email").GetString());
        Assert.Equal("Export me", root.GetProperty("profile").GetProperty("AboutMe").GetString());
        Assert.Equal("Message for export", root.GetProperty("messages")[0].GetProperty("Body").GetString());
        Assert.Equal(1, root.GetProperty("interestsSent").GetArrayLength());
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("photos/", StringComparison.Ordinal) && e.FullName.EndsWith(".png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_account_removes_everything()
    {
        var woman = await app.CreateMemberAsync(Gender.Female);
        var man = await app.CreateMemberAsync(Gender.Male);
        await woman.Client.PostFileAsync("/Photos/Upload", TestImages.JpegWithExif(), "me.jpg");
        await woman.PostAsync("/Interests/Express", new() { ["userId"] = man.Id });
        var file = await app.WithScope(async sp => (await sp.GetRequiredService<Data.ApplicationDbContext>().Photos.SingleAsync(p => p.UserId == woman.Id)).FileName);
        Assert.True(File.Exists(Path.Combine(app.PhotoDirectory, file)));

        var wrong = await woman.PostAsync("/Identity/Account/Manage/DeletePersonalData", new() { ["Input.Password"] = "wrong-password" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);

        var ok = await woman.PostAsync("/Identity/Account/Manage/DeletePersonalData", new() { ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        await app.WithDb(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == woman.Id));
            Assert.False(await db.Profiles.AnyAsync(p => p.UserId == woman.Id));
            Assert.False(await db.Photos.AnyAsync(p => p.UserId == woman.Id));
            Assert.False(await db.Interests.AnyAsync(i => i.FromUserId == woman.Id));
        });
        Assert.False(File.Exists(Path.Combine(app.PhotoDirectory, file)));
    }
}

[Collection(AppFixtureGroup.Name)]
public class WebSecurityTests(TestApp app)
{
    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await app.Browser().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", csp, StringComparison.Ordinal);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.DoesNotContain("cdn.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posts_without_antiforgery_token_are_rejected()
    {
        var member = await app.CreateMemberAsync(Gender.Male);
        var response = await member.Client.PostAsync(new Uri("/Safety/Block", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["userId"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Language_switch_is_validated_and_redirects_locally()
    {
        var browser = app.Browser();
        var external = await browser.PostFormAsync("/Home/SetLanguage", new() { ["culture"] = "sv", ["returnUrl"] = "https://evil.example" });
        Assert.Equal("/", external.Headers.Location!.ToString());

        var html = await browser.GetStringAsync("/");
        Assert.Contains("lang=\"sv\"", html, StringComparison.Ordinal);
        Assert.Contains("Hitta din rättfärdiga livspartner", html, StringComparison.Ordinal);

        var bogus = app.Browser();
        await bogus.PostFormAsync("/Home/SetLanguage", new() { ["culture"] = "xx-INVALID", ["returnUrl"] = "/" });
        Assert.Contains("lang=\"en\"", await bogus.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_pages_return_404_and_health_is_ok()
    {
        var browser = app.Browser();
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/does-not-exist")).StatusCode);
        Assert.Equal("ok", await browser.GetStringAsync("/health"));
    }
}
