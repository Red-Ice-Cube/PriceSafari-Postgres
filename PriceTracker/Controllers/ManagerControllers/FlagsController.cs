using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Linq;
using System.Threading.Tasks;

namespace PriceTracker.Controllers
{
    public class FlagsController : Controller
    {
        private readonly PriceTrackerContext _context;

        public FlagsController(PriceTrackerContext context)
        {
            _context = context;
        }

        // GET: Flags/Create
        public IActionResult Create(int storeId)
        {
            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/Flags/Create.cshtml");
        }

        // POST: Flags/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FlagId,FlagName,FlagColor,StoreId")] FlagsClass flag, int storeId)
        {
            flag.StoreId = storeId;

            if (ModelState.IsValid)
            {
                _context.Add(flag);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index", "Store", new { id = storeId });
            }

            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/Flags/Create.cshtml", flag);
        }

        // GET: Flags/List
        public async Task<IActionResult> List(int storeId)
        {
            var flags = await _context.Flags.Where(f => f.StoreId == storeId).ToListAsync();
            ViewBag.StoreId = storeId;

            return View("~/Views/ManagerPanel/Flags/List.cshtml", flags);
        }
    }
}
