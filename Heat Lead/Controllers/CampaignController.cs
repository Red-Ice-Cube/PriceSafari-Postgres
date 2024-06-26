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
    public class CampaignController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public CampaignController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            DateTime userLocalTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
            DateTime startDate = userLocalTime.Date.AddDays(-31);
            DateTime endDate = userLocalTime.Date.AddDays(1).AddTicks(-1);

            var campaignViewModels = await _context.Campaigns
                .Where(c => c.UserId == user.Id && c.IsActive)
                .OrderBy(c => c.Position)
                .Select(c => new CampaignViewModel
                {
                    CampaignId = c.CampaignId,
                    CampaignName = c.CampaignName,
                    ProductsCount = c.CampaignProducts.Count,
                    IsActive = c.IsActive,
                    Position = c.Position,
                    AffiliateLink = c.AffiliateLinks.Select(a => new AffiliateLinkViewModel
                    {
                        Id = a.AffiliateLinkId,
                        ClickCount = a.AffiliateLinkClick.Count,
                        ExactSoldProductsCount = a.ExactSoldProductsCount,
                        AffiliateURL = a.AffiliateURL,
                        ProductImage = a.Product.ProductImage,
                        ProductName = a.Product.ProductName,
                        IsActive = a.Product.IsActive,
                        SoldValue = a.Product.AffiliateCommission * a.ExactSoldProductsCount,
                        ClickTimes = a.AffiliateLinkClick.Where(click => click.ClickTime >= startDate && click.ClickTime <= endDate).Select(click => click.ClickTime).ToList(),
                    }).ToList()
                })
                .ToListAsync();

            return View("~/Views/Panel/Campaign/Index.cshtml", campaignViewModels);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetCampaignData()
        {
            DateTime userLocalTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
            DateTime startDate = userLocalTime.Date.AddDays(-30);
            DateTime endDate = userLocalTime.Date.AddDays(1).AddTicks(-1);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var campaigns = await _context.Campaigns
                .Where(c => c.UserId == user.Id && c.IsActive)
                .Select(c => new
                {
                    Campaign = c,
                    Orders = _context.OrderDetails.Where(o => o.CampaignId == c.CampaignId && o.CreationDate >= startDate && o.CreationDate < endDate.AddDays(1)).Sum(o => o.ProductQuantity),
                    FullOrders = _context.OrderDetails.Where(o => o.CampaignId == c.CampaignId).Sum(o => o.ProductQuantity),
                    Clicks = _context.AffiliateLinkClick.Where(a => a.AffiliateLink.CampaignId == c.CampaignId && a.ClickTime >= startDate && a.ClickTime < endDate.AddDays(1)).Count(),
                    FullClicks = _context.AffiliateLinkClick.Where(a => a.AffiliateLink.CampaignId == c.CampaignId).Count(),
                    Earnings = _context.OrderDetails.Where(o => o.CampaignId == c.CampaignId && o.CreationDate >= startDate && o.CreationDate <= endDate).Sum(o => o.ProductQuantity * o.Product.AffiliateCommission),
                    FullEarnings = _context.OrderDetails.Where(o => o.CampaignId == c.CampaignId).Sum(o => o.ProductQuantity * o.Product.AffiliateCommission)
                })
                .ToListAsync();

            var campaignsData = campaigns.Select(c => new CampaignsData
            {
                CampaignId = c.Campaign.CampaignId,
                CampaignName = c.Campaign.CampaignName,
                Orders = c.Orders,
                FullOrders = c.FullOrders,
                Click = c.Clicks,
                FullClick = c.FullClicks,
                Earnings = c.Earnings,
                FullEarnings = c.FullEarnings,
                Clicks = new List<ClickDataViewModel>(),
                Sales = new List<SalesDataViewModel>(),
                CreationDate = c.Campaign.CreationDate,
            }).ToList();

            foreach (var campaignData in campaignsData)
            {
                var campaignId = campaignData.CampaignId;
                var clickDetails = _context.AffiliateLinkClick
                    .Where(c => c.AffiliateLink.CampaignId == campaignId && c.ClickTime >= startDate && c.ClickTime < endDate.AddDays(1))
                    .GroupBy(c => c.ClickTime.Date)
                    .ToDictionary(g => g.Key, g => g.Count());

                var salesDetails = _context.OrderDetails
                     .Include(o => o.Product)
                     .Where(o => o.CampaignId == campaignId && o.CreationDate >= startDate && o.CreationDate <= endDate)
                     .GroupBy(o => o.CreationDate.Date)
                     .ToDictionary(g => g.Key, g => g.Sum(o => o.ProductQuantity * (o.Product?.AffiliateCommission ?? 0)));

                for (DateTime date = startDate; date < endDate; date = date.AddDays(1))
                {
                    campaignData.Clicks.Add(new ClickDataViewModel { ClickTime = date, Count = clickDetails.ContainsKey(date) ? clickDetails[date] : 0 });
                    campaignData.Sales.Add(new SalesDataViewModel { SaleTime = date, Amount = salesDetails.ContainsKey(date) ? salesDetails[date] : 0 });
                }
            }

            return Json(campaignsData);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrder([FromBody] List<int> campaignOrder)
        {
            int position = 0;
            foreach (var campaignId in campaignOrder)
            {
                var campaign = await _context.Campaigns.FindAsync(campaignId);
                if (campaign != null)
                {
                    campaign.Position = position++;
                    _context.Update(campaign);
                }
            }
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteCampaign(int campaignId)
        {
            var campaign = await _context.Campaigns.FindAsync(campaignId);
            if (campaign == null)
            {
                return Json(new { success = false, message = "Nie znaleziono kampanii." });
            }

            campaign.IsActive = false;
            _context.Update(campaign);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCampaignName(int campaignId, string campaignName)
        {
            var campaign = await _context.Campaigns.FindAsync(campaignId);
            if (campaign == null)
            {
                return Json(new { success = false, message = "Nie znaleziono kampanii." });
            }

            campaign.CampaignName = campaignName;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Nazwa kampanii została zaktualizowana." });
        }
    }
}