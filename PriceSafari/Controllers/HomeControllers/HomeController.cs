using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Models.HomeModels;
using PriceSafari.Services.ViewRenderService;

namespace PriceSafari.Controllers.HomeControllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PriceSafariContext _context;
        private readonly IEmailSender _emailSender;
        private readonly IViewRenderService _viewRenderService;

        public HomeController(PriceSafariContext context, ILogger<HomeController> logger, IEmailSender emailSender, IViewRenderService viewRenderService)
        {
            _logger = logger;
            _context = context;
            _emailSender = emailSender;
            _viewRenderService = viewRenderService;
        }


        public async Task<IActionResult> Index()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();

            var viewModel = new HomeViewModel
            {
                Email = settings.ContactEmail,
                PhoneNumber = settings.ContactNumber
            };

            return View(viewModel);
        }

        public IActionResult Contact()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contact(ContactFormViewModel model)
        {
            if (ModelState.IsValid)
            {
                var submission = new ContactFormSubmission
                {
                    Email = model.Email,
                    CompanyName = model.CompanyName,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    ConsentToDataProcessing = model.ConsentToDataProcessing,
                    PhoneNumber = model.PhoneNumber,
                    PrefersPhone = model.PrefersPhone,
                    SubmissionDate = DateTime.Now
                };

                _context.ContactFormSubmissions.Add(submission);
                await _context.SaveChangesAsync();

                // Wysłanie powiadomienia email do administratora
                var adminSubject = "Nowe zgłoszenie z formularza kontaktowego";
                var adminMessage = $"Nowe zgłoszenie od {submission.FirstName} {submission.LastName}";
                await _emailSender.SendEmailAsync("twoj_email@przyklad.pl", adminSubject, adminMessage);

                // Wysłanie emaila z podziękowaniem do użytkownika
                var userSubject = "Dziękujemy za kontakt z nami!";
                // Renderowanie szablonu wiadomości e-mail
                string userMessage = await _viewRenderService.RenderToStringAsync("EmailTemplates/ThankYouEmail", submission);

                // Wysłanie e-maila
                await _emailSender.SendEmailAsync(
                    submission.Email,
                    userSubject,
                    userMessage);

                return RedirectToAction("ContactThankYou");
            }
            return View(model);
        }




    }
}
