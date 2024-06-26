using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ManagerPanelController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public ManagerPanelController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [Authorize]
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue)
                startDate = DateTime.Now.AddDays(-31).Date;

            if (!endDate.HasValue)
                endDate = DateTime.Now;

            startDate = startDate.Value.Date;
            endDate = endDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            var categoryClicks = await GetCategoryClicks(startDate.Value, endDate.Value);
            var modelData = await GetModelData(startDate.Value, endDate.Value);
            var categoryEarnings = await GetCategoryEarnings(startDate.Value, endDate.Value);
            var categoryOrders = await GetCategoryOrders(startDate.Value, endDate.Value);

            var totalLinksDuringPeriod = await _context.AffiliateLink
                .CountAsync(c => c.CreationDate >= startDate && c.CreationDate <= endDate);

            var memberUsers = await _userManager.Users
                .Where(u => u.IsMember)
                .ToListAsync();

            var pendingAffiliates = await _context.AffiliateVerification
                .Where(a => a.IsVerified == false)
                .CountAsync();

            var totalAffiliates = memberUsers.Count;

            var model = new ManagerPanelViewModel
            {
                TotalAffiliates = totalAffiliates,
                PendingAffiliates = pendingAffiliates,
            };

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new
                {
                    modelTimeLabels = modelData.TimeLabels,
                    modelData.ManagerEarnings,
                    modelData.ManagerOrders,
                    modelData.ManagerSales,
                    modelData.ManagerClicks,

                    categoryClicks = categoryClicks.Select(cc => new
                    {
                        cc.CategoryName,
                        cc.CategoryClickCount
                    }).ToList(),
                    totalClicksDuringPeriod = modelData.ManagerClicks.Sum(),
                    totalEarningsDuringPeriod = modelData.ManagerEarnings.Sum(),
                    totalOrdersDuringPeriod = modelData.ManagerOrders.Sum(),
                    totalSalesDuringPeriod = modelData.ManagerSales.Sum(),
                    totalLinksDuringPeriod,
                    categoryEarnings = categoryEarnings.Select(cc => new
                    {
                        cc.Category,
                        cc.Earnings
                    }).ToList(),
                    categoryOrders = categoryOrders.Select(cc => new
                    {
                        cc.Category,
                        cc.Orders
                    }).ToList(),
                });
            }

            return View("~/Views/ManagerPanel/Index.cshtml", model);
        }

        [NonAction]
        public async Task<(List<string> TimeLabels, List<int> ManagerClicks, List<decimal> ManagerEarnings, List<int> ManagerOrders, List<decimal> ManagerSales)> GetModelData(DateTime startDate, DateTime endDate)
        {
            bool groupByHours = (endDate - startDate).TotalDays <= 1;
            string timeLabelFormat = groupByHours ? "HH:mm" : "dd.MM";
            int intervalCount = groupByHours ? (int)Math.Ceiling((endDate - startDate).TotalHours) + 1 : (endDate - startDate).Days + 1;

            List<string> timeLabels = Enumerable.Range(0, intervalCount)
                .Select(i => startDate.AddHours(groupByHours ? i : i * 24).ToString(timeLabelFormat))
                .ToList();

            var baseQuery = _context.Order
                .Where(o => o.CreationDate >= startDate && o.CreationDate <= endDate);

            var groupedResults = await baseQuery
                .GroupBy(o => groupByHours ? EF.Functions.DateDiffHour(startDate, o.CreationDate) : EF.Functions.DateDiffDay(startDate, o.CreationDate))
                .Select(g => new
                {
                    Interval = g.Key,
                    Earnings = g.Sum(o => o.AffiliateCommision),
                    Orders = g.Count(),
                    Sales = g.Sum(o => o.ProductPrice)
                })
                .ToListAsync();

            var clickResults = await _context.AffiliateLinkClick
                .Where(c => c.ClickTime >= startDate && c.ClickTime <= endDate)
                .GroupBy(c => groupByHours ? EF.Functions.DateDiffHour(startDate, c.ClickTime) : EF.Functions.DateDiffDay(startDate, c.ClickTime))
                .Select(g => new
                {
                    Interval = g.Key,
                    Clicks = g.Count()
                })
                .ToListAsync();

            var clicks = new int[intervalCount];
            var earnings = new decimal[intervalCount];
            var orders = new int[intervalCount];
            var sales = new decimal[intervalCount];

            foreach (var result in groupedResults)
            {
                if (result.Interval >= 0 && result.Interval < intervalCount)
                {
                    earnings[result.Interval] = result.Earnings;
                    orders[result.Interval] = result.Orders;
                    sales[result.Interval] = result.Sales;
                }
            }

            foreach (var click in clickResults)
            {
                if (click.Interval >= 0 && click.Interval < intervalCount)
                {
                    clicks[click.Interval] = click.Clicks;
                }
            }

            return (timeLabels, clicks.ToList(), earnings.ToList(), orders.ToList(), sales.ToList());
        }

        public async Task<List<ManagerCategoryClick>> GetCategoryClicks(DateTime startDate, DateTime endDate)
        {
            var affiliateLinkClicks = await _context.AffiliateLinkClick
                 .Include(click => click.AffiliateLink)
            .ThenInclude(link => link.Product)
                 .Where(click => click.ClickTime >= startDate && click.ClickTime <= endDate)
                 .ToListAsync();

            var categoryClicks = new Dictionary<int, int>(); // Klucz: CategoryId, Wartość: Liczba kliknięć

            foreach (var click in affiliateLinkClicks)
            {
                var categoryId = click.AffiliateLink.Product != null && click.AffiliateLink.Product.CategoryId.HasValue
                                 ? click.AffiliateLink.Product.CategoryId
                                 : null;

                // Pomijanie kliknięć, dla których nie można ustalić categoryId
                if (!categoryId.HasValue)
                {
                    continue;
                }

                // Zliczanie kliknięć
                if (categoryClicks.ContainsKey(categoryId.Value))
                {
                    categoryClicks[categoryId.Value]++;
                }
                else
                {
                    categoryClicks.Add(categoryId.Value, 1);
                }
            }

            // Sortowanie i ograniczenie do pierwszych 8 kategorii
            var sortedCategoryClicks = categoryClicks
                .OrderByDescending(kvp => kvp.Value)
                .Take(8)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var result = sortedCategoryClicks.Select(kvp => new ManagerCategoryClick
            {
                CategoryName = _context.Category.FirstOrDefault(c => c.CategoryId == kvp.Key)?.CategoryName ?? "Nieznana kategoria",
                CategoryClickCount = kvp.Value
            }).ToList();

            return result;
        }

        [NonAction]
        public async Task<List<ManagerCodeEarnings>> GetCategoryEarnings(DateTime startDate, DateTime endDate)
        {
            var allOrders = await _context.Order
                .Where(order => order.CreationDate >= startDate && order.CreationDate <= endDate)
                .Include(order => order.Product)
                .ThenInclude(product => product.Category)
                .ToListAsync();

            var earningsData = new Dictionary<int, ManagerCodeEarnings>();

            foreach (var order in allOrders)
            {
                var product = order.Product;
                if (product != null)
                {
                    var category = product.Category;
                    if (category != null)
                    {
                        if (!earningsData.TryGetValue(category.CategoryId, out var existingEarningData))
                        {
                            existingEarningData = new ManagerCodeEarnings
                            {
                                Category = category.CategoryName,
                                Earnings = 0m,
                                CreationDate = DateTime.Now
                            };
                            earningsData[category.CategoryId] = existingEarningData;
                        }

                        existingEarningData.Earnings += order.ProductPrice;
                    }
                }
            }

            var sortedEarningsData = earningsData.Values
                .OrderByDescending(earning => earning.Earnings)
                .Take(8)
                .ToList();

            return sortedEarningsData;
        }

        [NonAction]
        public async Task<List<ManagerCodeOrders>> GetCategoryOrders(DateTime startDate, DateTime endDate)
        {
            var allOrders = await _context.Order
                .Where(order => order.CreationDate >= startDate && order.CreationDate <= endDate)
                .Include(order => order.Product)
                .ThenInclude(product => product.Category)
                .ToListAsync();

            var ordersData = new Dictionary<int, ManagerCodeOrders>();

            foreach (var order in allOrders)
            {
                var product = order.Product;
                if (product != null)
                {
                    var category = product.Category;
                    if (category != null)
                    {
                        if (!ordersData.TryGetValue(category.CategoryId, out var existingOrdersData))
                        {
                            existingOrdersData = new ManagerCodeOrders
                            {
                                Category = category.CategoryName,
                                Orders = 0,
                                CreationDate = DateTime.Now
                            };
                            ordersData[category.CategoryId] = existingOrdersData;
                        }

                        existingOrdersData.Orders += order.Amount;
                    }
                }
            }

            var sortedOrdersData = ordersData.Values
               .OrderByDescending(order => order.Orders)
               .Take(8)
               .ToList();

            return sortedOrdersData;
        }
    }
}