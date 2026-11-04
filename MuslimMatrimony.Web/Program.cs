using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var connectionString = config.GetConnectionString("MuslimMatrimony");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:MuslimMatrimony is not configured (set ConnectionStrings__MuslimMatrimony).");
}

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddDefaultIdentity<ApplicationUser>(o =>
    {
        o.SignIn.RequireConfirmedAccount = true;
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequiredUniqueChars = 4; // length over composition rules (NIST 800-63B)
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Production always runs behind HTTPS; plain-HTTP localhost is allowed in Development only.
var cookieSecurity = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// Suspensions and password changes end other sessions quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "nn.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSecurity;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "nn.af";
    o.Cookie.SecurePolicy = cookieSecurity;
});

builder.Services.Configure<EmailOptions>(config.GetSection("Email"));
builder.Services.Configure<StorageOptions>(config.GetSection("Storage"));
builder.Services.AddSingleton<EmailOutbox>();
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<SafetyService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<GdprService>();
builder.Services.AddScoped<LastActiveFilter>();
builder.Services.AddMemoryCache();

var keysPath = config["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("NikahNoor");
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// Emit Swedish characters as-is instead of &#xE4; entities.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var cultures = Cultures.Supported.Select(c => new CultureInfo(c)).ToList();
    o.DefaultRequestCulture = new RequestCulture(Cultures.Default);
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
});

builder.Services.AddControllersWithViews(o =>
    {
        o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        o.Filters.AddService<LastActiveFilter>();
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(SharedResource)));
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeAreaFolder("Identity", "/Account/Manage");
    o.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
});

var rateLimitingEnabled = config.GetValue("RateLimiting:Enabled", true);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Credential and account-recovery forms: 10 POSTs per minute per client IP.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!rateLimitingEnabled || !HttpMethods.IsPost(ctx.Request.Method) ||
            !ctx.Request.Path.StartsWithSegments("/Identity/Account", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/Identity/Account/Manage", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/Identity/Account/Logout", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("none");
        }

        return RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    // Per member: messages, interests and reports.
    o.AddPolicy("member-actions", ctx => rateLimitingEnabled
        ? RateLimitPartition.GetSlidingWindowLimiter(ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new SlidingWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 })
        : RateLimitPartition.GetNoLimiter("none"));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var ip in config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        o.KnownProxies.Add(IPAddress.Parse(ip));
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (config.GetValue("Database:MigrateOnStartup", true))
    {
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    await AdminSeeder.EnsureAdminsAsync(scope.ServiceProvider, config);
}

if (string.IsNullOrWhiteSpace(config["Email:Host"]) && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("Email:Host is not set: confirmation and password-reset emails will not be delivered.");
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (ApplicationDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Text("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

await app.RunAsync();

public partial class Program
{
}
