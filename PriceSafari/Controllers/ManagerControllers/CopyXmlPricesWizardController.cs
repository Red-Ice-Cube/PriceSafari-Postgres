using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class CopyXmlPricesWizardController : Controller
    {
        private readonly PriceSafariContext _context;

        public CopyXmlPricesWizardController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> ShowCopyXmlWizard(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = "Sklep nie znaleziony.";
                return RedirectToAction("Index", "ProductMapping");
            }

            var mapping = await _context.CopyXmlPriceMappings
                .FirstOrDefaultAsync(m => m.StoreId == storeId);

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.CopyXMLPricesEnabled = store.CopyXMLPrices;
            ViewBag.ExistingMapping = mapping == null
                ? "null"
                : System.Text.Json.JsonSerializer.Serialize(mapping);

            return View("~/Views/ManagerPanel/ProductMapping/WizardCopyXmlPrices.cshtml");
        }

        /// <summary>Proxy XML — taki sam jak w GoogleImportWizardXml.</summary>
        [HttpGet]
        public async Task<IActionResult> ProxyXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Brak sklepu.");
            if (string.IsNullOrEmpty(store.ProductMapXmlUrlGoogle))
                return BadRequest("Brak URL do pliku w bazie (ProductMapXmlUrlGoogle).");

            var url = store.ProductMapXmlUrlGoogle;

            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var filePath = Uri.UnescapeDataString(url.Substring("file://".Length))
                        .TrimStart('/', '\\');
                    var xmlContent = await System.IO.File.ReadAllTextAsync(filePath);
                    return Content(xmlContent, "text/xml");
                }
                catch (Exception ex) { return BadRequest($"Błąd odczytu pliku: {ex.Message}"); }
            }

            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyFeedBot/1.0)");
            try
            {
                var xmlContent = await client.GetStringAsync(url);
                return Content(xmlContent, "text/xml");
            }
            catch (Exception ex) { return BadRequest($"Błąd pobierania: {ex.Message}"); }
        }

        [HttpGet]
        public async Task<IActionResult> GetMapping(int storeId)
        {
            var m = await _context.CopyXmlPriceMappings
                .FirstOrDefaultAsync(x => x.StoreId == storeId);
            return Json(m);
        }

        public class MappingDto
        {
            public int StoreId { get; set; }
            public string KeyField { get; set; } = "Ean";
            public string? ProductNodeXPath { get; set; }
            public string? KeyXPath { get; set; }
            public string? PriceXPath { get; set; }
            public string? PriceWithShippingXPath { get; set; }
            public string? InStockXPath { get; set; }
            public string? InStockMarkerValue { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveMapping([FromBody] MappingDto dto)
        {
            if (dto == null || dto.StoreId <= 0)
                return Json(new { success = false, message = "Brak danych." });

            var existing = await _context.CopyXmlPriceMappings
                .FirstOrDefaultAsync(m => m.StoreId == dto.StoreId);

            var keyField = dto.KeyField?.Equals("ExternalId", StringComparison.OrdinalIgnoreCase) == true
                ? CopyXmlKeyField.ExternalId
                : CopyXmlKeyField.Ean;

            if (existing == null)
            {
                existing = new CopyXmlPriceMapping { StoreId = dto.StoreId };
                _context.CopyXmlPriceMappings.Add(existing);
            }

            existing.KeyField = keyField;
            existing.ProductNodeXPath = dto.ProductNodeXPath;
            existing.KeyXPath = dto.KeyXPath;
            existing.PriceXPath = dto.PriceXPath;
            existing.PriceWithShippingXPath = dto.PriceWithShippingXPath;
            existing.InStockXPath = dto.InStockXPath;
            existing.InStockMarkerValue = dto.InStockMarkerValue;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Zapisano mapowanie doklejania cen XML." });
        }

        [HttpPost]
        public async Task<IActionResult> ClearMapping(int storeId)
        {
            var existing = await _context.CopyXmlPriceMappings
                .FirstOrDefaultAsync(m => m.StoreId == storeId);
            if (existing != null)
            {
                _context.CopyXmlPriceMappings.Remove(existing);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Usunięto mapowanie.";
            }
            return RedirectToAction("ShowCopyXmlWizard", new { storeId });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleCopyXMLPrices(int storeId, bool enabled)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return Json(new { success = false, message = "Brak sklepu." });
            store.CopyXMLPrices = enabled;
            await _context.SaveChangesAsync();
            return Json(new { success = true, enabled });
        }

        /// <summary>
        /// Zwraca produkty sklepu z IsScrapable=true — do podglądu mapowania po stronie JS.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetScrapableProducts(int storeId)
        {
            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.Ean,
                    p.ExternalId
                })
                .ToListAsync();
            return Json(products);
        }
    }
}