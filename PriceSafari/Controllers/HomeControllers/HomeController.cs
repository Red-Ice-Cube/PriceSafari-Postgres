using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Models.HomeModels;
using System.Diagnostics;

namespace PriceSafari.Controllers.HomeControllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PriceSafariContext _context;
        private readonly IEmailSender _emailSender;

        public HomeController(PriceSafariContext context, ILogger<HomeController> logger, IEmailSender emailSender)
        {
            _logger = logger;
            _context = context;
            _emailSender = emailSender;
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
                    SubmissionDate = DateTime.Now
                };

                _context.ContactFormSubmissions.Add(submission);
                await _context.SaveChangesAsync();

                // Wysłanie powiadomienia email
                var subject = "Nowe zgłoszenie z formularza kontaktowego";
                var message = $"Nowe zgłoszenie od {submission.FirstName} {submission.LastName}";
                await _emailSender.SendEmailAsync("twoj_email@przyklad.pl", subject, message);

                return RedirectToAction("ContactThankYou");
            }
            return View(model);
        }

        public IActionResult ContactThankYou()
        {
            return View();
        }
    }
}
