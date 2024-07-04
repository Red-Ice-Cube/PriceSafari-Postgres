using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Hubs;

namespace PriceTracker.Controllers.MemberControllers
{
    public class ChanelController : Controller
    {

        private readonly PriceTrackerContext _context;

        public ChanelController(PriceTrackerContext context)
        {
            _context = context;
            
        }


        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();

            return View("~/Views/Panel/Chanel/Index.cshtml", stores);
        }
    }
}
