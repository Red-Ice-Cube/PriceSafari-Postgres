using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers.ManagerControllers

{
    [Authorize(Roles = "Manager, Admin")]
    public class ManagerOrderController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public ManagerOrderController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userOrders = await _context.Order
                 .Include(o => o.Product)
                 .ThenInclude(p => p.Category)
                 .Include(o => o.Heat_LeadUser)
                 .ToListAsync();

            var model = new ManagerOrderViewModel
            {
            };
            model.ManagerOrders = userOrders
               .OrderByDescending(order => order.CreationDate) // Sortowanie zamówień od najnowszych do najstarszych
               .Select(order => new ManagerOrders
               {
                   OrderId = order.OrderId,
                   AffiliateName = order.Heat_LeadUser.PartnerName,
                   AffiliateSurname = order.Heat_LeadUser.PartnerSurname,
                   OrderNumber = order.OrderNumber,
                   CategoryName = order.Product?.Category?.CategoryName,
                   ProductName = order.Product?.ProductName,
                   Amount = order.Amount,
                   ProductPrice = order.ProductPrice,
                   Earnings = order.AffiliateCommision,
                   CreationDate = order.CreationDate,
                   ValidationEndDate = order.ValidationEndDate,
                   Accepted = order.InWallet,
                   InValidation = (order.ValidationEndDate.HasValue && DateTime.Now < order.ValidationEndDate.Value),
                   ValidationStatus = order.ValidationEndDate.HasValue
                   ? (order.InValidation
                   ? $"Walidacja za {order.ValidationEndDate.Value.Subtract(DateTime.Now).Days} dni"
                    : "Zaakceptowano")
               : "Brak daty walidacji",
                   IsCancelled = !order.IsAccepted,
               }).ToList();

            return View("~/Views/ManagerPanel/Order/Index.cshtml", model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangeOrderAcceptance(int orderId)
        {
            var order = await _context.Order.FindAsync(orderId);
            if (order == null)
            {
                return NotFound();
            }

            if (order.InValidation)
            {
                order.IsAccepted = !order.IsAccepted; // Przełączanie wartości IsAccepted
                _context.Update(order);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Nie można zmienić akceptacji zamówienia, które nie jest w walidacji." });
        }
    }
}