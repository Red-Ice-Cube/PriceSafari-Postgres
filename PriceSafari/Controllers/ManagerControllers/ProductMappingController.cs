using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using PriceSafari.Models.ProductXML;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class ProductMappingController : Controller
    {
        private readonly PriceSafariContext _context;

        public ProductMappingController(PriceSafariContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();
            return View("~/Views/ManagerPanel/ProductMapping/Index.cshtml", stores);
        }

        public async Task<IActionResult> ProductXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            var mappedProducts = await _context.ProductMaps
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;

            return View("~/Views/ManagerPanel/ProductMapping/ProductXml.cshtml", mappedProducts);
        }

        [HttpPost]
        public async Task<IActionResult> TruncateProductMaps()
        {
            try
            {

                await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE ProductMaps");

                TempData["SuccessMessage"] = "Wszystkie wpisy w tabeli ProductMaps zostały pomyślnie usunięte.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {

                TempData["ErrorMessage"] = $"Wystąpił błąd podczas usuwania raportów: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        public async Task<IActionResult> MappedProducts(int storeId, bool onlyMismatch = false, bool onlyGoogleXmlMismatch = false)
        {

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
                return NotFound("Sklep nie znaleziony.");

            string storeNameLower = store.StoreName?.ToLower() ?? "";

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id })
                .FirstOrDefaultAsync();

            int latestScrapId = latestScrap?.Id ?? 0;
            var priceLookup = new Dictionary<int, ProductPrices>();

            if (latestScrapId > 0)
            {

                var priceHistoriesForScrap = await _context.PriceHistories
                    .Where(ph => ph.ScrapHistoryId == latestScrapId)

                    .Select(ph => new
                    {
                        ph.ProductId,
                        ph.Price,
                        ph.IsGoogle,
                        ph.StoreName,
                        ph.Id
                    })
                    .ToListAsync();

                priceLookup = priceHistoriesForScrap
               .Where(ph => (ph.StoreName ?? "").ToLower() == storeNameLower)
               .GroupBy(ph => ph.ProductId)
               .ToDictionary(
                   g => g.Key,
                   g => new ProductPrices
                   {

                       GooglePrice = g.Where(ph => ph.IsGoogle)
                                      .OrderByDescending(ph => ph.Id)
                                      .Select(ph => (decimal?)ph.Price)
                                      .FirstOrDefault(),

                       CeneoPrice = g.Where(ph => !ph.IsGoogle)
                                     .OrderByDescending(ph => ph.Id)
                                     .Select(ph => (decimal?)ph.Price)
                                     .FirstOrDefault()
                   }
               );
            }

            var productsData = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.ExternalId,
                    p.Url,
                    p.OfferUrl,
                    p.GoogleUrl,
                    p.IsScrapable,
                    p.IsRejected,
                    p.MainUrl,
                    p.ImgUrlGoogle,
                    p.ExportedNameCeneo,
                    p.ProductNameInStoreForGoogle,
                    p.Ean,
                    p.EanGoogle,
                    p.GoogleXMLPrice,
                    p.CeneoXMLPrice
                })
                .ToListAsync();

            var vmList = new List<MappedProductViewModel>();

            foreach (var pData in productsData)
            {

                var prices = priceLookup.TryGetValue(pData.ProductId, out var foundPrices) ? foundPrices : new ProductPrices();
                var googlePrice = prices.GooglePrice;
                var ceneoPrice = prices.CeneoPrice;
                decimal? diff = (googlePrice.HasValue && ceneoPrice.HasValue) ? googlePrice.Value - ceneoPrice.Value : (decimal?)null;

                bool satisfiesMismatch = !onlyMismatch
                    || (googlePrice.HasValue && ceneoPrice.HasValue
                        && googlePrice.Value != ceneoPrice.Value);

                bool satisfiesGoogleXmlMismatch = !onlyGoogleXmlMismatch
                    || (googlePrice.HasValue && pData.GoogleXMLPrice.HasValue
                        && googlePrice.Value != pData.GoogleXMLPrice.Value);

                if (!satisfiesMismatch || !satisfiesGoogleXmlMismatch)
                {
                    continue;
                }

                var vm = new MappedProductViewModel
                {
                    ProductId = pData.ProductId,
                    ProductName = pData.ProductName,
                    ExternalId = pData.ExternalId,
                    Url = pData.Url,
                    UrlCeneo = pData.OfferUrl,
                    UrlGoogle = pData.GoogleUrl,
                    IsScrapable = pData.IsScrapable,
                    IsRejected = pData.IsRejected,
                    HasGoogle = !string.IsNullOrEmpty(pData.GoogleUrl),
                    HasCeneo = !string.IsNullOrEmpty(pData.OfferUrl),
                    MainUrlCeneo = pData.MainUrl,
                    MainUrlGoogle = pData.ImgUrlGoogle,
                    NameCeneo = pData.ExportedNameCeneo,
                    NameGoogle = pData.ProductNameInStoreForGoogle,
                    EanCeneo = pData.Ean,
                    EanGoogle = pData.EanGoogle,
                    LastGooglePrice = googlePrice,
                    LastCeneoPrice = ceneoPrice,
                    GoogleXMLPrice = pData.GoogleXMLPrice,
                    CeneoXMLPrice = pData.CeneoXMLPrice,
                    PriceDifference = diff,
                    ScrapId = latestScrapId
                };
                vmList.Add(vm);
            }

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;
            ViewBag.OnlyMismatch = onlyMismatch;
            ViewBag.OnlyGoogleXmlMismatch = onlyGoogleXmlMismatch;

            return View("~/Views/ManagerPanel/ProductMapping/MappedProducts.cshtml", vmList);
        }

        private class ProductPrices
        {
            public decimal? GooglePrice { get; set; }
            public decimal? CeneoPrice { get; set; }
        }

        public class MappedProductViewModel
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public int? ExternalId { get; set; }
            public string Url { get; set; }
            public string UrlCeneo { get; set; }
            public string UrlGoogle { get; set; }
            public bool IsScrapable { get; set; }
            public bool IsRejected { get; set; }
            public bool HasGoogle { get; set; }
            public bool HasCeneo { get; set; }
            public string MainUrlCeneo { get; set; }
            public string MainUrlGoogle { get; set; }
            public string NameCeneo { get; set; }
            public string NameGoogle { get; set; }
            public string EanCeneo { get; set; }
            public string EanGoogle { get; set; }
            public decimal? LastGooglePrice { get; set; }
            public decimal? LastCeneoPrice { get; set; }
            public decimal? GoogleXMLPrice { get; set; }
            public decimal? CeneoXMLPrice { get; set; }
            public decimal? PriceDifference { get; set; }
            public int ScrapId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveSelectedProducts(int storeId, [FromBody] List<int> productIds)
        {
            try
            {

                var allStoreProducts = await _context.Products
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                var productsToRemove = allStoreProducts
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToList();

                _context.Products.RemoveRange(productsToRemove);

                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MapProducts(int storeId)
        {

            var storeProducts = await _context.Products
                .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.ExportedNameCeneo))
                .ToListAsync();

            var groupedProducts = storeProducts
                .GroupBy(p => SimplifyName(p.ExportedNameCeneo))
                .ToList();

            foreach (var group in groupedProducts)
            {
                var productsInGroup = group.ToList();

                if (productsInGroup.Count > 1)
                {

                    var mainProduct = productsInGroup.First();

                    for (int i = 1; i < productsInGroup.Count; i++)
                    {
                        var duplicateProduct = productsInGroup[i];

                        MergeProductData(mainProduct, duplicateProduct);

                        _context.Products.Remove(duplicateProduct);
                    }

                    _context.Products.Update(mainProduct);
                }
                else
                {

                    continue;
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MappedProducts", new { storeId });
        }

        private string SimplifyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var simplifiedName = Regex.Replace(name, @"[^\w]", "").ToUpperInvariant();
            return simplifiedName;
        }
        private void MergeProductData(ProductClass mainProduct, ProductClass duplicateProduct)
        {

            if (string.IsNullOrEmpty(mainProduct.ProductName) && !string.IsNullOrEmpty(duplicateProduct.ProductName))
                mainProduct.ProductName = duplicateProduct.ProductName;

            if (string.IsNullOrEmpty(mainProduct.Category) && !string.IsNullOrEmpty(duplicateProduct.Category))
                mainProduct.Category = duplicateProduct.Category;

            if (string.IsNullOrEmpty(mainProduct.OfferUrl) && !string.IsNullOrEmpty(duplicateProduct.OfferUrl))
                mainProduct.OfferUrl = duplicateProduct.OfferUrl;

            if (!mainProduct.ExternalId.HasValue && duplicateProduct.ExternalId.HasValue)
                mainProduct.ExternalId = duplicateProduct.ExternalId;

            if (string.IsNullOrEmpty(mainProduct.CatalogNumber) && !string.IsNullOrEmpty(duplicateProduct.CatalogNumber))
                mainProduct.CatalogNumber = duplicateProduct.CatalogNumber;

            if (string.IsNullOrEmpty(mainProduct.Ean) && !string.IsNullOrEmpty(duplicateProduct.Ean))
                mainProduct.Ean = duplicateProduct.Ean;

            if (string.IsNullOrEmpty(mainProduct.MainUrl) && !string.IsNullOrEmpty(duplicateProduct.MainUrl))
                mainProduct.MainUrl = duplicateProduct.MainUrl;

            if (!mainProduct.ExternalPrice.HasValue && duplicateProduct.ExternalPrice.HasValue)
                mainProduct.ExternalPrice = duplicateProduct.ExternalPrice;

            if (string.IsNullOrEmpty(mainProduct.ExportedNameCeneo) && !string.IsNullOrEmpty(duplicateProduct.ExportedNameCeneo))
                mainProduct.ExportedNameCeneo = duplicateProduct.ExportedNameCeneo;

            if (string.IsNullOrEmpty(mainProduct.Url) && !string.IsNullOrEmpty(duplicateProduct.Url))
                mainProduct.Url = duplicateProduct.Url;

            if (string.IsNullOrEmpty(mainProduct.GoogleUrl) && !string.IsNullOrEmpty(duplicateProduct.GoogleUrl))
                mainProduct.GoogleUrl = duplicateProduct.GoogleUrl;

            if (string.IsNullOrEmpty(mainProduct.ProductNameInStoreForGoogle) && !string.IsNullOrEmpty(duplicateProduct.ProductNameInStoreForGoogle))
                mainProduct.ProductNameInStoreForGoogle = duplicateProduct.ProductNameInStoreForGoogle;

            if (string.IsNullOrEmpty(mainProduct.EanGoogle) && !string.IsNullOrEmpty(duplicateProduct.EanGoogle))
                mainProduct.EanGoogle = duplicateProduct.EanGoogle;

            if (string.IsNullOrEmpty(mainProduct.ImgUrlGoogle) && !string.IsNullOrEmpty(duplicateProduct.ImgUrlGoogle))
                mainProduct.ImgUrlGoogle = duplicateProduct.ImgUrlGoogle;

            if (!mainProduct.FoundOnGoogle.HasValue && duplicateProduct.FoundOnGoogle.HasValue)
                mainProduct.FoundOnGoogle = duplicateProduct.FoundOnGoogle;

            if (!mainProduct.IsScrapable && duplicateProduct.IsScrapable)
                mainProduct.IsScrapable = true;

            if (!mainProduct.IsRejected && duplicateProduct.IsRejected)
                mainProduct.IsRejected = true;

            if (!mainProduct.MarginPrice.HasValue && duplicateProduct.MarginPrice.HasValue)
                mainProduct.MarginPrice = duplicateProduct.MarginPrice;

            if (!mainProduct.CeneoXMLPrice.HasValue && duplicateProduct.CeneoXMLPrice.HasValue)
                mainProduct.CeneoXMLPrice = duplicateProduct.CeneoXMLPrice;

            if (!mainProduct.CeneoDeliveryXMLPrice.HasValue && duplicateProduct.CeneoDeliveryXMLPrice.HasValue)
                mainProduct.CeneoDeliveryXMLPrice = duplicateProduct.CeneoDeliveryXMLPrice;

            if (!mainProduct.GoogleXMLPrice.HasValue && duplicateProduct.GoogleXMLPrice.HasValue)
                mainProduct.GoogleXMLPrice = duplicateProduct.GoogleXMLPrice;

            if (!mainProduct.GoogleDeliveryXMLPrice.HasValue && duplicateProduct.GoogleDeliveryXMLPrice.HasValue)
                mainProduct.GoogleDeliveryXMLPrice = duplicateProduct.GoogleDeliveryXMLPrice;

            mainProduct.PriceHistories ??= new List<PriceHistoryClass>();
            foreach (var priceHistory in duplicateProduct.PriceHistories ?? Enumerable.Empty<PriceHistoryClass>())
            {

                if (!mainProduct.PriceHistories.Contains(priceHistory))
                {

                    mainProduct.PriceHistories.Add(priceHistory);
                }
            }

            mainProduct.ProductFlags ??= new List<ProductFlag>();
            foreach (var productFlag in duplicateProduct.ProductFlags ?? Enumerable.Empty<ProductFlag>())
            {

                if (!mainProduct.ProductFlags.Contains(productFlag))
                {

                    mainProduct.ProductFlags.Add(productFlag);
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrUpdateProductsFromProductMap(int storeId, bool addGooglePrices)
        {

            var mappedProducts = await _context.ProductMaps
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var existingProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            foreach (var mappedProduct in mappedProducts)
            {
                int? externalId = int.TryParse(mappedProduct.ExternalId, out var extId) ? extId : (int?)null;
                string url = mappedProduct.Url;

                var existingProduct = existingProducts
                    .FirstOrDefault(p => p.ExternalId == externalId && p.Url == url);

                if (existingProduct != null)
                {

                    MapProductFields(existingProduct, mappedProduct, addGooglePrices);
                    _context.Products.Update(existingProduct);
                }
                else
                {

                    var newProduct = new ProductClass
                    {
                        StoreId = storeId,
                        ExternalId = externalId,
                        Url = url
                    };

                    MapProductFields(newProduct, mappedProduct, addGooglePrices);

                    _context.Products.Add(newProduct);
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Produkty zostały pomyślnie utworzone lub zaktualizowane.";
            return RedirectToAction("MappedProducts", new { storeId });
        }

        private void MapProductFields(ProductClass product, ProductMap mappedProduct, bool addGooglePrices)
        {

            if (!product.ExternalId.HasValue && int.TryParse(mappedProduct.ExternalId, out var externalId))
            {
                product.ExternalId = externalId;
            }

            if (string.IsNullOrEmpty(product.Url))
            {
                product.Url = mappedProduct.Url;
            }

            if (!string.IsNullOrEmpty(mappedProduct.GoogleExportedName))
            {

                if (string.IsNullOrEmpty(product.ProductName))
                    product.ProductName = mappedProduct.GoogleExportedName;
                if (string.IsNullOrEmpty(product.ProductNameInStoreForGoogle))
                    product.ProductNameInStoreForGoogle = mappedProduct.GoogleExportedName;

                if (!string.IsNullOrEmpty(mappedProduct.ExportedName) && string.IsNullOrEmpty(product.ExportedNameCeneo))
                {
                    product.ExportedNameCeneo = mappedProduct.ExportedName;
                }
            }
            else if (!string.IsNullOrEmpty(mappedProduct.ExportedName))
            {

                if (string.IsNullOrEmpty(product.ProductName))
                    product.ProductName = mappedProduct.ExportedName;
                if (string.IsNullOrEmpty(product.ExportedNameCeneo))
                    product.ExportedNameCeneo = mappedProduct.ExportedName;
            }
            else
            {

                if (string.IsNullOrEmpty(product.ProductName))
                    product.ProductName = "Brak nazwy produktu";
            }

            if (!string.IsNullOrEmpty(mappedProduct.GoogleEan))
            {
                if (string.IsNullOrEmpty(product.EanGoogle))
                    product.EanGoogle = mappedProduct.GoogleEan;
                if (string.IsNullOrEmpty(product.Ean))
                    product.Ean = mappedProduct.GoogleEan;
            }
            else if (!string.IsNullOrEmpty(mappedProduct.Ean))
            {
                if (string.IsNullOrEmpty(product.Ean))
                    product.Ean = mappedProduct.Ean;
            }

            if (!string.IsNullOrEmpty(mappedProduct.GoogleImage))
            {
                if (string.IsNullOrEmpty(product.ImgUrlGoogle))
                    product.ImgUrlGoogle = mappedProduct.GoogleImage;
                if (string.IsNullOrEmpty(product.MainUrl))
                    product.MainUrl = mappedProduct.GoogleImage;
            }
            else if (!string.IsNullOrEmpty(mappedProduct.MainUrl))
            {
                if (string.IsNullOrEmpty(product.MainUrl))
                    product.MainUrl = mappedProduct.MainUrl;
            }

            if (addGooglePrices)
            {

                if (mappedProduct.GoogleXMLPrice.HasValue)
                {
                    product.GoogleXMLPrice = mappedProduct.GoogleXMLPrice;
                }

                if (mappedProduct.GoogleDeliveryXMLPrice.HasValue)
                {
                    product.GoogleDeliveryXMLPrice = mappedProduct.GoogleDeliveryXMLPrice;
                }
            }

            if (mappedProduct.CeneoXMLPrice.HasValue)
            {
                product.CeneoXMLPrice = mappedProduct.CeneoXMLPrice;
            }

            if (mappedProduct.CeneoDeliveryXMLPrice.HasValue)
            {
                product.CeneoDeliveryXMLPrice = mappedProduct.CeneoDeliveryXMLPrice;
            }

        }

        [HttpPost]
        public async Task<IActionResult> RemoveAllProductsForStore(int storeId)
        {
            try
            {

                var productsToRemove = await _context.Products
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                _context.Products.RemoveRange(productsToRemove);

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Wszystkie produkty dla StoreId={storeId} zostały usunięte.";
                return RedirectToAction("MappedProducts", new { storeId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Błąd przy usuwaniu produktów dla StoreId={storeId}: {ex.Message}";
                return RedirectToAction("MappedProducts", new { storeId });
            }
        }
    }
}