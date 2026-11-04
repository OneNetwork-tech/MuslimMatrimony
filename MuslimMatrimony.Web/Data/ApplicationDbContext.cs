using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Models;

namespace MuslimMatrimony.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Interest> Interests => Set<Interest>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity's tables get explicit names, so the snake_case convention doesn't reach them.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");

        // Deleting an account removes everything that belongs to it (GDPR right to erasure).
        builder.Entity<Profile>(e =>
        {
            e.HasOne(p => p.User).WithOne(u => u.Profile).HasForeignKey<Profile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.Country, p.City });
            e.ToTable(t => t.HasCheckConstraint("ck_profiles_age_range", "preferred_age_min >= 18 AND preferred_age_max >= preferred_age_min"));
        });

        builder.Entity<Photo>(e =>
        {
            e.HasOne(p => p.Profile).WithMany(p => p.Photos).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.FileName).IsUnique();
            e.HasIndex(p => p.Status);
        });

        builder.Entity<Interest>(e =>
        {
            e.HasOne(i => i.FromUser).WithMany().HasForeignKey(i => i.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.ToUser).WithMany().HasForeignKey(i => i.ToUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => new { i.FromUserId, i.ToUserId }).IsUnique();
            e.HasIndex(i => i.ToUserId);
            e.ToTable(t => t.HasCheckConstraint("ck_interests_not_self", "from_user_id <> to_user_id"));
        });

        builder.Entity<Message>(e =>
        {
            e.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Recipient).WithMany().HasForeignKey(m => m.RecipientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.SenderId, m.RecipientId, m.SentAt });
            e.HasIndex(m => new { m.RecipientId, m.ReadAt });
        });

        builder.Entity<Block>(e =>
        {
            e.HasKey(b => new { b.BlockerId, b.BlockedId });
            e.HasOne(b => b.Blocker).WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Blocked).WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => b.BlockedId);
        });

        builder.Entity<Report>(e =>
        {
            e.HasOne(r => r.Reporter).WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.ReportedUser).WithMany().HasForeignKey(r => r.ReportedUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.Status);
        });
    }
}
