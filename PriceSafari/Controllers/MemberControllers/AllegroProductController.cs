//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using NPOI.HSSF.UserModel;
//using NPOI.SS.UserModel;
//using NPOI.XSSF.UserModel;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.ViewModels;
//using System.Globalization;
//using System.Security.Claims;

//namespace PriceSafari.Controllers
//{
//    [Authorize(Roles = "Admin, Manager, Member")]
//    public class AllegroProductController : Controller
//    {
//        private readonly PriceSafariContext _context;
//        private readonly ILogger<AllegroProductController> _logger;

//        public AllegroProductController(PriceSafariContext context, ILogger<AllegroProductController> logger)
//        {
//            _context = context;
//            _logger = logger;
//        }

//        [HttpGet]
//        public async Task<IActionResult> AllegroProductList(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

//            if (userStore == null)
//            {
//                return Forbid();
//            }

//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null) return NotFound();

//            ViewBag.StoreName = store.StoreName;
//            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
//            ViewBag.ProductCount = store.ProductsToScrapAllegro;
//            ViewBag.StoreId = storeId;

//            var flags = await _context.Flags
//                .Where(f => f.IsMarketplace && f.StoreId == storeId)
//                .Select(f => new FlagViewModel
//                {
//                    FlagId = f.FlagId,
//                    FlagName = f.FlagName,
//                    FlagColor = f.FlagColor
//                })
//                .ToListAsync();

//            ViewBag.Flags = flags;

//            return View("~/Views/Panel/Product/AllegroProductList.cshtml");
//        }

//        [HttpGet]
//        public async Task<IActionResult> GetAllegroProducts(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

//            if (userStore == null)
//            {
//                return Forbid();
//            }

//            var products = await _context.AllegroProducts
//                .Where(p => p.StoreId == storeId)
//                .Include(p => p.ProductFlags)
//                .Select(p => new
//                {
//                    p.AllegroProductId,
//                    p.AllegroProductName,
//                    p.AllegroOfferUrl,
//                    p.IdOnAllegro,
//                    p.AllegroSku,
//                    p.IsScrapable,
//                    p.IsRejected,
//                    p.AllegroMarginPrice,
//                    p.AddedDate,
//                    p.AllegroEan,
//                    FlagIds = p.ProductFlags.Select(pf => pf.FlagId).ToList()
//                })
//                .ToListAsync();

//            return Json(products);
//        }

//        [HttpPost]

//        public async Task<IActionResult> UpdateScrapableAllegroProduct(int storeId, [FromBody] int allegroProductId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (userStore == null) return Forbid();

//            var product = await _context.AllegroProducts.Include(p => p.Store)
//                .FirstOrDefaultAsync(p => p.AllegroProductId == allegroProductId && p.StoreId == storeId);

//            if (product == null) return NotFound(new { success = false, message = "Product not found." });

//            var scrapableCount = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId && p.IsScrapable);

//            if (!product.IsScrapable && scrapableCount >= product.Store.ProductsToScrapAllegro)
//            {
//                return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania dla Allegro." });
//            }

//            product.IsScrapable = !product.IsScrapable;
//            await _context.SaveChangesAsync();
//            return Json(new { success = true, newIsScrapable = product.IsScrapable });
//        }

//        [HttpPost]

//        public async Task<IActionResult> UpdateMultipleScrapableAllegroProducts(int storeId, [FromBody] List<int> productIds)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (userStore == null) return Forbid();

//            var store = await _context.Stores.Include(s => s.AllegroProducts).FirstOrDefaultAsync(s => s.StoreId == storeId);
//            if (store == null) return NotFound();

//            int currentScrapableCount = store.AllegroProducts.Count(p => p.IsScrapable);
//            int availableCount = (store.ProductsToScrapAllegro ?? int.MaxValue) - currentScrapableCount;

//            var productsToUpdateQuery = _context.AllegroProducts.Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId) && !p.IsScrapable);

//            var productsToUpdate = await productsToUpdateQuery.Take(availableCount).ToListAsync();

//            foreach (var product in productsToUpdate)
//            {
//                product.IsScrapable = true;
//            }

//            await _context.SaveChangesAsync();

//            int requestedCountToUpdate = await productsToUpdateQuery.CountAsync();
//            if (productsToUpdate.Count < requestedCountToUpdate)
//            {
//                return Json(new { success = true, message = $"Zaktualizowano {productsToUpdate.Count} z {requestedCountToUpdate} produktów. Przekroczono limit." });
//            }

//            return Json(new { success = true });
//        }

//        [HttpGet]
//        public async Task<IActionResult> DownloadAllegroSkeleton(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

//            if (userStore == null) return Forbid();

//            var products = await _context.AllegroProducts
//                .Include(p => p.ProductFlags)
//                .ThenInclude(pf => pf.Flag)
//                .Where(p => p.StoreId == storeId)
//                .ToListAsync();

//            using (var workbook = new XSSFWorkbook())
//            {
//                var sheet = workbook.CreateSheet("Produkty");

//                var headerRow = sheet.CreateRow(0);
//                headerRow.CreateCell(0).SetCellValue("ID");
//                headerRow.CreateCell(1).SetCellValue("EAN");
//                headerRow.CreateCell(2).SetCellValue("SKU");
//                headerRow.CreateCell(3).SetCellValue("CENA");
//                headerRow.CreateCell(4).SetCellValue("FLAGI");

//                var headerStyle = workbook.CreateCellStyle();
//                var font = workbook.CreateFont();
//                font.IsBold = true;
//                headerStyle.SetFont(font);
//                for (int i = 0; i < 5; i++) headerRow.GetCell(i).CellStyle = headerStyle;

//                int rowIndex = 1;
//                foreach (var product in products)
//                {
//                    var row = sheet.CreateRow(rowIndex++);

//                    row.CreateCell(0).SetCellValue(product.IdOnAllegro ?? "");

//                    row.CreateCell(1).SetCellValue(product.AllegroEan ?? "");

//                    row.CreateCell(2).SetCellValue(product.AllegroSku ?? "");

//                    if (product.AllegroMarginPrice.HasValue)
//                    {
//                        row.CreateCell(3).SetCellValue((double)product.AllegroMarginPrice.Value);
//                    }
//                    else
//                    {
//                        row.CreateCell(3).SetCellValue("");
//                    }

//                    if (product.ProductFlags != null && product.ProductFlags.Any())
//                    {
//                        var flagNames = product.ProductFlags
//                            .Select(pf => pf.Flag.FlagName)
//                            .Where(n => !string.IsNullOrEmpty(n));

//                        var flagsString = string.Join(", ", flagNames);
//                        row.CreateCell(4).SetCellValue(flagsString);
//                    }
//                    else
//                    {
//                        row.CreateCell(4).SetCellValue("");
//                    }
//                }

//                sheet.SetColumnWidth(0, 20 * 256);
//                sheet.SetColumnWidth(1, 18 * 256);
//                sheet.SetColumnWidth(2, 16 * 256);
//                sheet.SetColumnWidth(3, 14 * 256);
//                sheet.SetColumnWidth(4, 30 * 256);

//                using (var stream = new MemoryStream())
//                {
//                    workbook.Write(stream);
//                    var content = stream.ToArray();
//                    var fileName = $"Allegro_Szkielet_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
//                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
//                }
//            }
//        }

//        [HttpPost]

//        public async Task<IActionResult> ResetMultipleScrapableAllegroProducts(int storeId, [FromBody] List<int> productIds)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (userStore == null) return Forbid();

//            var productsToUpdate = await _context.AllegroProducts
//                .Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId))
//                .ToListAsync();

//            foreach (var product in productsToUpdate)
//            {
//                product.IsScrapable = false;
//            }

//            await _context.SaveChangesAsync();
//            return Json(new { success = true });
//        }

//        private class AllegroImportRow
//        {
//            public string Ean { get; set; }
//            public string IdOnAllegro { get; set; }
//            public decimal? Price { get; set; }
//            public string Sku { get; set; }
//            public List<string> FlagNames { get; set; } = new List<string>();
//        }

//        public class UpdateAllegroPurchasePriceViewModel
//        {
//            public int AllegroProductId { get; set; }
//            public decimal? NewPrice { get; set; }
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> UpdateAllegroPurchasePrice(int storeId, [FromBody] UpdateAllegroPurchasePriceViewModel model)
//        {
//            if (model == null || model.AllegroProductId <= 0)
//            {
//                return BadRequest(new { success = false, message = "Nieprawidłowe dane." });
//            }

//            if (model.NewPrice.HasValue && model.NewPrice < 0)
//            {
//                return BadRequest(new { success = false, message = "Cena nie może być ujemna." });
//            }

//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (!hasAccess)
//            {
//                return Forbid();
//            }

//            var product = await _context.AllegroProducts.FirstOrDefaultAsync(p => p.AllegroProductId == model.AllegroProductId && p.StoreId == storeId);
//            if (product == null)
//            {
//                return NotFound(new { success = false, message = "Produkt Allegro nie został znaleziony." });
//            }

//            try
//            {
//                if (product.AllegroMarginPrice != model.NewPrice)
//                {
//                    product.AllegroMarginPrice = model.NewPrice;
//                    product.AllegroMarginPriceUpdatedDate = DateTime.UtcNow;

//                    await _context.SaveChangesAsync();

//                    _logger.LogInformation("Zaktualizowano cenę zakupu Allegro dla produktu ID={ProductId} na {NewPrice} przez użytkownika ID={UserId}",
//                                model.AllegroProductId, model.NewPrice.HasValue ? model.NewPrice.Value.ToString() : "NULL", userId);
//                }

//                return Json(new { success = true, message = "Cena zakupu została zaktualizowana." });
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Błąd podczas aktualizacji ceny zakupu Allegro dla produktu ID={ProductId}", model.AllegroProductId);
//                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera." });
//            }
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> SetAllegroMargins(int storeId, IFormFile uploadedFile)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            _logger.LogInformation("Wywołanie SetAllegroMargins: StoreId={StoreId}, UserId={UserId}, FileName={FileName}, FileSize={FileSize}",
//                                     storeId, userId, uploadedFile?.FileName, uploadedFile?.Length);

//            var userStore = await _context.UserStores
//                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (userStore == null)
//            {
//                _logger.LogWarning("Brak dostępu użytkownika {UserId} do sklepu {StoreId} (SetAllegroMargins)", userId, storeId);
//                return Forbid();
//            }

//            if (uploadedFile == null || uploadedFile.Length == 0)
//            {
//                _logger.LogWarning("Brak pliku w requestcie lub plik pusty");
//                TempData["ErrorMessage"] = "Proszę wgrać poprawny plik Excel.";
//                return RedirectToAction("ProductList", new { storeId });
//            }
//            if (uploadedFile.Length > 10 * 1024 * 1024)
//            {
//                _logger.LogWarning("Przekroczony rozmiar pliku: {Size} bajtów", uploadedFile.Length);
//                TempData["ErrorMessage"] = "Wielkość pliku nie może przekraczać 10 MB.";
//                return RedirectToAction("ProductList", new { storeId });
//            }
//            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
//            if (ext != ".xls" && ext != ".xlsx")
//            {
//                _logger.LogWarning("Niewspierane rozszerzenie pliku: {Ext}", ext);
//                TempData["ErrorMessage"] = "Niewspierany format pliku. Proszę .xls lub .xlsx.";
//                return RedirectToAction("ProductList", new { storeId });
//            }
//            var mime = uploadedFile.ContentType;
//            if (mime != "application/vnd.ms-excel" &&
//                mime != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
//            {
//                _logger.LogWarning("Niewspierany typ MIME: {Mime}", mime);
//                TempData["ErrorMessage"] = "Niewspierany typ pliku. Proszę Excel.";
//                return RedirectToAction("ProductList", new { storeId });
//            }

//            try
//            {
//                var importRows = await ParseExcelFileExtended(uploadedFile);

//                if (importRows == null || !importRows.Any())
//                {
//                    TempData["ErrorMessage"] = "Plik nie zawiera poprawnych danych.";
//                    return RedirectToAction("AllegroProductList", new { storeId });
//                }

//                var distinctFlagNamesFromExcel = importRows
//                    .SelectMany(r => r.FlagNames)
//                    .Select(f => f.Trim())
//                    .Distinct(StringComparer.OrdinalIgnoreCase)
//                    .Where(f => !string.IsNullOrEmpty(f))
//                    .ToList();

//                var existingFlags = await _context.Flags
//                    .Where(f => f.StoreId == storeId && f.IsMarketplace)
//                    .ToListAsync();

//                var flagsToCreate = new List<FlagsClass>();

//                foreach (var flagName in distinctFlagNamesFromExcel)
//                {
//                    if (!existingFlags.Any(f => f.FlagName.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
//                    {
//                        flagsToCreate.Add(new FlagsClass
//                        {
//                            StoreId = storeId,
//                            FlagName = flagName,
//                            FlagColor = GenerateRandomColor(),
//                            IsMarketplace = true
//                        });
//                    }
//                }

//                if (flagsToCreate.Any())
//                {
//                    _context.Flags.AddRange(flagsToCreate);
//                    await _context.SaveChangesAsync();

//                    existingFlags.AddRange(flagsToCreate);
//                }

//                var flagMap = existingFlags.ToDictionary(f => f.FlagName.ToUpperInvariant(), f => f.FlagId);

//                var products = await _context.AllegroProducts
//                    .Include(p => p.ProductFlags)
//                    .Where(p => p.StoreId == storeId)
//                    .ToListAsync();

//                int updatedCount = 0;

//                foreach (var row in importRows)
//                {
//                    var targets = new List<AllegroProductClass>();

//                    if (!string.IsNullOrEmpty(row.IdOnAllegro))
//                    {
//                        var productById = products.FirstOrDefault(p => p.IdOnAllegro == row.IdOnAllegro);
//                        if (productById != null)
//                        {
//                            targets.Add(productById);
//                        }
//                    }

//                    if (!targets.Any() && !string.IsNullOrEmpty(row.Ean))
//                    {
//                        var productsByEan = products.Where(p => p.AllegroEan == row.Ean).ToList();
//                        if (productsByEan.Any())
//                        {
//                            targets.AddRange(productsByEan);
//                        }
//                    }

//                    foreach (var productToUpdate in targets)
//                    {
//                        bool isModified = false;

//                        if (row.Price.HasValue && productToUpdate.AllegroMarginPrice != row.Price.Value)
//                        {
//                            productToUpdate.AllegroMarginPrice = row.Price.Value;
//                            productToUpdate.AllegroMarginPriceUpdatedDate = DateTime.UtcNow;
//                            isModified = true;
//                        }

//                        if (!string.IsNullOrEmpty(row.Sku) && productToUpdate.AllegroSku != row.Sku)
//                        {
//                            productToUpdate.AllegroSku = row.Sku;
//                            isModified = true;
//                        }

//                        if (row.FlagNames != null && row.FlagNames.Any())
//                        {
//                            var targetFlagIds = row.FlagNames
//                                .Select(fn => fn.Trim().ToUpperInvariant())
//                                .Where(fn => flagMap.ContainsKey(fn))
//                                .Select(fn => flagMap[fn])
//                                .ToList();

//                            var currentFlagIds = productToUpdate.ProductFlags.Select(pf => pf.FlagId).ToList();

//                            if (!new HashSet<int>(currentFlagIds).SetEquals(targetFlagIds))
//                            {
//                                var flagsToRemove = productToUpdate.ProductFlags.ToList();
//                                foreach (var f in flagsToRemove) _context.ProductFlags.Remove(f);

//                                foreach (var flagId in targetFlagIds)
//                                {
//                                    _context.ProductFlags.Add(new ProductFlag
//                                    {
//                                        AllegroProductId = productToUpdate.AllegroProductId,
//                                        FlagId = flagId
//                                    });
//                                }
//                                isModified = true;
//                            }
//                        }

//                        if (isModified) updatedCount++;
//                    }
//                }

//                await _context.SaveChangesAsync();

//                TempData["SuccessMessage"] = $"Zaktualizowano {updatedCount} produktów (Ceny, SKU, Flagi).";
//                return RedirectToAction("AllegroProductList", new { storeId });
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Błąd podczas importu Allegro");
//                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
//                return RedirectToAction("AllegroProductList", new { storeId });
//            }
//        }

//        private async Task<List<AllegroImportRow>> ParseExcelFileExtended(IFormFile file)
//        {
//            var list = new List<AllegroImportRow>();

//            using (var stream = new MemoryStream())
//            {
//                await file.CopyToAsync(stream);
//                stream.Position = 0;

//                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
//                IWorkbook workbook = extension == ".xls" ? new HSSFWorkbook(stream) : new XSSFWorkbook(stream);

//                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
//                var sheet = workbook.GetSheetAt(0);
//                if (sheet == null) return null;

//                var headerRow = sheet.GetRow(0);
//                if (headerRow == null) return null;

//                int eanCol = -1, priceCol = -1, idCol = -1, skuCol = -1, flagCol = -1;

//                for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
//                {
//                    var txt = GetCellValue(headerRow.GetCell(c), evaluator)?.Trim().ToUpperInvariant().Replace(" ", "");
//                    if (string.IsNullOrEmpty(txt)) continue;

//                    if (txt == "EAN" || txt == "KODEAN") eanCol = c;
//                    else if (txt == "CENA" || txt == "PRICE" || txt == "CENAZAKUPU") priceCol = c;
//                    else if (txt == "ID" || txt == "IDONALLEGRO" || txt == "OFFERID") idCol = c;
//                    else if (txt == "SKU" || txt == "SYGNATURA") skuCol = c;
//                    else if (txt == "FLAGA" || txt == "FLAGI" || txt == "FLAGS") flagCol = c;
//                }

//                if ((eanCol < 0 && idCol < 0))
//                {
//                    _logger.LogError("Brak kolumn identyfikacyjnych (EAN lub ID)");
//                    return null;
//                }

//                for (int r = 1; r <= sheet.LastRowNum; r++)
//                {
//                    var row = sheet.GetRow(r);
//                    if (row == null) continue;

//                    var importItem = new AllegroImportRow();

//                    if (eanCol >= 0) importItem.Ean = GetCellValue(row.GetCell(eanCol), evaluator)?.Trim();

//                    if (idCol >= 0) importItem.IdOnAllegro = GetCellValue(row.GetCell(idCol), evaluator)?.Trim();

//                    if (string.IsNullOrEmpty(importItem.Ean) && string.IsNullOrEmpty(importItem.IdOnAllegro)) continue;

//                    if (priceCol >= 0)
//                    {
//                        var priceTxt = GetCellValue(row.GetCell(priceCol), evaluator)?.Trim().Replace(",", ".");
//                        if (decimal.TryParse(priceTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
//                        {
//                            importItem.Price = p;
//                        }
//                    }

//                    if (skuCol >= 0)
//                    {
//                        importItem.Sku = GetCellValue(row.GetCell(skuCol), evaluator)?.Trim();
//                    }

//                    if (flagCol >= 0)
//                    {
//                        var flagsRaw = GetCellValue(row.GetCell(flagCol), evaluator)?.Trim();
//                        if (!string.IsNullOrEmpty(flagsRaw))
//                        {
//                            var parts = flagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
//                            foreach (var part in parts)
//                            {
//                                importItem.FlagNames.Add(part.Trim());
//                            }
//                        }
//                    }

//                    list.Add(importItem);
//                }
//            }

//            return list;
//        }

//        private string GenerateRandomColor()
//        {
//            var random = new Random();
//            return String.Format("#{0:X6}", random.Next(0x1000000));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> ClearAllAllegroPurchasePrices(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            _logger.LogInformation("Próba usunięcia wszystkich cen zakupu Allegro dla StoreId={StoreId} przez UserId={UserId}", storeId, userId);

//            var userStore = await _context.UserStores
//                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (userStore == null)
//            {
//                _logger.LogWarning("User {UserId} nie ma dostępu do StoreId {StoreId} (ClearAllAllegroPurchasePrices).", userId, storeId);
//                return Forbid();
//            }

//            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
//            if (!storeExists)
//            {
//                _logger.LogWarning("Sklep o ID {StoreId} nie znaleziony (ClearAllAllegroPurchasePrices).", storeId);
//                return NotFound(new { success = false, message = "Sklep nie został znaleziony." });
//            }

//            try
//            {

//                var productsInStore = await _context.AllegroProducts
//                    .Where(p => p.StoreId == storeId)
//                    .ToListAsync();

//                int clearedCount = 0;
//                if (productsInStore.Any())
//                {
//                    foreach (var product in productsInStore)
//                    {

//                        if (product.AllegroMarginPrice != null)
//                        {
//                            product.AllegroMarginPrice = null;
//                            product.AllegroMarginPriceUpdatedDate = null;
//                            clearedCount++;
//                        }
//                    }
//                    await _context.SaveChangesAsync();
//                    _logger.LogInformation("Pomyślnie usunięto ceny zakupu dla {ClearedCount} produktów Allegro w StoreId={StoreId}.", clearedCount, storeId);
//                    TempData["SuccessMessage"] = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów Allegro.";

//                    return Json(new { success = true, message = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów Allegro." });
//                }
//                else
//                {
//                    _logger.LogInformation("Nie znaleziono produktów Allegro w StoreId={StoreId} do usunięcia cen.", storeId);
//                    TempData["SuccessMessage"] = "Brak produktów Allegro w sklepie, nie usunięto żadnych cen zakupu.";

//                    return Json(new { success = true, message = "Brak produktów Allegro w sklepie, nie usunięto żadnych cen zakupu." });
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Błąd podczas ClearAllAllegroPurchasePrices dla StoreId={StoreId}.", storeId);

//                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera podczas usuwania cen zakupu Allegro." });
//            }
//        }

//        private async Task<Dictionary<string, decimal>> ParseExcelFile(IFormFile file)
//        {
//            var marginData = new Dictionary<string, decimal>();

//            using (var stream = new MemoryStream())
//            {
//                await file.CopyToAsync(stream);
//                stream.Position = 0;

//                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
//                _logger.LogInformation("Ładowanie skoroszytu, rozszerzenie: {Ext}", extension);

//                IWorkbook workbook = extension switch
//                {
//                    ".xls" => new HSSFWorkbook(stream),
//                    ".xlsx" => new XSSFWorkbook(stream),
//                    _ => null
//                };
//                if (workbook == null)
//                {
//                    _logger.LogError("Nie udało się załadować skoroszytu");
//                    return null;
//                }
//                _logger.LogInformation("Skoroszyt załadowany: {Type}", workbook.GetType().Name);

//                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
//                var sheet = workbook.GetSheetAt(0);
//                if (sheet == null)
//                {
//                    _logger.LogError("Brak arkusza w skoroszycie");
//                    return null;
//                }
//                _logger.LogInformation("Odczyt arkusza: {SheetName}", sheet.SheetName);

//                var eanHeaders = new[] { "EAN", "KODEAN" };
//                var priceHeaders = new[] { "CENA", "PRICE" };
//                int eanCol = -1, priceCol = -1;

//                var headerRow = sheet.GetRow(0);
//                if (headerRow == null)
//                {
//                    _logger.LogError("Brak wiersza nagłówkowego");
//                    return null;
//                }

//                for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
//                {
//                    var cell = headerRow.GetCell(c);
//                    var txt = GetCellValue(cell, evaluator)?
//                                 .Trim()
//                                 .ToUpperInvariant()
//                                 .Replace(" ", "");
//                    if (eanHeaders.Contains(txt)) eanCol = c;
//                    if (priceHeaders.Contains(txt)) priceCol = c;
//                }
//                _logger.LogInformation("Znalezione kolumny — EAN: {EanCol}, CENA: {PriceCol}", eanCol, priceCol);

//                if (eanCol < 0 || priceCol < 0)
//                {
//                    _logger.LogError("Nie znaleziono wymaganych kolumn w nagłówku");
//                    return null;
//                }

//                for (int r = 1; r <= sheet.LastRowNum; r++)
//                {
//                    var row = sheet.GetRow(r);
//                    if (row == null) continue;

//                    var eCell = row.GetCell(eanCol);
//                    var pCell = row.GetCell(priceCol);

//                    var ean = GetCellValue(eCell, evaluator)?.Trim();
//                    var priceText = GetCellValue(pCell, evaluator)?.Trim();

//                    if (string.IsNullOrWhiteSpace(ean) || string.IsNullOrWhiteSpace(priceText))
//                        continue;

//                    _logger.LogDebug("Wiersz {Row}: EAN={Ean}, Cena surowa='{PriceText}'", r, ean, priceText);

//                    priceText = priceText.Replace(",", ".");
//                    if (decimal.TryParse(priceText,
//                                         NumberStyles.Any,
//                                         CultureInfo.InvariantCulture,
//                                         out var mVal))
//                    {
//                        _logger.LogDebug("Parsowanie OK: marża={Margin} dla EAN={Ean}", mVal, ean);
//                        if (!marginData.ContainsKey(ean))
//                            marginData.Add(ean, mVal);
//                        else
//                            _logger.LogWarning("Duplikat EAN: {Ean}, pomijam drugi wpis", ean);
//                    }
//                    else
//                    {
//                        _logger.LogWarning("Nie udało się sparsować ceny '{PriceText}' w wierszu {Row}", priceText, r);
//                    }
//                }
//            }

//            _logger.LogInformation("Zakończono parsowanie, znaleziono {Count} marż", marginData.Count);
//            return marginData.Count > 0 ? marginData : null;
//        }
//        private string GetCellValue(ICell cell, IFormulaEvaluator evaluator)
//        {
//            if (cell == null) return null;

//            if (cell.CellType == CellType.Formula &&
//                !string.IsNullOrEmpty(cell.CellFormula) &&
//                cell.CellFormula.Contains("["))
//            {

//                _logger.LogDebug("Formula external reference detected ('{Formula}'), using cached result.", cell.CellFormula);
//                return cell.CachedFormulaResultType switch
//                {
//                    CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
//                    CellType.String => cell.StringCellValue,
//                    CellType.Boolean => cell.BooleanCellValue.ToString(),
//                    _ => null
//                };
//            }

//            try
//            {
//                if (cell.CellType == CellType.Formula)
//                {

//                    var eval = evaluator.Evaluate(cell);
//                    if (eval != null)
//                    {
//                        return eval.CellType switch
//                        {
//                            CellType.Numeric => eval.NumberValue.ToString(CultureInfo.InvariantCulture),
//                            CellType.String => eval.StringValue,
//                            CellType.Boolean => eval.BooleanValue.ToString(),
//                            _ => null
//                        };
//                    }

//                    _logger.LogDebug("Evaluate zwróciło null dla formuły '{Formula}', używam cached.", cell.CellFormula);
//                    return cell.CachedFormulaResultType switch
//                    {
//                        CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
//                        CellType.String => cell.StringCellValue,
//                        CellType.Boolean => cell.BooleanCellValue.ToString(),
//                        _ => null
//                    };
//                }

//                return cell.CellType switch
//                {
//                    CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
//                    CellType.String => cell.StringCellValue,
//                    CellType.Boolean => cell.BooleanCellValue.ToString(),
//                    _ => cell.ToString()
//                };
//            }
//            catch (Exception ex)
//            {

//                _logger.LogDebug(ex, "Błąd Evaluate() dla komórki formuły '{Formula}', używam cached.", cell.CellFormula);
//                if (cell.CellType == CellType.Formula)
//                {
//                    return cell.CachedFormulaResultType switch
//                    {
//                        CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
//                        CellType.String => cell.StringCellValue,
//                        CellType.Boolean => cell.BooleanCellValue.ToString(),
//                        _ => null
//                    };
//                }
//                return null;
//            }
//        }
//    }
//}


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
    public class AllegroProductController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroProductController> _logger;

        public AllegroProductController(PriceSafariContext context, ILogger<AllegroProductController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        private async Task<bool> CurrentUserUsesProducerView()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return false;

            return await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UseProducerViewForMarketplace)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> HasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
        }

        // ════════════════════════════════════════════════════════════════
        //  WIDOK GŁÓWNY + LISTING
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> AllegroProductList(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            bool isProducerView = await CurrentUserUsesProducerView();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
            ViewBag.ProductCount = store.ProductsToScrapAllegro;
            ViewBag.StoreId = storeId;
            ViewBag.IsProducerView = isProducerView;

            var flags = await _context.Flags
                .Where(f => f.IsMarketplace && f.StoreId == storeId)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor
                })
                .ToListAsync();

            ViewBag.Flags = flags;

            return View("~/Views/Panel/Product/AllegroProductList.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetAllegroProducts(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();

            var products = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Include(p => p.ProductFlags)
                .Select(p => new
                {
                    p.AllegroProductId,
                    p.AllegroProductName,
                    p.AllegroOfferUrl,
                    p.IdOnAllegro,
                    p.AllegroSku,
                    p.IsScrapable,
                    p.IsRejected,
                    p.AllegroMarginPrice,
                    p.AllegroMapPrice,
                    p.AddedDate,
                    p.AllegroEan,
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
        public async Task<IActionResult> UpdateScrapableAllegroProduct(int storeId, [FromBody] int allegroProductId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var product = await _context.AllegroProducts.Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.AllegroProductId == allegroProductId && p.StoreId == storeId);

            if (product == null) return NotFound(new { success = false, message = "Product not found." });

            var scrapableCount = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId && p.IsScrapable);

            if (!product.IsScrapable && scrapableCount >= product.Store.ProductsToScrapAllegro)
            {
                return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania dla Allegro." });
            }

            product.IsScrapable = !product.IsScrapable;
            await _context.SaveChangesAsync();
            return Json(new { success = true, newIsScrapable = product.IsScrapable });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMultipleScrapableAllegroProducts(int storeId, [FromBody] List<int> productIds)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.Include(s => s.AllegroProducts).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            int currentScrapableCount = store.AllegroProducts.Count(p => p.IsScrapable);
            int availableCount = (store.ProductsToScrapAllegro ?? int.MaxValue) - currentScrapableCount;

            var productsToUpdateQuery = _context.AllegroProducts
                .Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId) && !p.IsScrapable);

            var productsToUpdate = await productsToUpdateQuery.Take(availableCount).ToListAsync();

            foreach (var product in productsToUpdate)
                product.IsScrapable = true;

            await _context.SaveChangesAsync();

            int requestedCountToUpdate = await productsToUpdateQuery.CountAsync();
            if (productsToUpdate.Count < requestedCountToUpdate)
            {
                return Json(new { success = true, message = $"Zaktualizowano {productsToUpdate.Count} z {requestedCountToUpdate} produktów. Przekroczono limit." });
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ResetMultipleScrapableAllegroProducts(int storeId, [FromBody] List<int> productIds)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var productsToUpdate = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId))
                .ToListAsync();

            foreach (var product in productsToUpdate)
                product.IsScrapable = false;

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ════════════════════════════════════════════════════════════════
        //  CENA INLINE — dynamicznie zakup (standard) / MAP (producent)
        // ════════════════════════════════════════════════════════════════

        public class UpdateAllegroPriceViewModel
        {
            public int AllegroProductId { get; set; }
            public decimal? NewPrice { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAllegroPurchasePrice(int storeId, [FromBody] UpdateAllegroPriceViewModel model)
        {
            if (model == null || model.AllegroProductId <= 0)
                return BadRequest(new { success = false, message = "Nieprawidłowe dane." });

            if (model.NewPrice.HasValue && model.NewPrice < 0)
                return BadRequest(new { success = false, message = "Cena nie może być ujemna." });

            if (!await HasAccessToStore(storeId)) return Forbid();

            var product = await _context.AllegroProducts
                .FirstOrDefaultAsync(p => p.AllegroProductId == model.AllegroProductId && p.StoreId == storeId);

            if (product == null)
                return NotFound(new { success = false, message = "Produkt Allegro nie został znaleziony." });

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            try
            {
                bool changed = false;

                if (isProducerView)
                {
                    if (product.AllegroMapPrice != model.NewPrice)
                    {
                        product.AllegroMapPrice = model.NewPrice;
                        product.AllegroMapPriceUpdatedDate = DateTime.UtcNow;
                        changed = true;
                    }
                }
                else
                {
                    if (product.AllegroMarginPrice != model.NewPrice)
                    {
                        product.AllegroMarginPrice = model.NewPrice;
                        product.AllegroMarginPriceUpdatedDate = DateTime.UtcNow;
                        changed = true;
                    }
                }

                if (changed) await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Zaktualizowano cenę {Label} dla produktu Allegro ID={ProductId} na {NewPrice}",
                    priceLabel, model.AllegroProductId,
                    model.NewPrice.HasValue ? model.NewPrice.Value.ToString(CultureInfo.InvariantCulture) : "NULL");

                return Json(new { success = true, message = $"Cena {priceLabel} została zaktualizowana." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas aktualizacji ceny {Label} dla produktu Allegro ID={ProductId}",
                    priceLabel, model.AllegroProductId);
                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllAllegroPurchasePrices(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
                return NotFound(new { success = false, message = "Sklep nie został znaleziony." });

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            try
            {
                var productsInStore = await _context.AllegroProducts
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int clearedCount = 0;
                foreach (var product in productsInStore)
                {
                    if (isProducerView)
                    {
                        if (product.AllegroMapPrice != null)
                        {
                            product.AllegroMapPrice = null;
                            product.AllegroMapPriceUpdatedDate = null;
                            clearedCount++;
                        }
                    }
                    else
                    {
                        if (product.AllegroMarginPrice != null)
                        {
                            product.AllegroMarginPrice = null;
                            product.AllegroMarginPriceUpdatedDate = null;
                            clearedCount++;
                        }
                    }
                }

                if (clearedCount > 0) await _context.SaveChangesAsync();

                _logger.LogInformation("Usunięto ceny {Label} dla {ClearedCount} produktów Allegro w StoreId={StoreId}",
                    priceLabel, clearedCount, storeId);

                var message = clearedCount > 0
                    ? $"Pomyślnie usunięto ceny {priceLabel} dla {clearedCount} produktów Allegro."
                    : $"Brak produktów Allegro z ustaloną ceną {priceLabel} — nic nie usunięto.";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas ClearAllAllegroPurchasePrices dla StoreId={StoreId}", storeId);
                return StatusCode(500, new { success = false, message = $"Wystąpił wewnętrzny błąd serwera podczas usuwania cen {priceLabel}." });
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  EXPORT SKELETONU — dynamicznie CENA / CENA MAP
        // ════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> DownloadAllegroSkeleton(int storeId)
        {
            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceHeader = isProducerView ? "CENA MAP" : "CENA";
            string fileSuffix = isProducerView ? "MAP" : "Zakup";

            var products = await _context.AllegroProducts
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

                row.CreateCell(0).SetCellValue(product.IdOnAllegro ?? "");
                row.CreateCell(1).SetCellValue(product.AllegroEan ?? "");
                row.CreateCell(2).SetCellValue(product.AllegroSku ?? "");

                decimal? priceValue = isProducerView ? product.AllegroMapPrice : product.AllegroMarginPrice;
                if (priceValue.HasValue)
                    row.CreateCell(3).SetCellValue((double)priceValue.Value);
                else
                    row.CreateCell(3).SetCellValue("");

                if (product.ProductFlags != null && product.ProductFlags.Any())
                {
                    var flagNames = product.ProductFlags
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
            var fileName = $"Allegro_Szkielet_{fileSuffix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // ════════════════════════════════════════════════════════════════
        //  IMPORT (Excel) — dynamicznie CENA / CENA MAP
        // ════════════════════════════════════════════════════════════════

        private class AllegroImportRow
        {
            public string Ean { get; set; }
            public string IdOnAllegro { get; set; }
            public decimal? Price { get; set; }
            public string Sku { get; set; }
            public List<string> FlagNames { get; set; } = new List<string>();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAllegroMargins(int storeId, IFormFile uploadedFile)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("Wywołanie SetAllegroMargins: StoreId={StoreId}, UserId={UserId}, FileName={FileName}, FileSize={FileSize}",
                                     storeId, userId, uploadedFile?.FileName, uploadedFile?.Length);

            if (!await HasAccessToStore(storeId)) return Forbid();

            bool isProducerView = await CurrentUserUsesProducerView();
            string priceLabel = isProducerView ? "MAP" : "zakupu";

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                TempData["ErrorMessage"] = "Proszę wgrać poprawny plik Excel.";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
            if (uploadedFile.Length > 10 * 1024 * 1024)
            {
                TempData["ErrorMessage"] = "Wielkość pliku nie może przekraczać 10 MB.";
                return RedirectToAction("AllegroProductList", new { storeId });
            }

            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
            {
                TempData["ErrorMessage"] = "Niewspierany format pliku. Proszę .xls lub .xlsx.";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
            var mime = uploadedFile.ContentType;
            if (mime != "application/vnd.ms-excel" &&
                mime != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                TempData["ErrorMessage"] = "Niewspierany typ pliku. Proszę Excel.";
                return RedirectToAction("AllegroProductList", new { storeId });
            }

            try
            {
                var importRows = await ParseExcelFileExtended(uploadedFile, isProducerView);

                if (importRows == null || !importRows.Any())
                {
                    TempData["ErrorMessage"] = $"Plik nie zawiera poprawnych danych lub nie znaleziono kolumny z ceną {priceLabel}.";
                    return RedirectToAction("AllegroProductList", new { storeId });
                }

                // Flagi — znajdź istniejące lub utwórz nowe
                var distinctFlagNamesFromExcel = importRows
                    .SelectMany(r => r.FlagNames)
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                var existingFlags = await _context.Flags
                    .Where(f => f.StoreId == storeId && f.IsMarketplace)
                    .ToListAsync();

                var flagsToCreate = distinctFlagNamesFromExcel
                    .Where(name => !existingFlags.Any(f => f.FlagName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .Select(name => new FlagsClass
                    {
                        StoreId = storeId,
                        FlagName = name,
                        FlagColor = GenerateRandomColor(),
                        IsMarketplace = true
                    })
                    .ToList();

                if (flagsToCreate.Any())
                {
                    _context.Flags.AddRange(flagsToCreate);
                    await _context.SaveChangesAsync();
                    existingFlags.AddRange(flagsToCreate);
                }

                var flagMap = existingFlags.ToDictionary(f => f.FlagName.ToUpperInvariant(), f => f.FlagId);

                var products = await _context.AllegroProducts
                    .Include(p => p.ProductFlags)
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var row in importRows)
                {
                    var targets = new List<AllegroProductClass>();

                    if (!string.IsNullOrEmpty(row.IdOnAllegro))
                    {
                        var productById = products.FirstOrDefault(p => p.IdOnAllegro == row.IdOnAllegro);
                        if (productById != null) targets.Add(productById);
                    }

                    if (!targets.Any() && !string.IsNullOrEmpty(row.Ean))
                    {
                        var productsByEan = products.Where(p => p.AllegroEan == row.Ean).ToList();
                        if (productsByEan.Any()) targets.AddRange(productsByEan);
                    }

                    foreach (var productToUpdate in targets)
                    {
                        bool isModified = false;

                        // Cena — kierunek zależy od widoku
                        if (row.Price.HasValue)
                        {
                            if (isProducerView)
                            {
                                if (productToUpdate.AllegroMapPrice != row.Price.Value)
                                {
                                    productToUpdate.AllegroMapPrice = row.Price.Value;
                                    productToUpdate.AllegroMapPriceUpdatedDate = DateTime.UtcNow;
                                    isModified = true;
                                }
                            }
                            else
                            {
                                if (productToUpdate.AllegroMarginPrice != row.Price.Value)
                                {
                                    productToUpdate.AllegroMarginPrice = row.Price.Value;
                                    productToUpdate.AllegroMarginPriceUpdatedDate = DateTime.UtcNow;
                                    isModified = true;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(row.Sku) && productToUpdate.AllegroSku != row.Sku)
                        {
                            productToUpdate.AllegroSku = row.Sku;
                            isModified = true;
                        }

                        if (row.FlagNames != null && row.FlagNames.Any())
                        {
                            var targetFlagIds = row.FlagNames
                                .Select(fn => fn.Trim().ToUpperInvariant())
                                .Where(fn => flagMap.ContainsKey(fn))
                                .Select(fn => flagMap[fn])
                                .ToList();

                            var currentFlagIds = productToUpdate.ProductFlags.Select(pf => pf.FlagId).ToList();

                            if (!new HashSet<int>(currentFlagIds).SetEquals(targetFlagIds))
                            {
                                var flagsToRemove = productToUpdate.ProductFlags.ToList();
                                foreach (var f in flagsToRemove) _context.ProductFlags.Remove(f);

                                foreach (var flagId in targetFlagIds)
                                {
                                    _context.ProductFlags.Add(new ProductFlag
                                    {
                                        AllegroProductId = productToUpdate.AllegroProductId,
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
                return RedirectToAction("AllegroProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas importu Allegro");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
        }

        private async Task<List<AllegroImportRow>> ParseExcelFileExtended(IFormFile file, bool isProducerView)
        {
            var list = new List<AllegroImportRow>();

            // Akceptowane nagłówki ceny zależą od trybu
            var priceHeaderCandidates = isProducerView
                ? new HashSet<string> { "MAP", "CENAMAP", "MAPPRICE", "CENAMINIMALNA", "MINIMALNACENA" }
                : new HashSet<string> { "CENA", "PRICE", "CENAZAKUPU" };

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            IWorkbook workbook = extension == ".xls" ? new HSSFWorkbook(stream) : new XSSFWorkbook(stream);

            var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
            var sheet = workbook.GetSheetAt(0);
            if (sheet == null) return null;

            var headerRow = sheet.GetRow(0);
            if (headerRow == null) return null;

            int eanCol = -1, priceCol = -1, idCol = -1, skuCol = -1, flagCol = -1;

            for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
            {
                var txt = GetCellValue(headerRow.GetCell(c), evaluator)?.Trim().ToUpperInvariant().Replace(" ", "");
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt == "EAN" || txt == "KODEAN") eanCol = c;
                else if (priceHeaderCandidates.Contains(txt)) priceCol = c;
                else if (txt == "ID" || txt == "IDONALLEGRO" || txt == "OFFERID") idCol = c;
                else if (txt == "SKU" || txt == "SYGNATURA") skuCol = c;
                else if (txt == "FLAGA" || txt == "FLAGI" || txt == "FLAGS") flagCol = c;
            }

            if (eanCol < 0 && idCol < 0)
            {
                _logger.LogError("Brak kolumn identyfikacyjnych (EAN lub ID)");
                return null;
            }

            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                var importItem = new AllegroImportRow();

                if (eanCol >= 0) importItem.Ean = GetCellValue(row.GetCell(eanCol), evaluator)?.Trim();
                if (idCol >= 0) importItem.IdOnAllegro = GetCellValue(row.GetCell(idCol), evaluator)?.Trim();

                if (string.IsNullOrEmpty(importItem.Ean) && string.IsNullOrEmpty(importItem.IdOnAllegro)) continue;

                if (priceCol >= 0)
                {
                    var priceTxt = GetCellValue(row.GetCell(priceCol), evaluator)?.Trim().Replace(",", ".");
                    if (decimal.TryParse(priceTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                        importItem.Price = p;
                }

                if (skuCol >= 0)
                    importItem.Sku = GetCellValue(row.GetCell(skuCol), evaluator)?.Trim();

                if (flagCol >= 0)
                {
                    var flagsRaw = GetCellValue(row.GetCell(flagCol), evaluator)?.Trim();
                    if (!string.IsNullOrEmpty(flagsRaw))
                    {
                        var parts = flagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                            importItem.FlagNames.Add(part.Trim());
                    }
                }

                list.Add(importItem);
            }

            return list;
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS — kolory + odczyt komórek Excela
        // ════════════════════════════════════════════════════════════════

        private string GenerateRandomColor()
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