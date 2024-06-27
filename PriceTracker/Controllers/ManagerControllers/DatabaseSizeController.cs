using Microsoft.AspNetCore.Mvc;
using PriceTracker.Data;
using System.Threading.Tasks;

namespace PriceTracker.Controllers.ManagerControllers
{
    public class DatabaseSizeController : Controller
    {
        private readonly PriceTrackerContext _context;

        public DatabaseSizeController(PriceTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var tableSizes = await _context.GetTableSizes();
            return View("~/Views/ManagerPanel/DatabaseSize/Index.cshtml", tableSizes);
        }
    }
}


