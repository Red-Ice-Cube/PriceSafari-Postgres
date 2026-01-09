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
using System.Diagnostics;

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

            var extendedInfoLookup = new Dictionary<int, decimal?>();
            if (latestScrapId > 0)
            {
                extendedInfoLookup = await _context.PriceHistoryExtendedInfos
                    .Where(e => e.ScrapHistoryId == latestScrapId)
                    .Select(e => new { e.ProductId, e.ExtendedDataApiPrice })
                    .ToDictionaryAsync(k => k.ProductId, v => v.ExtendedDataApiPrice);
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
                 p.CeneoXMLPrice,
                 p.ProducerCode,
             })
             .ToListAsync();

            var vmList = new List<MappedProductViewModel>();

            foreach (var pData in productsData)
            {
                var prices = priceLookup.TryGetValue(pData.ProductId, out var foundPrices) ? foundPrices : new ProductPrices();
                var googlePrice = prices.GooglePrice;
                var ceneoPrice = prices.CeneoPrice;

                // 2. NOWE: Pobieramy cenę API ze słownika
                var apiPrice = extendedInfoLookup.TryGetValue(pData.ProductId, out var extPrice) ? extPrice : null;

                decimal? diff = (googlePrice.HasValue && ceneoPrice.HasValue) ? googlePrice.Value - ceneoPrice.Value : (decimal?)null;

                // Logika filtrowania (bez zmian)
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
                    ScrapId = latestScrapId,
                    ProducerCode = pData.ProducerCode,
                    // 3. NOWE: Przypisanie ceny API
                    ExternalApiPrice = apiPrice
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
            public decimal? ExternalApiPrice { get; set; }

            public string? ProducerCode { get; set; }
            public string? GoogleProducerCode { get; set; }
            public string? CeneoProducerCode { get; set; }
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

            if (string.IsNullOrEmpty(mainProduct.ProducerCode) && !string.IsNullOrEmpty(duplicateProduct.ProducerCode))
            {
                mainProduct.ProducerCode = duplicateProduct.ProducerCode;
            }

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateEansFromProductMap(int storeId)
        {
            if (storeId <= 0)
            {
                TempData["ErrorMessage"] = "Nieprawidłowy identyfikator sklepu.";
                return RedirectToAction("Index");
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = $"Sklep o ID {storeId} nie został znaleziony.";
                return RedirectToAction("Index");
            }

            var productMaps = await _context.ProductMaps
                .Where(pm => pm.StoreId == storeId)
                .ToListAsync();

            if (!productMaps.Any())
            {
                TempData["InfoMessage"] = "Brak zmapowanych produktów (ProductMap) dla tego sklepu. Nie można zaktualizować EANów.";
                return RedirectToAction("MappedProducts", new { storeId });
            }

            var existingDbProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            if (!existingDbProducts.Any())
            {
                TempData["InfoMessage"] = "Brak produktów w bazie danych dla tego sklepu. Nie można zaktualizować EANów.";
                return RedirectToAction("MappedProducts", new { storeId });
            }

            int updatedCount = 0;
            var updateLog = new List<string>();
            var notFoundInDbLog = new List<string>();

            foreach (var mappedProduct in productMaps)
            {

                int? mappedExternalId = null;
                if (!string.IsNullOrEmpty(mappedProduct.ExternalId) && int.TryParse(mappedProduct.ExternalId, out var extId))
                {
                    mappedExternalId = extId;
                }
                string mappedUrl = mappedProduct.Url;

                ProductClass? dbProductToUpdate = null;

                if (mappedExternalId.HasValue && !string.IsNullOrWhiteSpace(mappedUrl))
                {
                    dbProductToUpdate = existingDbProducts.FirstOrDefault(p => p.ExternalId == mappedExternalId && p.Url == mappedUrl);
                }

                if (dbProductToUpdate == null && !string.IsNullOrWhiteSpace(mappedUrl))
                {

                    var potentialMatchesByUrl = existingDbProducts.Where(p => p.Url == mappedUrl).ToList();
                    if (potentialMatchesByUrl.Count == 1)
                    {
                        dbProductToUpdate = potentialMatchesByUrl.First();
                    }
                    else if (potentialMatchesByUrl.Count > 1)
                    {

                        updateLog.Add($"OSTRZEŻENIE: Znaleziono {potentialMatchesByUrl.Count} produktów w DB z URL '{mappedUrl}' dla ProductMap ID: {mappedProduct.ProductMapId}. Pomijam aktualizację EAN dla tego wpisu mapy.");
                        continue;
                    }
                }

                if (dbProductToUpdate != null)
                {
                    bool eanWasUpdated = false;
                    string originalEan = dbProductToUpdate.Ean ?? "brak";
                    string originalEanGoogle = dbProductToUpdate.EanGoogle ?? "brak";
                    string newEanSource = "";

                    if (!string.IsNullOrWhiteSpace(mappedProduct.GoogleEan))
                    {
                        if (dbProductToUpdate.EanGoogle != mappedProduct.GoogleEan)
                        {
                            dbProductToUpdate.EanGoogle = mappedProduct.GoogleEan;
                            eanWasUpdated = true;
                            newEanSource += "EanGoogle (map)";
                        }

                        if (string.IsNullOrWhiteSpace(dbProductToUpdate.Ean) || dbProductToUpdate.Ean != mappedProduct.GoogleEan)
                        {
                            if (dbProductToUpdate.Ean != mappedProduct.GoogleEan)
                            {
                                dbProductToUpdate.Ean = mappedProduct.GoogleEan;
                                eanWasUpdated = true;
                                newEanSource = string.IsNullOrEmpty(newEanSource) ? "Ean (z GoogleEan map)" : newEanSource + ", Ean (z GoogleEan map)";
                            }
                        }
                    }

                    else if (!string.IsNullOrWhiteSpace(mappedProduct.Ean))
                    {
                        if (dbProductToUpdate.Ean != mappedProduct.Ean)
                        {
                            dbProductToUpdate.Ean = mappedProduct.Ean;
                            eanWasUpdated = true;
                            newEanSource = "Ean (map)";
                        }

                    }

                    if (eanWasUpdated)
                    {
                        _context.Products.Update(dbProductToUpdate);
                        updatedCount++;
                        updateLog.Add($"Produkt ID: {dbProductToUpdate.ProductId} (Nazwa: {dbProductToUpdate.ProductName?.Substring(0, Math.Min(30, dbProductToUpdate.ProductName.Length))}...): " +
                                      $"EAN z '{originalEan}' na '{dbProductToUpdate.Ean}', EanGoogle z '{originalEanGoogle}' na '{dbProductToUpdate.EanGoogle}'. Źródło: {newEanSource}.");
                    }
                }
                else
                {
                    notFoundInDbLog.Add($"ProductMap ID: {mappedProduct.ProductMapId} (ExtID: {mappedProduct.ExternalId ?? "brak"}, URL: {mappedProduct.Url ?? "brak"}, NazwaMapy: {mappedProduct.ExportedName ?? mappedProduct.GoogleExportedName ?? "brak"}) nie znalazł dopasowania w produktach bazodanowych.");
                }
            }

            if (updatedCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Pomyślnie zaktualizowano EANy dla {updatedCount} produktów.";
                if (updateLog.Any()) TempData["UpdateDetailsLog"] = string.Join("<br>", updateLog);
            }
            else
            {
                TempData["InfoMessage"] = "Nie znaleziono produktów do aktualizacji EAN lub EANy były już poprawne.";
            }

            if (notFoundInDbLog.Any())
            {
                TempData["NotFoundInDbLog"] = string.Join("<br>", notFoundInDbLog);
                Debug.WriteLine("Produkty z ProductMap nieznalezione w bazie danych:");
                foreach (var log in notFoundInDbLog) Debug.WriteLine(log);
            }
            Debug.WriteLine("Log aktualizacji EAN:");
            foreach (var log in updateLog) Debug.WriteLine(log);

            return RedirectToAction("MappedProducts", new { storeId });
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

        [HttpPost]
        public async Task<IActionResult> RemoveStringFromProducerCodes(int storeId, string stringToRemove)
        {
            if (storeId <= 0)
            {
                TempData["ErrorMessage"] = "Nieprawidłowy identyfikator sklepu.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrEmpty(stringToRemove))
            {
                TempData["ErrorMessage"] = "Musisz podać ciąg znaków do usunięcia.";
                return RedirectToAction("MappedProducts", new { storeId });
            }

            try
            {

                var products = await _context.Products
                    .Where(p => p.StoreId == storeId && p.ProducerCode != null)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var product in products)
                {

                    if (product.ProducerCode.Contains(stringToRemove))
                    {

                        product.ProducerCode = product.ProducerCode.Replace(stringToRemove, "");
                        updatedCount++;
                    }
                }

                if (updatedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Pomyślnie usunięto ciąg '{stringToRemove}' z kodów producenta dla {updatedCount} produktów.";
                }
                else
                {
                    TempData["InfoMessage"] = $"Nie znaleziono produktów zawierających ciąg '{stringToRemove}' w kodzie producenta.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Wystąpił błąd podczas aktualizacji kodów producenta: {ex.Message}";
            }

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

            string? producerToSet = null;

            if (!string.IsNullOrEmpty(mappedProduct.GoogleExportedProducer))
            {
                producerToSet = mappedProduct.GoogleExportedProducer;
            }
            else if (!string.IsNullOrEmpty(mappedProduct.CeneoExportedProducer))
            {
                producerToSet = mappedProduct.CeneoExportedProducer;
            }
            if (!string.IsNullOrEmpty(producerToSet))
            {
                product.Producer = producerToSet;
            }

            if (!string.IsNullOrEmpty(mappedProduct.GoogleExportedProducerCode))
            {
                if (string.IsNullOrEmpty(product.ProducerCode))
                {
                    product.ProducerCode = mappedProduct.GoogleExportedProducerCode;
                }
            }
            else if (!string.IsNullOrEmpty(mappedProduct.CeneoExportedProducerCode))
            {
                if (string.IsNullOrEmpty(product.ProducerCode))
                {
                    product.ProducerCode = mappedProduct.CeneoExportedProducerCode;
                }
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
        public async Task<IActionResult> MappedProductsAllegro(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Sklep nie znaleziony.");

            var allegroProducts = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            // Wykrywanie duplikatów ID
            var duplicateIds = allegroProducts
                .Where(p => !string.IsNullOrEmpty(p.IdOnAllegro))
                .GroupBy(p => p.IdOnAllegro)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            var vmList = new List<MappedProductAllegroViewModel>();

            foreach (var p in allegroProducts)
            {
                var vm = new MappedProductAllegroViewModel
                {
                    AllegroProductId = p.AllegroProductId,
                    ProductName = p.AllegroProductName,
                    Url = p.AllegroOfferUrl,
                    Ean = p.AllegroEan,
                    MarginPrice = p.AllegroMarginPrice,
                    IsScrapable = p.IsScrapable,
                    IsRejected = p.IsRejected,
                    AddedDate = p.AddedDate,
                    // Nowe dane
                    IdOnAllegro = p.IdOnAllegro,
                    IsDuplicate = !string.IsNullOrEmpty(p.IdOnAllegro) && duplicateIds.Contains(p.IdOnAllegro)
                };
                vmList.Add(vm);
            }

            // Sortujemy tak, aby duplikaty były na górze i zgrupowane po ID
            vmList = vmList
                .OrderByDescending(x => x.IsDuplicate) // Najpierw duplikaty
                .ThenBy(x => x.IdOnAllegro)            // Potem grupowanie po ID
                .ThenByDescending(x => x.AddedDate)    // Reszta po dacie
                .ToList();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;

            return View("~/Views/ManagerPanel/ProductMapping/MappedProductsAllegro.cshtml", vmList);
        }



        [HttpPost]
        public async Task<IActionResult> RemoveSelectedAllegroProducts(int storeId, [FromBody] List<int> productIds)
        {
            if (productIds == null || !productIds.Any())
                return BadRequest(new { success = false, message = "Brak produktów do usunięcia." });

            // Używamy strategii wykonania dla transakcji (bezpieczeństwo przy EF Core)
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Konwersja listy ID na string do bezpiecznego użycia w SQL IN (...)
                    var idsString = string.Join(",", productIds);

                    // 1. Usuń z tabeli blokującej (BridgeItems)
                    // Musimy to zrobić SQL-em, bo EF Core może mieć problemy z kaskadą przy skomplikowanych zależnościach
                    await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM AllegroPriceBridgeItems WHERE AllegroProductId IN ({idsString})");

                    // 2. Usuń Flagi produktów
                    await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM ProductFlags WHERE AllegroProductId IN ({idsString})");

                    // 3. Usuń Extended Info (szczegóły scrapowania)
                    await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM AllegroPriceHistoryExtendedInfos WHERE AllegroProductId IN ({idsString})");

                    // 4. Usuń Historię Cen
                    await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM AllegroPriceHistories WHERE AllegroProductId IN ({idsString})");

                    // 5. Na końcu usuń same produkty Allegro
                    // Dodatkowo sprawdzamy StoreId dla bezpieczeństwa
                    var deletedCount = await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM AllegroProducts WHERE StoreId = {storeId} AND AllegroProductId IN ({idsString})");

                    await transaction.CommitAsync();

                    return Json(new { success = true, count = deletedCount, message = $"Pomyślnie usunięto {deletedCount} produktów." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    // Logujemy błąd wewnętrzny (opcjonalnie)
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    return Json(new { success = false, message = $"Błąd bazy danych: {msg}" });
                }
            });
        }


        public class MappedProductAllegroViewModel
        {
            public int AllegroProductId { get; set; }
            public string ProductName { get; set; }
            public string Url { get; set; }
            public string Ean { get; set; }
            public decimal? MarginPrice { get; set; }
            public bool IsScrapable { get; set; }
            public bool IsRejected { get; set; }
            public DateTime AddedDate { get; set; }

            // --- NOWE POLA ---
            public string IdOnAllegro { get; set; }
            public bool IsDuplicate { get; set; }
        }
    }
}