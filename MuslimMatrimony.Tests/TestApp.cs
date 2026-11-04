using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;
using Npgsql;

namespace MuslimMatrimony.Tests;

/// <summary>
/// The real app against a real PostgreSQL (TEST_DATABASE_URL). The schema is dropped and
/// recreated once per run, so point it at a dedicated test database.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "correct-horse-battery";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
        ?? "Host=localhost;Database=muslimmatrimony_test;Username=muslimmatrimony;Password=muslimmatrimony";

    public string PhotoDirectory { get; } = Path.Combine(Path.GetTempPath(), "mm-tests-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DROP SCHEMA public CASCADE; CREATE SCHEMA public;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:MuslimMatrimony", ConnectionString);
        builder.UseSetting("Storage:PhotoDirectory", PhotoDirectory);
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Admin:Emails:0", AdminEmail);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(PhotoDirectory))
        {
            Directory.Delete(PhotoDirectory, true);
        }
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    public EmailOutbox Outbox => Services.GetRequiredService<EmailOutbox>();

    public async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task WithDb(Func<ApplicationDbContext, Task> action) => WithScope(async sp =>
    {
        await action(sp.GetRequiredService<ApplicationDbContext>());
        return true;
    });

    /// <summary>Creates a confirmed member directly (fast path for tests that aren't about registration).</summary>
    public async Task<Member> CreateMemberAsync(Gender gender, bool withProfile = true, int age = 28, string? email = null, Action<Profile>? profile = null)
    {
        email ??= $"{gender.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var id = await WithScope(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true, FirstName = gender == Gender.Male ? "Yusuf" : "Aisha", Gender = gender,
                DateOfBirth = Age.Today.AddYears(-age).AddDays(-10), ConsentGivenAt = now, ConsentVersion = Consent.CurrentVersion, CreatedAt = now,
            };
            var result = await users.CreateAsync(user, Password);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            if (withProfile)
            {
                var db = sp.GetRequiredService<ApplicationDbContext>();
                var p = new Profile { UserId = user.Id, City = "Stockholm", Country = "Sweden", CreatedAt = now, UpdatedAt = now, AboutMe = "About " + user.FirstName };
                profile?.Invoke(p);
                db.Profiles.Add(p);
                await db.SaveChangesAsync();
            }

            return user.Id;
        });

        var member = new Member(this, id, email);
        await member.LoginAsync();
        return member;
    }

    private string? _adminId;

    public async Task<Member> CreateAdminAsync()
    {
        if (_adminId is null)
        {
            var created = await CreateMemberAsync(Gender.Male, withProfile: false, email: AdminEmail);
            await WithScope(async sp =>
            {
                await AdminSeeder.EnsureAdminsAsync(sp, sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
                return true;
            });
            _adminId = created.Id;
        }

        var admin = new Member(this, _adminId, AdminEmail);
        await admin.LoginAsync(); // fresh cookie with the role claim
        return admin;
    }
}

public sealed partial class Member(TestApp app, string id, string email)
{
    public string Id { get; } = id;
    public string Email { get; } = email;
    public HttpClient Client { get; private set; } = app.Browser();

    public async Task LoginAsync()
    {
        Client = app.Browser();
        var response = await Client.PostFormAsync("/Identity/Account/Login", new()
        {
            ["Input.Email"] = Email,
            ["Input.Password"] = TestApp.Password,
            ["Input.RememberMe"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url);

    public Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, string> form, string? tokenPage = null) =>
        Client.PostFormAsync(url, form, tokenPage);
}

public static partial class HttpExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    public static async Task<string> AntiforgeryTokenAsync(this HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {page}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Posts a form the way a browser does, with the antiforgery token from <paramref name="tokenPage"/> (default: the form's own URL, else the home page).</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, Dictionary<string, string> form, string? tokenPage = null)
    {
        var page = tokenPage ?? (url.StartsWith("/Identity/", StringComparison.Ordinal) ? url : "/");
        form["__RequestVerificationToken"] = await client.AntiforgeryTokenAsync(page);
        return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(form));
    }

    public static async Task<HttpResponseMessage> PostFileAsync(this HttpClient client, string url, byte[] file, string fileName)
    {
        var token = await client.AntiforgeryTokenAsync("/Photos");
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(file), "file", fileName },
        };
        return await client.PostAsync(new Uri(url, UriKind.Relative), content);
    }
}

[CollectionDefinition(Name)]
public sealed class AppFixtureGroup : ICollectionFixture<TestApp>
{
    public const string Name = "app";
}
