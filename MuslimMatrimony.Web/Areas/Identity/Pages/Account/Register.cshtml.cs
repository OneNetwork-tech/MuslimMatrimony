using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using MuslimMatrimony.Models;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel(UserManager<ApplicationUser> users, IEmailSender emailSender, IStringLocalizer<SharedResource> l, ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Please enter your first name.")]
        [StringLength(60, ErrorMessage = "Use at most 60 characters.")]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(254)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please choose your gender.")]
        [Display(Name = "I am")]
        public Gender? Gender { get; set; }

        [Required(ErrorMessage = "Please enter your date of birth.")]
        [DataType(DataType.Date)]
        [Display(Name = "Date of birth")]
        public DateOnly? DateOfBirth { get; set; }

        [Required(ErrorMessage = "Please choose a password.")]
        [StringLength(128, MinimumLength = 10, ErrorMessage = "The password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "I accept the terms of service and privacy policy")]
        public bool AcceptTerms { get; set; }

        [Display(Name = "I explicitly consent to Nikah Noor processing my religious beliefs and the other profile details I provide, for the purpose of matchmaking")]
        public bool ConsentSpecialCategory { get; set; }
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

        if (Input.DateOfBirth is { } dob)
        {
            var age = Age.On(dob, Age.Today);
            if (age < 18)
            {
                ModelState.AddModelError("Input.DateOfBirth", l["You must be at least 18 years old to register."]);
            }
            else if (age > 120)
            {
                ModelState.AddModelError("Input.DateOfBirth", l["Please enter a valid date of birth."]);
            }
        }

        if (!Input.AcceptTerms)
        {
            ModelState.AddModelError("Input.AcceptTerms", l["You must accept the terms to register."]);
        }

        if (!Input.ConsentSpecialCategory)
        {
            ModelState.AddModelError("Input.ConsentSpecialCategory", l["Your consent is required because a matrimonial profile contains religious information."]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            UserName = Input.Email.Trim(),
            Email = Input.Email.Trim(),
            FirstName = Input.FirstName.Trim(),
            Gender = Input.Gender!.Value,
            DateOfBirth = Input.DateOfBirth!.Value,
            ConsentGivenAt = now,
            ConsentVersion = Consent.CurrentVersion,
            CreatedAt = now,
        };

        var result = await users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            // Don't reveal whether an address is already registered.
            if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                var existing = await users.FindByEmailAsync(user.Email);
                if (existing is not null)
                {
                    await emailSender.SendEmailAsync(user.Email, "Someone tried to register with your email",
                        "<p>Someone tried to create a Nikah Noor account with this email address. If it was you, sign in or reset your password instead. If not, you can ignore this email.</p>");
                }

                return RedirectToPage("RegisterConfirmation", new { email = user.Email, returnUrl });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("New account created");
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var callbackUrl = Url.Page("/Account/ConfirmEmail", pageHandler: null, values: new { area = "Identity", userId = user.Id, code, returnUrl }, protocol: Request.Scheme)!;
        await emailSender.SendEmailAsync(user.Email, "Confirm your email",
            $"<p>Assalamu alaikum {HtmlEncoder.Default.Encode(user.FirstName)},</p><p>Please confirm your Nikah Noor account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.</p>");

        return RedirectToPage("RegisterConfirmation", new { email = user.Email, returnUrl });
    }
}
