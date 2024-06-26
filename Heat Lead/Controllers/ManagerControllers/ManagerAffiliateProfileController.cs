using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Plotly.NET.StyleParam.DrawingStyle;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ManagerAffiliateProfileController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public ManagerAffiliateProfileController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [Authorize]
        public async Task<IActionResult> Profile(string codePAR, DateTime? startDate, DateTime? endDate)
        {
            if (!startDate.HasValue)
                startDate = DateTime.Now.AddDays(-31).Date;

            if (!endDate.HasValue)
                endDate = DateTime.Now;

            startDate = startDate.Value.Date;
            endDate = endDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == codePAR);

            if (user == null)
            {
                return NotFound("User not found.");
            }

            var userId = user.Id;

            var categoryClicks = await GetCategoryClicks(userId, startDate.Value, endDate.Value);
            var modelData = await GetModelData(userId, startDate.Value, endDate.Value);
            var categoryEarnings = await GetCategoryEarnings(userId, startDate.Value, endDate.Value);

            var fixedStartDate = DateTime.Now.AddDays(-45).Date;
            var fixedEndDate = DateTime.Now.AddDays(+1).Date;

            var datesRange = Enumerable.Range(0, (fixedEndDate - fixedStartDate).Days + 0)
                .Select(offset => fixedStartDate.AddDays(offset))
                .ToList();

            var walletDataFromDb = await _context.Order
                .Where(order => order.UserId == userId && order.CreationDate >= fixedStartDate && order.CreationDate <= fixedEndDate)
                .GroupBy(order => order.CreationDate.Date)
                .Select(g => new WalletData
                {
                    Date = g.Key,
                    InValidationEarnings = g.Where(o => !o.InWallet && o.IsAccepted).Sum(o => o.AffiliateCommision),
                    AcceptedEarnings = g.Where(o => o.InWallet && o.IsAccepted).Sum(o => o.AffiliateCommision)
                })
                .ToListAsync();

            var walletData = datesRange.Select(date => new WalletData
            {
                Date = date,
                InValidationEarnings = walletDataFromDb.FirstOrDefault(w => w.Date == date)?.InValidationEarnings ?? 0,
                AcceptedEarnings = walletDataFromDb.FirstOrDefault(w => w.Date == date)?.AcceptedEarnings ?? 0
            }).ToList();

            var userOrders = await _context.Order
            .Where(order => order.CreationDate >= startDate && order.CreationDate < endDate && order.UserId == user.Id)
            .Include(o => o.Product)
            .ThenInclude(p => p.Category)
            .ToListAsync();

            var model = new ManagerAffiliateProfileViewModel
            {
                UserWallet = await _context.Wallet.FirstOrDefaultAsync(w => w.UserId == userId),
                InValidationEarnings = walletData.Sum(w => w.InValidationEarnings),
                CodePAR = user.CodePAR,
                PartnerName = user.PartnerName,
                PartnerSurname = user.PartnerSurname,
                JoinDate = user.CreationDate,
                Email = user.Email,
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

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new
                {
                    modelTimeLabels = modelData.TimeLabels,
                    modelData.Earnings,
                    modelData.Orders,
                    modelData.Sales,
                    modelData.Clicks,
                    categoryClicks = categoryClicks.Select(cc => new { cc.CategoryName, cc.CategoryClickCount }).ToList(),
                    totalClicksDuringPeriod = modelData.Clicks.Sum(),
                    totalEarningsDuringPeriod = modelData.Earnings.Sum(),
                    totalOrdersDuringPeriod = modelData.Orders.Sum(),
                    totalSalesDuringPeriod = modelData.Sales.Sum(),
                    acceptedEarningsDuringPeriod = modelData.AcceptedEarnings.Sum(),
                    acceptedOrdersDuringPeriod = modelData.AcceptedOrders.Sum(),
                    acceptedSalesDuringPeriod = modelData.AcceptedSales.Sum(),
                    canceledEarningsDuringPeriod = modelData.CanceledEarnings.Sum(),
                    canceledOrdersDuringPeriod = modelData.CanceledOrders.Sum(),
                    canceledSalesDuringPeriod = modelData.CanceledSales.Sum(),
                    categoryEarnings = categoryEarnings.Select(cc => new { cc.Category, cc.Earnings }).ToList(),
                    WalletData = walletData.Select(w => new { Date = w.Date.ToString("yyyy-MM-dd"), w.InValidationEarnings, w.AcceptedEarnings }).ToList()
                });
            }

            return View("~/Views/ManagerPanel/Affiliates/Profile.cshtml", model);
        }

        [NonAction]
        public async Task<(List<string> TimeLabels, List<int> Clicks, List<decimal> Earnings, List<int> Orders, List<decimal> Sales, List<decimal> AcceptedEarnings, List<int> AcceptedOrders, List<decimal> AcceptedSales, List<decimal> CanceledEarnings, List<int> CanceledOrders, List<decimal> CanceledSales)> GetModelData(string userId, DateTime startDate, DateTime endDate)
        {
            bool groupByHours = (endDate - startDate).TotalDays <= 1;
            string timeLabelFormat = groupByHours ? "HH:mm" : "dd.MM";
            int intervalCount = groupByHours ? (int)Math.Ceiling((endDate - startDate).TotalHours) + 1 : (endDate - startDate).Days + 1;

            List<string> timeLabels = Enumerable.Range(0, intervalCount)
                .Select(i => startDate.AddHours(groupByHours ? i : i * 24).ToString(timeLabelFormat))
                .ToList();

            var baseQuery = _context.Order
                .Where(o => o.UserId == userId && o.CreationDate >= startDate && o.CreationDate <= endDate);

            var groupedResults = await baseQuery
                .GroupBy(o => groupByHours ? EF.Functions.DateDiffHour(startDate, o.CreationDate) : EF.Functions.DateDiffDay(startDate, o.CreationDate))
                .Select(g => new
                {
                    Interval = g.Key,
                    Earnings = g.Sum(o => o.AffiliateCommision),
                    Orders = g.Count(),
                    Sales = g.Sum(o => o.ProductPrice),
                    AcceptedEarnings = g.Where(o => o.IsAccepted).Sum(o => o.AffiliateCommision),
                    AcceptedOrders = g.Where(o => o.IsAccepted).Count(),
                    AcceptedSales = g.Where(o => o.IsAccepted).Sum(o => o.ProductPrice),
                    CanceledEarnings = g.Where(o => !o.IsAccepted).Sum(o => o.AffiliateCommision),
                    CanceledOrders = g.Where(o => !o.IsAccepted).Count(),
                    CanceledSales = g.Where(o => !o.IsAccepted).Sum(o => o.ProductPrice)
                })
                .ToListAsync();

            var clickResults = await _context.AffiliateLinkClick
                .Where(c => c.AffiliateLink.UserId == userId && c.ClickTime >= startDate && c.ClickTime <= endDate)
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
            var acceptedEarnings = new decimal[intervalCount];
            var acceptedOrders = new int[intervalCount];
            var acceptedSales = new decimal[intervalCount];
            var canceledEarnings = new decimal[intervalCount];
            var canceledOrders = new int[intervalCount];
            var canceledSales = new decimal[intervalCount];

            foreach (var result in groupedResults)
            {
                if (result.Interval >= 0 && result.Interval < intervalCount)
                {
                    earnings[result.Interval] = result.Earnings;
                    orders[result.Interval] = result.Orders;
                    sales[result.Interval] = result.Sales;
                    acceptedEarnings[result.Interval] = result.AcceptedEarnings;
                    acceptedOrders[result.Interval] = result.AcceptedOrders;
                    acceptedSales[result.Interval] = result.AcceptedSales;
                    canceledEarnings[result.Interval] = result.CanceledEarnings;
                    canceledOrders[result.Interval] = result.CanceledOrders;
                    canceledSales[result.Interval] = result.CanceledSales;
                }
            }

            foreach (var click in clickResults)
            {
                if (click.Interval >= 0 && click.Interval < intervalCount)
                {
                    clicks[click.Interval] = click.Clicks;
                }
            }

            return (timeLabels, clicks.ToList(), earnings.ToList(), orders.ToList(), sales.ToList(),
                    acceptedEarnings.ToList(), acceptedOrders.ToList(), acceptedSales.ToList(), canceledEarnings.ToList(),
                    canceledOrders.ToList(), canceledSales.ToList());
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
            var result = sortedCategoryClicks.Select(kvp => new Models.ManagerViewModels.CategoryClick
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

            var earningsData = new Dictionary<int, Models.ManagerViewModels.CodeEarnings>();

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
                            existingEarningData = new Models.ManagerViewModels.CodeEarnings
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

            // Sortowanie według zarobków w porządku malejącym i ograniczenie do pierwszych 8 kategorii
            var sortedEarningsData = earningsData.Values
                .OrderByDescending(earning => earning.Earnings)
                .Take(8)
                .ToList();

            return sortedEarningsData;
        }
    }
}