using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MuslimMatrimony.Models; // Make sure this namespace matches your project structure

namespace MuslimMatrimony.Data
{
    /// <summary>
    /// This class represents the database context for the application.
    /// It is responsible for managing the connection to the database and for
    /// mapping your C# model classes to database tables.
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // --- Add your custom DbSets here ---

        /// <summary>
        /// This DbSet represents the collection of all UserProfile objects in the context.
        /// Entity Framework Core will use this to create and manage the 'UserProfiles' table
        /// in the database. Each row in this table will correspond to a UserProfile object.
        /// </summary>
        public DbSet<UserProfile> UserProfiles { get; set; }

    }
}
