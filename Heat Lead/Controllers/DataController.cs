using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers
{
    [Authorize(Roles = "Member")]
    public class DataController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public DataController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue)
                startDate = DateTime.Now.AddDays(-31).Date;

            if (!endDate.HasValue)
                endDate = DateTime.Now;

            startDate = startDate.Value.Date;
            endDate = endDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return NotFound();
            }

            ViewData["UserFullName"] = $"{user.PartnerName} {user.PartnerSurname}";

            var totalClicksDuringPeriod = await _context.AffiliateLinkClick
                .CountAsync(c => c.AffiliateLink.UserId == user.Id && c.ClickTime >= startDate && c.ClickTime <= endDate);

            var totalEarningsDuringPeriod = await _context.Order
                .Where(order => order.UserId == user.Id && order.CreationDate >= startDate && order.CreationDate < endDate)
                .SumAsync(order => order.AffiliateCommision);

            var totalOrdersDuringPeriod = await _context.Order
                .Where(order => order.UserId == user.Id && order.CreationDate >= startDate && order.CreationDate < endDate)
                .SumAsync(order => order.Amount);

            var totalSalesDuringPeriod = await _context.Order
                .Where(order => order.UserId == user.Id && order.CreationDate >= startDate && order.CreationDate < endDate)
                .SumAsync(order => order.ProductPrice);

            var inValidationEarnings = await _context.Order
                    .Where(order => order.UserId == user.Id
                    && order.InValidation == true
                    && order.IsAccepted == true)
                    .SumAsync(order => order.AffiliateCommision);

            var userWallet = await _context.Wallet.FirstOrDefaultAsync(w => w.UserId == user.Id);

            var userOrders = await _context.Order
            .Where(order => order.CreationDate >= startDate && order.CreationDate < endDate && order.UserId == user.Id)
            .Include(o => o.Product) // Ładowanie produktów
            .ThenInclude(p => p.Category) // Ładowanie kategorii dla produktu
            .ToListAsync();

            var model = new DashboardViewModel
            {
                UserWallet = userWallet,
                InValidationEarnings = inValidationEarnings
            };

            model.Orders = userOrders
            .OrderByDescending(order => order.CreationDate)
            .Select(order => new Orders
            {
                OrderNumber = order.OrderNumber,
                CategoryName = order.Product?.Category?.CategoryName,
                ProductName = order.Product?.ProductName,
                Amount = order.Amount,
                ProductPrice = order.ProductPrice,
                Earnings = order.AffiliateCommision,
                CreationDate = order.CreationDate,
                ValidationEndDate = order.ValidationEndDate,
                Accepted = order.InWallet,
                ValidationStatus = order.ValidationEndDate.HasValue
                    ? (order.InValidation
                    ? $"Walidacja za {order.ValidationEndDate.Value.Subtract(DateTime.Now).Days} dni"
                     : "Zaakceptowano")
                : "Brak daty walidacji",
                IsCancelled = !order.IsAccepted,
            }).ToList();

            // Sprawdzanie czy żądanie jest AJAXem
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var categoryClicks = await GetCategoryClicks(user.Id, startDate.Value, endDate.Value);
                var modelData = await GetModelData(user.Id, startDate.Value, endDate.Value);
                var categoryEarnings = await GetCategoryEarnings(user.Id, startDate.Value, endDate.Value);

                return Json(new
                {
                    modelTimeLabels = modelData.TimeLabels,
                    modelData.Earnings,
                    modelData.Orders,
                    modelData.Sales,
                    modelData.Clicks,
                    categoryClicks = categoryClicks.Select(cc => new
                    {
                        cc.CategoryName,
                        cc.CategoryClickCount
                    }).ToList(),
                    totalClicksDuringPeriod,
                    totalEarningsDuringPeriod,
                    totalOrdersDuringPeriod,
                    totalSalesDuringPeriod,
                    categoryEarnings = categoryEarnings.Select(cc => new
                    {
                        cc.Category,
                        cc.Earnings
                    }).ToList()
                });
            }

            return View("~/Views/Panel/Data/Index.cshtml", model);
        }

        [NonAction]
        public async Task<(List<string> TimeLabels, List<int> Clicks, List<decimal> Earnings, List<int> Orders, List<decimal> Sales)> GetModelData(string userId, DateTime startDate, DateTime endDate)
        {
            int totalHours = (int)Math.Ceiling((endDate - startDate).TotalHours);
            string format = "";

            int points, hoursPerPoint;
            if (totalHours <= 24)
            {
                points = totalHours;
                hoursPerPoint = 1;
                format = "HH:00";
            }
            else if (totalHours <= 7 * 24)
            {
                points = totalHours / 6;
                hoursPerPoint = 6;
                format = "HH:00 dd.MM";
            }
            else if (totalHours <= 31 * 24)
            {
                points = (totalHours / 24) + 1;
                hoursPerPoint = 24;
                format = "dd.MM";
            }
            else
            {
                points = (totalHours / (24 * 7)) + 1;
                hoursPerPoint = 24 * 7;
                format = "dd.MM";  // możemy dostosować format w zależności od potrzeb
            }
            var hoursLabels = Enumerable.Range(0, points).Select(h =>
                   startDate.AddHours(h * hoursPerPoint).ToString(format)).ToList();

            var clicksPerHour = new List<int>();
            var earningsPerHour = new List<decimal>();
            var ordersPerHour = new List<int>();
            var salesPerHour = new List<decimal>();

            for (int i = 0; i < points; i++)
            {
                var hourStart = startDate.AddHours(i * hoursPerPoint);
                var hourEnd = startDate.AddHours((i + 1) * hoursPerPoint);

                var clicks = await _context.AffiliateLinkClick
                    .CountAsync(c => c.AffiliateLink.UserId == userId && c.ClickTime >= hourStart && c.ClickTime < hourEnd);

                var earnings = await _context.Order
                   .Where(order => order.UserId == userId
                                   && order.IsAccepted == true
                                   && order.CreationDate >= hourStart
                                   && order.CreationDate < hourEnd)
                   .SumAsync(order => order.AffiliateCommision);

                var orders = await _context.Order
                    .Where(order => order.UserId == userId && order.CreationDate >= hourStart && order.CreationDate < hourEnd)
                    .SumAsync(order => order.Amount);

                var sales = await _context.Order
                    .Where(order => order.UserId == userId && order.CreationDate >= hourStart && order.CreationDate < hourEnd)
                    .SumAsync(order => order.ProductPrice);

                clicksPerHour.Add(clicks);
                earningsPerHour.Add(earnings);
                ordersPerHour.Add(orders);
                salesPerHour.Add(sales);
            }

            return (hoursLabels, clicksPerHour, earningsPerHour, ordersPerHour, salesPerHour);
        }

        [NonAction]
        public async Task<List<CategoryClick>> GetCategoryClicks(string userId, DateTime startDate, DateTime endDate)
        {
            var affiliateLinkClicks = await _context.AffiliateLinkClick
                 .Include(click => click.AffiliateLink)
                     .ThenInclude(link => link.Product)
                 .Where(click => click.AffiliateLink.UserId == userId && click.ClickTime >= startDate && click.ClickTime <= endDate)
                 .ToListAsync();

            var categoryClicks = new Dictionary<int, int>(); // Klucz: CategoryId, Wartość: Liczba kliknięć

            foreach (var click in affiliateLinkClicks)
            {
                var categoryId = click.AffiliateLink.Product != null && click.AffiliateLink.Product.CategoryId.HasValue
                                 ? click.AffiliateLink.Product.CategoryId
                                 : null;

                if (!categoryId.HasValue)
                {
                    continue;
                }

                if (categoryClicks.ContainsKey(categoryId.Value))
                {
                    categoryClicks[categoryId.Value]++;
                }
                else
                {
                    categoryClicks.Add(categoryId.Value, 1);
                }
            }

            // Sortowanie i ograniczanie do pierwszych 8 kategorii z największą liczbą kliknięć
            var sortedCategoryClicks = categoryClicks
                .OrderByDescending(kvp => kvp.Value)
                .Take(8)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Przekształć wyniki na listę CategoryClick
            var result = sortedCategoryClicks.Select(kvp => new CategoryClick
            {
                CategoryName = _context.Category.FirstOrDefault(c => c.CategoryId == kvp.Key)?.CategoryName ?? "Nieznana kategoria",
                CategoryClickCount = kvp.Value
            }).ToList();

            return result;
        }

        [NonAction]
        public async Task<List<CodeEarnings>> GetCategoryEarnings(string userId, DateTime startDate, DateTime endDate)
        {
            var ordersForUser = await _context.Order
                .Where(order => order.UserId == userId && order.CreationDate >= startDate && order.CreationDate <= endDate)
                .Include(order => order.Product)
                .ThenInclude(product => product.Category)
                .ToListAsync();

            var earningsData = new Dictionary<int, CodeEarnings>();

            foreach (var order in ordersForUser)
            {
                var product = order.Product;
                if (product != null)
                {
                    var category = product.Category;
                    if (category != null)
                    {
                        if (!earningsData.TryGetValue(category.CategoryId, out var existingEarningData))
                        {
                            existingEarningData = new CodeEarnings
                            {
                                Category = category.CategoryName,
                                Earnings = 0m,
                                CreationDate = DateTime.Now
                            };
                            earningsData[category.CategoryId] = existingEarningData;
                        }

                        existingEarningData.Earnings += order.AffiliateCommision;
                    }
                }
            }

            // Sortowanie według zarobków w porządku malejącym i ograniczenie do pierwszych 8 kategorii
            var sortedEarningsData = earningsData.Values
                .OrderByDescending(earning => earning.Earnings)
                .Take(8)
                .ToList();

            return sortedEarningsData;
        }
    }
}