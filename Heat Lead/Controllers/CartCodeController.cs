using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.IRepo.Interface;
using Heat_Lead.Models;
using Heat_Lead.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers
{
    [Authorize]
    public class CartCodeController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager, XmlGeneratorService xmlGeneratorService) : Controller
    {
        private readonly Heat_LeadContext _context = context;
        private readonly UserManager<Heat_LeadUser> _userManager = userManager;
        private readonly XmlGeneratorService _xmlGeneratorService = xmlGeneratorService;
        private readonly IApiService _apiService;

        [HttpPost]
        public async Task<IActionResult> GenerateCodesForCart([FromBody] GenerateCodesRequest request)
        {
            var user = await _userManager.GetUserAsync(User);

            var activeCampaignsCount = await _context.Campaigns
                .CountAsync(c => c.UserId == user.Id && c.IsActive);

            if (activeCampaignsCount >= 20)
            {
                return Json(new { error = "Nie możesz utworzyć więcej niż 20 aktywnych kampanii." });
            }

            var allActiveProducts = await _context.Product
                .Include(p => p.Category)
                    .ThenInclude(c => c.Store)
                .Where(p => p.IsActive)
                .ToListAsync();

            var limitedProductIds = request.ProductIds.Take(16).ToList();

            var cartProducts = allActiveProducts
                .Where(p => limitedProductIds.Contains(p.ProductId))
                .ToList();

            var campaign = new Campaign
            {
                CampaignName = request.CampaignName,
                UserId = user.Id
            };

            _context.Campaigns.Add(campaign);
            await _context.SaveChangesAsync();

            foreach (var product in cartProducts)
            {
                var campaignProduct = new CampaignProduct
                {
                    CampaignId = campaign.CampaignId,
                    ProductId = product.ProductId
                };
                _context.CampaignProducts.Add(campaignProduct);

                var generator = await _context.Generator.FirstOrDefaultAsync(g => g.ProductId == product.ProductId && g.UserId == user.Id);

                bool isNewGenerator = false;
                if (generator == null)
                {
                    isNewGenerator = true;

                    generator = new Generator
                    {
                        ProductId = product.ProductId,
                        CodeCAT = product.Category.CodeCAT,
                        StoreId = product.Category.StoreId,
                        CodeSTO = product.Category.Store.CodeSTO,
                        CategoryId = product.Category.CategoryId,
                        UserId = user.Id,
                        CodePAR = user.CodePAR
                    };

                    generator.CodeAFI = generator.CodeSTO.ToString() + generator.CodeCAT.ToString() + generator.CodePAR.ToString();
                    _context.Generator.Add(generator);
                }

                var existingLink = await _context.AffiliateLink.FirstOrDefaultAsync(a =>
                    a.CodeAFI == generator.CodeAFI &&
                    a.ProductId == product.ProductId &&
                    a.CampaignId == campaign.CampaignId);

                if (existingLink == null)
                {
                    var trackingParameter = $"{generator.CodeAFI}{product.ProductId}{campaign.CampaignId}";

                    var link = new AffiliateLink
                    {
                        UserId = user.Id,
                        ProductId = product.ProductId,
                        ProductURL = product.ProductURL,
                        CodeAFI = generator.CodeAFI,
                        StoreId = generator.StoreId,
                        CampaignId = campaign.CampaignId,
                        HeatLeadTrackingCode = trackingParameter,
                        AffiliateURL = $"{product.ProductURL}?heatlead={trackingParameter}"
                    };

                    _context.Add(link);
                }
            }
            await _context.SaveChangesAsync();
            var redirectUrl = Url.Action(nameof(CampaignController.Index), "Campaign");
            return Json(new { redirectUrl });
        }

        public class GenerateCodesRequest
        {
            public string CampaignName { get; set; }
            public List<int> ProductIds { get; set; }
        }
    }
}