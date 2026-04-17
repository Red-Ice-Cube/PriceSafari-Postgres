using AngleSharp.Dom;
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

        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
            ViewBag.ProductCount = store.ProductsToScrap;
            ViewBag.StoreId = storeId;

            var flags = await _context.Flags
         .Where(f => !f.IsMarketplace && f.StoreId == storeId) // <--- TUTAJ POPRAWKA: dodano && f.StoreId == storeId
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

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
                      p.MainUrl,
                      p.GoogleUrl,
                      AddedDate = p.AddedDate,
                      FoundOnGoogleDate = p.FoundOnGoogleDate,
                      FoundOnCeneoDate = p.FoundOnCeneoDate,

                      FlagIds = p.ProductFlags.Select(pf => pf.FlagId).ToList()
                  })
                  .ToListAsync();

            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateScrapableProduct(int storeId, [FromBody] int productId)
        {
            var logs = new List<string>();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            logs.Add($"User ID: {userId}");

            try
            {

                var userStore = await _context.UserStores
                    .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

                if (userStore == null)
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
                {
                    logs.Add($"Inner Exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { success = false, message = "Internal Server Error", logs });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null)
            {
                return NotFound();
            }

            int? currentScrapableCount = store.Products.Count(p => p.IsScrapable);
            int? availableCount = store.ProductsToScrap - currentScrapableCount;

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            if (availableCount.HasValue && productsToUpdate.Count > availableCount.Value)
            {
                productsToUpdate = productsToUpdate.Take(availableCount.Value).ToList();
            }

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = true;
            }

            await _context.SaveChangesAsync();

            if (productsToUpdate.Count < productIds.Count)
            {
                return Json(new { success = true, message = $"Zaktualizowano {productsToUpdate.Count} z {productIds.Count} produktów. Przekroczono limit produktów do scrapowania." });
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ResetMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null)
            {
                return NotFound();
            }

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = false;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

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

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

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
                var importRows = await ParseProductExcelFile(uploadedFile);

                if (importRows == null || !importRows.Any())
                {
                    TempData["ErrorMessage"] = "Plik nie zawiera poprawnych danych.";
                    return RedirectToAction("ProductList", new { storeId });
                }

                // --- LOGIKA FLAG ---
                var distinctFlagNames = importRows
                    .SelectMany(r => r.FlagNames)
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                var existingFlags = await _context.Flags
                    .Where(f => f.StoreId == storeId && !f.IsMarketplace)
                    .ToListAsync();

                var flagsToCreate = new List<FlagsClass>();
                foreach (var flagName in distinctFlagNames)
                {
                    if (!existingFlags.Any(f => f.FlagName.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
                    {
                        flagsToCreate.Add(new FlagsClass
                        {
                            StoreId = storeId,
                            FlagName = flagName,
                            FlagColor = GenerateRandomColorProduct(),
                            IsMarketplace = false
                        });
                    }
                }

                if (flagsToCreate.Any())
                {
                    _context.Flags.AddRange(flagsToCreate);
                    await _context.SaveChangesAsync();
                    existingFlags.AddRange(flagsToCreate);
                }

                var flagMap = existingFlags.ToDictionary(f => f.FlagName.ToUpperInvariant(), f => f.FlagId);

                // --- AKTUALIZACJA PRODUKTÓW ---
                var products = await _context.Products
                    .Include(p => p.ProductFlags)
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var row in importRows)
                {
                    var targets = new List<ProductClass>();

                    // Priorytet 1: ExternalId
                    if (!string.IsNullOrEmpty(row.ExternalId) &&
                        int.TryParse(row.ExternalId, out var extIdInt))
                    {
                        var found = products.FirstOrDefault(p => p.ExternalId == extIdInt);
                        if (found != null) targets.Add(found);
                    }

                    // Priorytet 2: EAN
                    if (!targets.Any() && !string.IsNullOrEmpty(row.Ean))
                    {
                        var byEan = products.Where(p => p.Ean == row.Ean).ToList();
                        targets.AddRange(byEan);
                    }

                    foreach (var product in targets)
                    {
                        bool isModified = false;

                        if (row.Price.HasValue && product.MarginPrice != row.Price.Value)
                        {
                            product.MarginPrice = row.Price.Value;
                            product.MarginPriceUpdatedDate = DateTime.UtcNow;
                            isModified = true;
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
                TempData["SuccessMessage"] = $"Zaktualizowano {updatedCount} produktów (Ceny, SKU, Flagi).";
                return RedirectToAction("ProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas SetMargins");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("ProductList", new { storeId });
            }
        }

        private async Task<List<ProductImportRow>> ParseProductExcelFile(IFormFile file)
        {
            var list = new List<ProductImportRow>();

            using (var stream = new MemoryStream())
            {
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
                    else if (txt == "CENA" || txt == "PRICE" || txt == "CENAZAKUPU") priceCol = c;
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
            }

            return list;
        }

        [HttpGet]
        public async Task<IActionResult> DownloadProductSkeleton(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

            var products = await _context.Products
                .Include(p => p.ProductFlags)
                    .ThenInclude(pf => pf.Flag)
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            using (var workbook = new XSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Produkty");

                var headerRow = sheet.CreateRow(0);
                headerRow.CreateCell(0).SetCellValue("ID");           // ExternalId
                headerRow.CreateCell(1).SetCellValue("EAN");          // EAN
                headerRow.CreateCell(2).SetCellValue("SKU");          // CatalogNumber
                headerRow.CreateCell(3).SetCellValue("CENA");         // MarginPrice
                headerRow.CreateCell(4).SetCellValue("FLAGI");        // Flagi po przecinku

                var headerStyle = workbook.CreateCellStyle();
                var font = workbook.CreateFont();
                font.IsBold = true;
                headerStyle.SetFont(font);
                for (int i = 0; i < 5; i++) headerRow.GetCell(i).CellStyle = headerStyle;

                int rowIndex = 1;
                foreach (var product in products)
                {
                    var row = sheet.CreateRow(rowIndex++);

                    // ID — ExternalId jako liczba lub pusty string
                    if (product.ExternalId.HasValue)
                        row.CreateCell(0).SetCellValue(product.ExternalId.Value);
                    else
                        row.CreateCell(0).SetCellValue("");

                    row.CreateCell(1).SetCellValue(product.Ean ?? "");
                    row.CreateCell(2).SetCellValue(product.CatalogNumber ?? "");

                    if (product.MarginPrice.HasValue)
                        row.CreateCell(3).SetCellValue((double)product.MarginPrice.Value);
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

                sheet.SetColumnWidth(0, 20 * 256);  // ID
                sheet.SetColumnWidth(1, 18 * 256);  // EAN
                sheet.SetColumnWidth(2, 16 * 256);  // SKU
                sheet.SetColumnWidth(3, 14 * 256);  // CENA
                sheet.SetColumnWidth(4, 30 * 256);  // FLAGI

                using (var stream = new MemoryStream())
                {
                    workbook.Write(stream);
                    var content = stream.ToArray();
                    var fileName = $"Produkty_Szkielet_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }


        private string GenerateRandomColorProduct()
        {
            var random = new Random();
            return String.Format("#{0:X6}", random.Next(0x1000000));
        }


        private string GetCellValue(ICell cell, IFormulaEvaluator evaluator)
        {
            if (cell == null) return null;

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllPurchasePrices(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("Attempting to clear all purchase prices for StoreId={StoreId} by UserId={UserId}", storeId, userId);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                _logger.LogWarning("User {UserId} does not have access to StoreId {StoreId} for ClearAllPurchasePrices.", userId, storeId);
                return Forbid();
            }

            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
            {
                _logger.LogWarning("Store with StoreId {StoreId} not found for ClearAllPurchasePrices.", storeId);
                return NotFound(new { success = false, message = "Sklep nie został znaleziony." });
            }

            try
            {

                var productsInStore = await _context.Products
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int clearedCount = 0;
                if (productsInStore.Any())
                {
                    foreach (var product in productsInStore)
                    {
                        if (product.MarginPrice != null)
                        {
                            product.MarginPrice = null;
                            product.MarginPriceUpdatedDate = null; // <--- DODAJ TO (czyścimy datę)
                            clearedCount++;
                        }
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully cleared purchase prices for {ClearedCount} products in StoreId={StoreId}.", clearedCount, storeId);
                    TempData["SuccessMessage"] = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów.";
                    return Json(new { success = true, message = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów." });
                }
                else
                {
                    _logger.LogInformation("No products found in StoreId={StoreId} to clear purchase prices.", storeId);
                    TempData["SuccessMessage"] = "Brak produktów w sklepie, nie usunięto żadnych cen zakupu.";
                    return Json(new { success = true, message = "Brak produktów w sklepie, nie usunięto żadnych cen zakupu." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ClearAllPurchasePrices for StoreId={StoreId}.", storeId);

                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera podczas usuwania cen zakupu." });
            }
        }

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
            {
                return BadRequest(new { success = false, message = "Nieprawidłowe dane." });
            }

            if (model.NewPrice.HasValue && model.NewPrice < 0)
            {
                return BadRequest(new { success = false, message = "Cena nie może być ujemna." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
            {
                return Forbid();
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == model.ProductId && p.StoreId == storeId);
            if (product == null)
            {
                return NotFound(new { success = false, message = "Produkt nie został znaleziony." });
            }

            try
            {
                // --- ZMIANA TUTAJ: Sprawdzamy czy cena się zmieniła ---
                if (product.MarginPrice != model.NewPrice)
                {
                    product.MarginPrice = model.NewPrice;
                    product.MarginPriceUpdatedDate = DateTime.UtcNow; // Ustawiamy datę zmiany

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Zaktualizowano cenę zakupu dla produktu ID={ProductId} na {NewPrice} przez użytkownika ID={UserId}",
                                     model.ProductId, model.NewPrice.HasValue ? model.NewPrice.Value.ToString() : "NULL", userId);
                }
                else
                {
                    _logger.LogInformation("Cena zakupu dla produktu ID={ProductId} jest taka sama. Nie zmieniono daty.", model.ProductId);
                }
                // -----------------------------------------------------

                return Json(new { success = true, message = "Cena zakupu została zaktualizowana." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas aktualizacji ceny zakupu dla produktu ID={ProductId}", model.ProductId);
                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera." });
            }
        }
    }
}