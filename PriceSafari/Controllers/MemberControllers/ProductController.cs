using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Globalization;
using System.Security.Claims;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class ProductController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<ProductController> _logger;

        public ProductController(PriceSafariContext context, ILogger<ProductController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        private async Task<bool> HasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
        }

        private async Task<bool> CurrentUserUsesProducerView()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return false;

            // UWAGA: jeśli pole w PriceSafariUser nazywa się inaczej (np. UseProducerView
            // jako wspólne dla marketplace i comparison), podmień tylko tę jedną nazwę.
            return await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UseProducerViewForPriceComparison)
                .FirstOrDefaultAsync();
        }

        // ════════════════════════════════════════════════════════════════
        //  STORE LIST
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> StoreList()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStoresQuery = _context.UserStores
                .Where(us => us.UserId == userId)
                .Select(us => us.StoreClass);

            var storeIds = await userStoresQuery.Select(s => s.StoreId).ToListAsync();

            var scrapableCounts = await _context.Products
                .Where(p => storeIds.Contains(p.StoreId) && p.IsScrapable)
                .GroupBy(p => p.StoreId)
                .Select(g => new { StoreId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StoreId, x => x.Count);

            var allegroScrapableCounts = await _context.AllegroProducts
                .Where(p => storeIds.Contains(p.StoreId) && p.IsScrapable)
                .GroupBy(p => p.StoreId)
                .Select(g => new { StoreId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StoreId, x => x.Count);

            var stores = await userStoresQuery.ToListAsync();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,

                ProductCount = scrapableCounts.TryGetValue(store.StoreId, out var count) ? count : 0,
                AllowedProducts = store.ProductsToScrap,
                AllegroProductCount = allegroScrapableCounts.TryGetValue(store.StoreId, out var allegroCount) ? allegroCount : 0,
                AllegroAllowedProducts = store.ProductsToScrapAllegro,

                OnCeneo = store.OnCeneo,
                OnGoogle = store.OnGoogle,
                OnAllegro = store.OnAllegro
            }).ToList();

            return View("~/Views/Panel/Product/StoreList.cshtml", storeDetails);
        }

        // ════════════════════════════════════════════════════════════════
        //  PRODUCT LIST + LISTING
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            bool isProducerView = await CurrentUserUsesProducerView();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
            ViewBag.ProductCount = store.ProductsToScrap;
            ViewBag.StoreId = storeId;
            ViewBag.IsProducerView = isProducerView;

            var flags = await _context.Flags
                .Where(f => !f.IsMarketplace && f.StoreId == storeId)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor
                })
                .ToListAsync();

            ViewBag.Flags = flags;

            return View("~/Views/Panel/Product/ProductList.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();

            var products = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Include(p => p.ProductFlags)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.Category,
                    p.Producer,
                    p.OfferUrl,
                    p.IsScrapable,
                    p.IsRejected,
                    p.Url,
                    p.Ean,
                    p.ExternalId,
                    p.CatalogNumber,
                    p.MarginPrice,
                    p.MapPrice,
                    p.MainUrl,
                    p.GoogleUrl,
                    p.AddedDate,
                    p.FoundOnGoogleDate,
                    p.FoundOnCeneoDate,
                    FlagIds = p.ProductFlags.Select(pf => pf.FlagId).ToList()
                })
                .ToListAsync();

            return Json(new
            {
                useProducerView = isProducerView,
                products
            });
        }

        // ════════════════════════════════════════════════════════════════
        //  MONITORING (scrapable) — identycznie w obu trybach
        // ════════════════════════════════════════════════════════════════

        [HttpPost]
        public async Task<IActionResult> UpdateScrapableProduct(int storeId, [FromBody] int productId)
        {
            var logs = new List<string>();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            logs.Add($"User ID: {userId}");

            try
            {
                if (!await HasAccessToStore(storeId))
                {
                    logs.Add("User store not found.");
                    return Json(new { success = false, message = "User store not found.", logs });
                }

                var product = await _context.Products.Include(p => p.Store)
                    .FirstOrDefaultAsync(p => p.ProductId == productId && p.StoreId == storeId);
                if (product == null)
                {
                    logs.Add("Product not found.");
                    return Json(new { success = false, message = "Product not found.", logs });
                }

                logs.Add($"Product ID: {product.ProductId}, Store ID: {product.StoreId}");

                var scrapableCount = await _context.Products
                    .CountAsync(p => p.StoreId == storeId && p.IsScrapable);
                logs.Add($"Scrapable count: {scrapableCount}, Store ProductsToScrap: {product.Store.ProductsToScrap}");

                if (!product.IsScrapable && scrapableCount >= product.Store.ProductsToScrap)
                {
                    logs.Add("Scrapable product limit exceeded.");
                    return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania.", logs });
                }

                product.IsScrapable = !product.IsScrapable;
                await _context.SaveChangesAsync();
                logs.Add("Product updated successfully.");

                return Json(new { success = true, logs, newIsScrapable = product.IsScrapable });
            }
            catch (Exception ex)
            {
                logs.Add($"Exception: {ex.Message}");
                if (ex.InnerException != null)
                    logs.Add($"Inner Exception: {ex.InnerException.Message}");
                return StatusCode(500, new { success = false, message = "Internal Server Error", logs });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            int? currentScrapableCount = store.Products.Count(p => p.IsScrapable);
            int? availableCount = store.ProductsToScrap - currentScrapableCount;

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            if (availableCount.HasValue && productsToUpdate.Count > availableCount.Value)
                productsToUpdate = productsToUpdate.Take(availableCount.Value).ToList();

            foreach (var product in productsToUpdate)
                product.IsScrapable = true;

            await _context.SaveChangesAsync();

            if (productsToUpdate.Count < productIds.Count)
                return Json(new { success = true, message = $"Zaktualizowano {productsToUpdate.Count} z {productIds.Count} produktów. Przekroczono limit produktów do scrapowania." });

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ResetMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            foreach (var product in productsToUpdate)
                product.IsScrapable = false;

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ════════════════════════════════════════════════════════════════
        //  CENA INLINE — dynamicznie zakup (standard) / MAP (producent)
        // ════════════════════════════════════════════════════════════════

        public class UpdatePurchasePriceViewModel
        {
            public int ProductId { get; set; }
            public decimal? NewPrice { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePurchasePrice(int storeId, [FromBody] UpdatePurchasePriceViewModel model)
        {
            if (model == null)
                return BadRequest(new { success = false, message = "Nieprawidłowe dane." });

            if (model.NewPrice.HasValue && model.NewPrice < 0)
                return BadRequest(new { success = false, message = "Cena nie może być ujemna." });

            if (!await HasAccessToStore(storeId)) return Forbid();

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == model.ProductId && p.StoreId == storeId);
            if (product == null)
                return NotFound(new { success = false, message = "Produkt nie został znaleziony." });

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            try
            {
                bool changed = false;

                if (isProducerView)
                {
                    if (product.MapPrice != model.NewPrice)
                    {
                        product.MapPrice = model.NewPrice;
                        product.MapPriceUpdatedDate = DateTime.UtcNow;
                        changed = true;
                    }
                }
                else
                {
                    if (product.MarginPrice != model.NewPrice)
                    {
                        product.MarginPrice = model.NewPrice;
                        product.MarginPriceUpdatedDate = DateTime.UtcNow;
                        changed = true;
                    }
                }

                if (changed) await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Zaktualizowano cenę {Label} dla produktu ID={ProductId} na {NewPrice}",
                    priceLabel, model.ProductId,
                    model.NewPrice.HasValue ? model.NewPrice.Value.ToString(CultureInfo.InvariantCulture) : "NULL");

                return Json(new { success = true, message = $"Cena {priceLabel} została zaktualizowana." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas aktualizacji ceny {Label} dla produktu ID={ProductId}", priceLabel, model.ProductId);
                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllPurchasePrices(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
                return NotFound(new { success = false, message = "Sklep nie został znaleziony." });

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            try
            {
                var productsInStore = await _context.Products
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int clearedCount = 0;
                foreach (var product in productsInStore)
                {
                    if (isProducerView)
                    {
                        if (product.MapPrice != null)
                        {
                            product.MapPrice = null;
                            product.MapPriceUpdatedDate = null;
                            clearedCount++;
                        }
                    }
                    else
                    {
                        if (product.MarginPrice != null)
                        {
                            product.MarginPrice = null;
                            product.MarginPriceUpdatedDate = null;
                            clearedCount++;
                        }
                    }
                }

                if (clearedCount > 0) await _context.SaveChangesAsync();

                _logger.LogInformation("Usunięto ceny {Label} dla {ClearedCount} produktów w StoreId={StoreId}",
                    priceLabel, clearedCount, storeId);

                var message = clearedCount > 0
                    ? $"Pomyślnie usunięto ceny {priceLabel} dla {clearedCount} produktów."
                    : $"Brak produktów z ustaloną ceną {priceLabel} — nic nie usunięto.";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd ClearAllPurchasePrices dla StoreId={StoreId}", storeId);
                return StatusCode(500, new { success = false, message = $"Wystąpił wewnętrzny błąd serwera podczas usuwania cen {priceLabel}." });
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  EXPORT SKELETONU — dynamicznie CENA / CENA MAP
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> DownloadProductSkeleton(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceHeader = isProducerView ? "CENA MAP" : "CENA";
            string fileSuffix = isProducerView ? "MAP" : "Zakup";

            var products = await _context.Products
                .Include(p => p.ProductFlags)
                    .ThenInclude(pf => pf.Flag)
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            using var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Produkty");

            var headerRow = sheet.CreateRow(0);
            headerRow.CreateCell(0).SetCellValue("ID");
            headerRow.CreateCell(1).SetCellValue("EAN");
            headerRow.CreateCell(2).SetCellValue("SKU");
            headerRow.CreateCell(3).SetCellValue(priceHeader);
            headerRow.CreateCell(4).SetCellValue("FLAGI");

            var headerStyle = workbook.CreateCellStyle();
            var font = workbook.CreateFont();
            font.IsBold = true;
            headerStyle.SetFont(font);
            for (int i = 0; i < 5; i++) headerRow.GetCell(i).CellStyle = headerStyle;

            int rowIndex = 1;
            foreach (var product in products)
            {
                var row = sheet.CreateRow(rowIndex++);

                if (product.ExternalId.HasValue)
                    row.CreateCell(0).SetCellValue(product.ExternalId.Value);
                else
                    row.CreateCell(0).SetCellValue("");

                row.CreateCell(1).SetCellValue(product.Ean ?? "");
                row.CreateCell(2).SetCellValue(product.CatalogNumber ?? "");

                decimal? priceValue = isProducerView ? product.MapPrice : product.MarginPrice;
                if (priceValue.HasValue)
                    row.CreateCell(3).SetCellValue((double)priceValue.Value);
                else
                    row.CreateCell(3).SetCellValue("");

                if (product.ProductFlags != null && product.ProductFlags.Any(pf => pf.ProductId.HasValue))
                {
                    var flagNames = product.ProductFlags
                        .Where(pf => pf.ProductId.HasValue && pf.Flag != null)
                        .Select(pf => pf.Flag.FlagName)
                        .Where(n => !string.IsNullOrEmpty(n));
                    row.CreateCell(4).SetCellValue(string.Join(", ", flagNames));
                }
                else
                {
                    row.CreateCell(4).SetCellValue("");
                }
            }

            sheet.SetColumnWidth(0, 20 * 256);
            sheet.SetColumnWidth(1, 18 * 256);
            sheet.SetColumnWidth(2, 16 * 256);
            sheet.SetColumnWidth(3, 14 * 256);
            sheet.SetColumnWidth(4, 30 * 256);

            using var stream = new MemoryStream();
            workbook.Write(stream);
            var content = stream.ToArray();
            var fileName = $"Produkty_Szkielet_{fileSuffix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ════════════════════════════════════════════════════════════════
        //  IMPORT (Excel) — dynamicznie CENA / CENA MAP
        // ════════════════════════════════════════════════════════════════

        private class ProductImportRow
        {
            public string Ean { get; set; }
            public string ExternalId { get; set; }
            public decimal? Price { get; set; }
            public string CatalogNumber { get; set; }
            public List<string> FlagNames { get; set; } = new List<string>();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetMargins(int storeId, IFormFile uploadedFile)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("SetMargins: StoreId={StoreId}, UserId={UserId}, File={File}", storeId, userId, uploadedFile?.FileName);

            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                TempData["ErrorMessage"] = "Proszę wgrać poprawny plik Excel.";
                return RedirectToAction("ProductList", new { storeId });
            }
            if (uploadedFile.Length > 10 * 1024 * 1024)
            {
                TempData["ErrorMessage"] = "Wielkość pliku nie może przekraczać 10 MB.";
                return RedirectToAction("ProductList", new { storeId });
            }
            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
            {
                TempData["ErrorMessage"] = "Niewspierany format pliku.";
                return RedirectToAction("ProductList", new { storeId });
            }
            var mime = uploadedFile.ContentType;
            if (mime != "application/vnd.ms-excel" &&
                mime != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                TempData["ErrorMessage"] = "Niewspierany typ MIME.";
                return RedirectToAction("ProductList", new { storeId });
            }

            try
            {
                var importRows = await ParseProductExcelFile(uploadedFile, isProducerView);

                if (importRows == null || !importRows.Any())
                {
                    TempData["ErrorMessage"] = $"Plik nie zawiera poprawnych danych lub nie znaleziono kolumny z ceną {priceLabel}.";
                    return RedirectToAction("ProductList", new { storeId });
                }

                // Flagi — znajdź istniejące lub utwórz nowe
                var distinctFlagNames = importRows
                    .SelectMany(r => r.FlagNames)
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                var existingFlags = await _context.Flags
                    .Where(f => f.StoreId == storeId && !f.IsMarketplace)
                    .ToListAsync();

                var flagsToCreate = distinctFlagNames
                    .Where(name => !existingFlags.Any(f => f.FlagName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .Select(name => new FlagsClass
                    {
                        StoreId = storeId,
                        FlagName = name,
                        FlagColor = GenerateRandomColorProduct(),
                        IsMarketplace = false
                    })
                    .ToList();

                if (flagsToCreate.Any())
                {
                    _context.Flags.AddRange(flagsToCreate);
                    await _context.SaveChangesAsync();
                    existingFlags.AddRange(flagsToCreate);
                }

                var flagMap = existingFlags.ToDictionary(f => f.FlagName.ToUpperInvariant(), f => f.FlagId);

                var products = await _context.Products
                    .Include(p => p.ProductFlags)
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var row in importRows)
                {
                    var targets = new List<ProductClass>();

                    if (!string.IsNullOrEmpty(row.ExternalId) &&
                        int.TryParse(row.ExternalId, out var extIdInt))
                    {
                        var found = products.FirstOrDefault(p => p.ExternalId == extIdInt);
                        if (found != null) targets.Add(found);
                    }

                    if (!targets.Any() && !string.IsNullOrEmpty(row.Ean))
                    {
                        var byEan = products.Where(p => p.Ean == row.Ean).ToList();
                        targets.AddRange(byEan);
                    }

                    foreach (var product in targets)
                    {
                        bool isModified = false;

                        // Cena — kierunek zależny od widoku
                        if (row.Price.HasValue)
                        {
                            if (isProducerView)
                            {
                                if (product.MapPrice != row.Price.Value)
                                {
                                    product.MapPrice = row.Price.Value;
                                    product.MapPriceUpdatedDate = DateTime.UtcNow;
                                    isModified = true;
                                }
                            }
                            else
                            {
                                if (product.MarginPrice != row.Price.Value)
                                {
                                    product.MarginPrice = row.Price.Value;
                                    product.MarginPriceUpdatedDate = DateTime.UtcNow;
                                    isModified = true;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(row.CatalogNumber) && product.CatalogNumber != row.CatalogNumber)
                        {
                            product.CatalogNumber = row.CatalogNumber;
                            isModified = true;
                        }

                        if (row.FlagNames != null && row.FlagNames.Any())
                        {
                            var targetFlagIds = row.FlagNames
                                .Select(fn => fn.Trim().ToUpperInvariant())
                                .Where(fn => flagMap.ContainsKey(fn))
                                .Select(fn => flagMap[fn])
                                .ToList();

                            var currentFlagIds = product.ProductFlags
                                .Where(pf => pf.ProductId.HasValue)
                                .Select(pf => pf.FlagId)
                                .ToList();

                            if (!new HashSet<int>(currentFlagIds).SetEquals(targetFlagIds))
                            {
                                var toRemove = product.ProductFlags
                                    .Where(pf => pf.ProductId.HasValue)
                                    .ToList();
                                foreach (var f in toRemove) _context.ProductFlags.Remove(f);

                                foreach (var flagId in targetFlagIds)
                                {
                                    _context.ProductFlags.Add(new ProductFlag
                                    {
                                        ProductId = product.ProductId,
                                        FlagId = flagId
                                    });
                                }
                                isModified = true;
                            }
                        }

                        if (isModified) updatedCount++;
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Zaktualizowano {updatedCount} produktów (Ceny {priceLabel}, SKU, Flagi).";
                return RedirectToAction("ProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas SetMargins");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("ProductList", new { storeId });
            }
        }

        private async Task<List<ProductImportRow>> ParseProductExcelFile(IFormFile file, bool isProducerView)
        {
            var list = new List<ProductImportRow>();

            // Akceptowane nagłówki ceny zależą od trybu
            var priceHeaderCandidates = isProducerView
                ? new HashSet<string> { "MAP", "CENAMAP", "MAPPRICE", "CENAMINIMALNA", "MINIMALNACENA" }
                : new HashSet<string> { "CENA", "PRICE", "CENAZAKUPU" };

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            IWorkbook workbook = extension == ".xls"
                ? (IWorkbook)new HSSFWorkbook(stream)
                : new XSSFWorkbook(stream);

            var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
            var sheet = workbook.GetSheetAt(0);
            if (sheet == null) return null;

            var headerRow = sheet.GetRow(0);
            if (headerRow == null) return null;

            int eanCol = -1, priceCol = -1, externalIdCol = -1, catalogNumberCol = -1, flagCol = -1;

            for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
            {
                var txt = GetCellValue(headerRow.GetCell(c), evaluator)?.Trim().ToUpperInvariant().Replace(" ", "");
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt == "EAN" || txt == "KODEAN") eanCol = c;
                else if (priceHeaderCandidates.Contains(txt)) priceCol = c;
                else if (txt == "ID" || txt == "EXTERNALID" || txt == "ZEWNETRZNE_ID") externalIdCol = c;
                else if (txt == "SKU" || txt == "SYGNATURA" || txt == "CATALOGNUMBER") catalogNumberCol = c;
                else if (txt == "FLAGA" || txt == "FLAGI" || txt == "FLAGS") flagCol = c;
            }

            if (eanCol < 0 && externalIdCol < 0)
            {
                _logger.LogError("Brak kolumn identyfikacyjnych (EAN lub ID)");
                return null;
            }

            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                var item = new ProductImportRow();

                if (eanCol >= 0) item.Ean = GetCellValue(row.GetCell(eanCol), evaluator)?.Trim();
                if (externalIdCol >= 0) item.ExternalId = GetCellValue(row.GetCell(externalIdCol), evaluator)?.Trim();

                if (string.IsNullOrEmpty(item.Ean) && string.IsNullOrEmpty(item.ExternalId)) continue;

                if (priceCol >= 0)
                {
                    var priceTxt = GetCellValue(row.GetCell(priceCol), evaluator)?.Trim().Replace(",", ".");
                    if (decimal.TryParse(priceTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                        item.Price = p;
                }

                if (catalogNumberCol >= 0)
                    item.CatalogNumber = GetCellValue(row.GetCell(catalogNumberCol), evaluator)?.Trim();

                if (flagCol >= 0)
                {
                    var flagsRaw = GetCellValue(row.GetCell(flagCol), evaluator)?.Trim();
                    if (!string.IsNullOrEmpty(flagsRaw))
                    {
                        foreach (var part in flagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                            item.FlagNames.Add(part.Trim());
                    }
                }

                list.Add(item);
            }

            return list;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS — kolory + odczyt komórek Excela
        // ════════════════════════════════════════════════════════════════

        private string GenerateRandomColorProduct()
        {
            var random = new Random();
            return string.Format("#{0:X6}", random.Next(0x1000000));
        }

        private string GetCellValue(ICell cell, IFormulaEvaluator evaluator)
        {
            if (cell == null) return null;

            // Formuła z zewnętrznym odniesieniem ([file]) — używamy cached
            if (cell.CellType == CellType.Formula &&
                !string.IsNullOrEmpty(cell.CellFormula) &&
                cell.CellFormula.Contains("["))
            {
                _logger.LogDebug("Formula external reference detected ('{Formula}'), using cached result.", cell.CellFormula);
                return cell.CachedFormulaResultType switch
                {
                    CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
                    CellType.String => cell.StringCellValue,
                    CellType.Boolean => cell.BooleanCellValue.ToString(),
                    _ => null
                };
            }

            try
            {
                if (cell.CellType == CellType.Formula)
                {
                    var eval = evaluator.Evaluate(cell);
                    if (eval != null)
                    {
                        return eval.CellType switch
                        {
                            CellType.Numeric => eval.NumberValue.ToString(CultureInfo.InvariantCulture),
                            CellType.String => eval.StringValue,
                            CellType.Boolean => eval.BooleanValue.ToString(),
                            _ => null
                        };
                    }

                    _logger.LogDebug("Evaluate zwróciło null dla formuły '{Formula}', używam cached.", cell.CellFormula);
                    return cell.CachedFormulaResultType switch
                    {
                        CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
                        CellType.String => cell.StringCellValue,
                        CellType.Boolean => cell.BooleanCellValue.ToString(),
                        _ => null
                    };
                }

                return cell.CellType switch
                {
                    CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
                    CellType.String => cell.StringCellValue,
                    CellType.Boolean => cell.BooleanCellValue.ToString(),
                    _ => cell.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Błąd Evaluate() dla komórki formuły '{Formula}', używam cached.", cell.CellFormula);
                if (cell.CellType == CellType.Formula)
                {
                    return cell.CachedFormulaResultType switch
                    {
                        CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
                        CellType.String => cell.StringCellValue,
                        CellType.Boolean => cell.BooleanCellValue.ToString(),
                        _ => null
                    };
                }
                return null;
            }
        }
    }
}