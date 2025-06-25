using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuslimMatrimony.Data; // You'll need to add your project's data namespace
using System.Globalization;

// This is the main entry point for your web application.

var builder = WebApplication.CreateBuilder(args);

// --- START: Add Database and Identity Services ---
// This is the section that fixes the new error.

// 1. Get the database connection string from appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// 2. Add the DbContext to the services collection.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString)); // Or UseSqlite, etc.

// 3. Add ASP.NET Core Identity services
builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
// --- END: Add Database and Identity Services ---


// --- START: Add Localization Services ---
// 1. Add services for localization.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// 2. Configure the localization options.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en-US"), // English (United States) - Default
        new CultureInfo("sv")     // Swedish
    };

    // Set the default culture
    options.DefaultRequestCulture = new RequestCulture("en-US");

    // Add the supported cultures
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
// --- END: Add Localization Services ---


// Add other services to the container
builder.Services.AddControllersWithViews()
    .AddViewLocalization(Microsoft.AspNetCore.Mvc.Razor.LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// --- START: Add Localization Middleware ---
var localizationOptions = app.Services.GetService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);
// --- END: Add Localization Middleware ---

app.UseRouting();

// --- START: Add Authentication Middleware ---
// These are required for login/logout to function correctly.
app.UseAuthentication();
app.UseAuthorization();
// --- END: Add Authentication Middleware ---

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// This is required for the Identity UI pages (Login, Register, etc.)
app.MapRazorPages();

app.Run();