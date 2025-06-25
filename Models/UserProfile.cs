using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace MuslimMatrimony.Models
{
    // --- Enums for structured data ---
    // Using enums for specific fields makes data entry consistent and easier to translate.

    public enum Gender
    {
        Male,
        Female
    }

    public enum MaritalStatus
    {
        Single,
        Divorced,
        Widowed,
        Annulled
    }

    public enum Sect
    {
        Sunni,
        Shia,
        [Display(Name = "Just Muslim")]
        JustMuslim,
        Other
    }

    public enum ReligiousPractice
    {
        [Display(Name = "Very Practicing")]
        VeryPracticing,
        Practicing,
        [Display(Name = "Moderately Practicing")]
        ModeratelyPracticing,
        [Display(Name = "Not Practicing")]
        NotPracticing
    }


    // --- UserProfile Class ---
    // This class extends the basic user information provided by ASP.NET Core Identity.

    public class UserProfile
    {
        [Key] // Marks this property as the primary key for the database table.
        public int Id { get; set; }

        // --- Foreign Key to link to the main Identity User ---
        // This creates a one-to-one relationship between the login user and their profile data.
        public string ApplicationUserId { get; set; }
        [ForeignKey("ApplicationUserId")]
        public virtual IdentityUser ApplicationUser { get; set; }


        // --- Personal Details ---
        [Required]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime DateOfBirth { get; set; }

        [Required]
        public Gender Gender { get; set; }

        // Storing height in centimeters
        public int? HeightInCm { get; set; }

        public string? ProfilePictureUrl { get; set; }


        // --- Religious & Lifestyle Details ---
        [Required]
        public MaritalStatus MaritalStatus { get; set; }

        [Required]
        public Sect Sect { get; set; }

        [Required]
        public ReligiousPractice ReligiousPractice { get; set; }

        [StringLength(100)]
        public string? Education { get; set; }

        [StringLength(100)]
        public string? Profession { get; set; }


        // --- Location ---
        [Required]
        [StringLength(100)]
        public string City { get; set; }

        [Required]
        [StringLength(100)]
        public string Country { get; set; }


        // --- About Sections ---
        [StringLength(1500, ErrorMessage = "The description cannot be longer than 1500 characters.")]
        public string? AboutMe { get; set; }

        [StringLength(1500, ErrorMessage = "The description cannot be longer than 1500 characters.")]
        public string? LookingFor { get; set; }


        // --- Profile Management ---
        public DateTime ProfileCreationDate { get; set; } = DateTime.UtcNow;
        public bool IsProfileComplete { get; set; } = false;

    }
}
