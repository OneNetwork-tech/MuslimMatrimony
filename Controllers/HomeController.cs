using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using MuslimMatrimony.Models;
using System.Diagnostics;
using Microsoft.AspNetCore.Localization;

namespace MuslimMatrimony.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        // Injects the localizer to get strings from .resx files
        private readonly IStringLocalizer<HomeController> _localizer;

        public HomeController(ILogger<HomeController> logger, IStringLocalizer<HomeController> localizer)
        {
            _logger = logger;
            _localizer = localizer;
        }

        // This action handles the request for the homepage
        public IActionResult Index()
        {
            // You can pass localized strings to the view using ViewData or a ViewModel
            ViewData["HeroTitle"] = _localizer["Find Your Righteous Partner"];
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // This is the crucial action for switching languages
        [HttpPost]
        public IActionResult SetLanguage(string culture, string returnUrl)
        {
            // Set a cookie with the user's selected language preference.
            // This cookie is then read by the localization middleware on subsequent requests.
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
            );

            // Redirect the user back to the page they were on.
            return LocalRedirect(returnUrl);
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
