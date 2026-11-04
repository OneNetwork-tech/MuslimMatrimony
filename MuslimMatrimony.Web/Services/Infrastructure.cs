using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MuslimMatrimony.Data;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Services;

public static class Cultures
{
    public static readonly string[] Supported = ["en", "sv"];
    public const string Default = "en";
}

/// <summary>Version of the terms/consent text the member agreed to; bump when the text changes.</summary>
public static class Consent
{
    public const string CurrentVersion = "2026-11";
}

public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = ContentSecurityPolicy;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        await next();
    });
}

/// <summary>Records when a member was last active, at most every five minutes per member.</summary>
public sealed class LastActiveFilter(ApplicationDbContext db, IMemoryCache cache) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var id = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is not null && !cache.TryGetValue("active:" + id, out _))
        {
            cache.Set("active:" + id, true, TimeSpan.FromMinutes(5));
            var now = DateTime.UtcNow;
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, now));
        }

        await next();
    }
}

public static class AdminSeeder
{
    /// <summary>Creates the Admin role and grants it to confirmed accounts listed in Admin:Emails.</summary>
    public static async Task EnsureAdminsAsync(IServiceProvider services, IConfiguration config)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AdminService.Role))
        {
            await roles.CreateAsync(new IdentityRole(AdminService.Role));
        }

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var email in config.GetSection("Admin:Emails").Get<string[]>() ?? [])
        {
            var user = await users.FindByEmailAsync(email);
            if (user is { EmailConfirmed: true } && !await users.IsInRoleAsync(user, AdminService.Role))
            {
                await users.AddToRoleAsync(user, AdminService.Role);
            }
        }
    }
}
