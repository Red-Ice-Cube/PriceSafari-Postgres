using PriceTracker.Models;
using PriceTracker.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using System.Diagnostics;

namespace PriceTracker.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PriceTrackerContext _context;

        public HomeController(PriceTrackerContext context, ILogger<HomeController> logger)
        {
            _logger = logger;
            _context = context;
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}