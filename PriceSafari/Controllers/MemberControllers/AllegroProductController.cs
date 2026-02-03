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

        [HttpGet]
        public async Task<IActionResult> AllegroProductList(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
            ViewBag.ProductCount = store.ProductsToScrapAllegro;
            ViewBag.StoreId = storeId;

            var flags = await _context.Flags
                .Where(f => f.IsMarketplace)
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var products = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Include(p => p.ProductFlags)
                .Select(p => new
                {
                    p.AllegroProductId,
                    p.AllegroProductName,
                    p.AllegroOfferUrl,
                    // --- DODANE POLA ---
                    p.IdOnAllegro,
                    p.AllegroSku,
                    // -------------------
                    p.IsScrapable,
                    p.IsRejected,
                    p.AllegroMarginPrice,
                    p.AddedDate,
                    p.AllegroEan,
                    FlagIds = p.ProductFlags.Select(pf => pf.FlagId).ToList()
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpPost]

        public async Task<IActionResult> UpdateScrapableAllegroProduct(int storeId, [FromBody] int allegroProductId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

            var store = await _context.Stores.Include(s => s.AllegroProducts).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            int currentScrapableCount = store.AllegroProducts.Count(p => p.IsScrapable);
            int availableCount = (store.ProductsToScrapAllegro ?? int.MaxValue) - currentScrapableCount;

            var productsToUpdateQuery = _context.AllegroProducts.Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId) && !p.IsScrapable);

            var productsToUpdate = await productsToUpdateQuery.Take(availableCount).ToListAsync();

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = true;
            }

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

            var productsToUpdate = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId))
                .ToListAsync();

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = false;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }



        private class AllegroImportRow
        {
            public string Ean { get; set; }
            public string IdOnAllegro { get; set; } // Nowe pole do matchowania
            public decimal? Price { get; set; }
            public string Sku { get; set; }         // Nowe pole SKU
            public List<string> FlagNames { get; set; } = new List<string>(); // Lista flag
        }




        public class UpdateAllegroPurchasePriceViewModel
        {
            public int AllegroProductId { get; set; }
            public decimal? NewPrice { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAllegroPurchasePrice(int storeId, [FromBody] UpdateAllegroPurchasePriceViewModel model)
        {
            if (model == null || model.AllegroProductId <= 0)
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

            var product = await _context.AllegroProducts.FirstOrDefaultAsync(p => p.AllegroProductId == model.AllegroProductId && p.StoreId == storeId);
            if (product == null)
            {
                return NotFound(new { success = false, message = "Produkt Allegro nie został znaleziony." });
            }

            try
            {
                // --- ZMIANA DLA ALLEGRO ---
                if (product.AllegroMarginPrice != model.NewPrice)
                {
                    product.AllegroMarginPrice = model.NewPrice;
                    product.AllegroMarginPriceUpdatedDate = DateTime.UtcNow; // Data aktualizacji

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Zaktualizowano cenę zakupu Allegro dla produktu ID={ProductId} na {NewPrice} przez użytkownika ID={UserId}",
                                model.AllegroProductId, model.NewPrice.HasValue ? model.NewPrice.Value.ToString() : "NULL", userId);
                }
                // --------------------------

                return Json(new { success = true, message = "Cena zakupu została zaktualizowana." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas aktualizacji ceny zakupu Allegro dla produktu ID={ProductId}", model.AllegroProductId);
                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera." });
            }
        }















        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAllegroMargins(int storeId, IFormFile uploadedFile)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("Wywołanie SetAllegroMargins: StoreId={StoreId}, UserId={UserId}, FileName={FileName}, FileSize={FileSize}",
                                     storeId, userId, uploadedFile?.FileName, uploadedFile?.Length);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null)
            {
                _logger.LogWarning("Brak dostępu użytkownika {UserId} do sklepu {StoreId} (SetAllegroMargins)", userId, storeId);
                return Forbid();
            }

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                _logger.LogWarning("Brak pliku w requestcie lub plik pusty");
                TempData["ErrorMessage"] = "Proszę wgrać poprawny plik Excel.";
                return RedirectToAction("ProductList", new { storeId });
            }
            if (uploadedFile.Length > 10 * 1024 * 1024)
            {
                _logger.LogWarning("Przekroczony rozmiar pliku: {Size} bajtów", uploadedFile.Length);
                TempData["ErrorMessage"] = "Wielkość pliku nie może przekraczać 10 MB.";
                return RedirectToAction("ProductList", new { storeId });
            }
            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
            {
                _logger.LogWarning("Niewspierane rozszerzenie pliku: {Ext}", ext);
                TempData["ErrorMessage"] = "Niewspierany format pliku. Proszę .xls lub .xlsx.";
                return RedirectToAction("ProductList", new { storeId });
            }
            var mime = uploadedFile.ContentType;
            if (mime != "application/vnd.ms-excel" &&
                mime != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                _logger.LogWarning("Niewspierany typ MIME: {Mime}", mime);
                TempData["ErrorMessage"] = "Niewspierany typ pliku. Proszę Excel.";
                return RedirectToAction("ProductList", new { storeId });
            }

            try
            {
                // 1. Parsowanie pliku do nowej struktury listy obiektów
                var importRows = await ParseExcelFileExtended(uploadedFile);

                if (importRows == null || !importRows.Any())
                {
                    TempData["ErrorMessage"] = "Plik nie zawiera poprawnych danych.";
                    return RedirectToAction("AllegroProductList", new { storeId });
                }

                // 2. LOGIKA FLAG - Tworzenie brakujących flag
                // Pobierz unikalne nazwy flag z pliku Excel
                var distinctFlagNamesFromExcel = importRows
                    .SelectMany(r => r.FlagNames)
                    .Select(f => f.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                // Pobierz istniejące flagi marketplace dla tego sklepu
                var existingFlags = await _context.Flags
                    .Where(f => f.StoreId == storeId && f.IsMarketplace)
                    .ToListAsync();

                var flagsToCreate = new List<FlagsClass>();

                foreach (var flagName in distinctFlagNamesFromExcel)
                {
                    // Sprawdź czy flaga już istnieje (ignorując wielkość liter)
                    if (!existingFlags.Any(f => f.FlagName.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
                    {
                        flagsToCreate.Add(new FlagsClass
                        {
                            StoreId = storeId,
                            FlagName = flagName,
                            FlagColor = GenerateRandomColor(), // Metoda pomocnicza do generowania koloru
                            IsMarketplace = true
                        });
                    }
                }

                // Zapisz nowe flagi w bazie, aby dostały ID
                if (flagsToCreate.Any())
                {
                    _context.Flags.AddRange(flagsToCreate);
                    await _context.SaveChangesAsync();

                    // Dodaj nowo utworzone flagi do listy istniejących, abyśmy mieli ich ID
                    existingFlags.AddRange(flagsToCreate);
                }

                // Stwórz słownik: Nazwa Flagi -> ID Flagi (dla szybkiego wyszukiwania)
                var flagMap = existingFlags.ToDictionary(f => f.FlagName.ToUpperInvariant(), f => f.FlagId);


                // 3. AKTUALIZACJA PRODUKTÓW
                // Pobieramy produkty z dołączonymi flagami, aby móc je modyfikować
                var products = await _context.AllegroProducts
                    .Include(p => p.ProductFlags)
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var row in importRows)
                {
                    // Zmieniamy logikę na listę celów, bo jeden EAN może pasować do wielu produktów
                    var targets = new List<AllegroProductClass>();

                    // KROK A: Priorytet - Szukaj po ID (unikalne dla oferty)
                    if (!string.IsNullOrEmpty(row.IdOnAllegro))
                    {
                        var productById = products.FirstOrDefault(p => p.IdOnAllegro == row.IdOnAllegro);
                        if (productById != null)
                        {
                            targets.Add(productById);
                        }
                    }

                    // KROK B: Jeśli NIE znaleziono po ID (lub brak ID), szukaj po EAN
                    // Uwaga: Pobieramy WSZYSTKIE produkty z tym EANem (Where zamiast FirstOrDefault)
                    if (!targets.Any() && !string.IsNullOrEmpty(row.Ean))
                    {
                        var productsByEan = products.Where(p => p.AllegroEan == row.Ean).ToList();
                        if (productsByEan.Any())
                        {
                            targets.AddRange(productsByEan);
                        }
                    }

                    // KROK C: Aktualizuj wszystkie znalezione produkty (może być 1, może być wiele, może być 0)
                    foreach (var productToUpdate in targets)
                    {
                        bool isModified = false;

                        // 1. Aktualizacja Ceny
                        if (row.Price.HasValue && productToUpdate.AllegroMarginPrice != row.Price.Value)
                        {
                            productToUpdate.AllegroMarginPrice = row.Price.Value;
                            productToUpdate.AllegroMarginPriceUpdatedDate = DateTime.UtcNow;
                            isModified = true;
                        }

                        // 2. Aktualizacja SKU
                        if (!string.IsNullOrEmpty(row.Sku) && productToUpdate.AllegroSku != row.Sku)
                        {
                            productToUpdate.AllegroSku = row.Sku;
                            isModified = true;
                        }

                        // 3. Aktualizacja Flag
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

                TempData["SuccessMessage"] = $"Zaktualizowano {updatedCount} produktów (Ceny, SKU, Flagi).";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas importu Allegro");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
        }

        private async Task<List<AllegroImportRow>> ParseExcelFileExtended(IFormFile file)
        {
            var list = new List<AllegroImportRow>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                IWorkbook workbook = extension == ".xls" ? new HSSFWorkbook(stream) : new XSSFWorkbook(stream);

                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                var sheet = workbook.GetSheetAt(0);
                if (sheet == null) return null;

                var headerRow = sheet.GetRow(0);
                if (headerRow == null) return null;

                // --- Detekcja Kolumn ---
                int eanCol = -1, priceCol = -1, idCol = -1, skuCol = -1, flagCol = -1;

                for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
                {
                    var txt = GetCellValue(headerRow.GetCell(c), evaluator)?.Trim().ToUpperInvariant().Replace(" ", "");
                    if (string.IsNullOrEmpty(txt)) continue;

                    if (txt == "EAN" || txt == "KODEAN") eanCol = c;
                    else if (txt == "CENA" || txt == "PRICE" || txt == "CENAZAKUPU") priceCol = c;
                    else if (txt == "ID" || txt == "IDONALLEGRO" || txt == "OFFERID") idCol = c; // Kolumna ID
                    else if (txt == "SKU" || txt == "SYGNATURA") skuCol = c; // Kolumna SKU
                    else if (txt == "FLAGA" || txt == "FLAGI" || txt == "FLAGS") flagCol = c; // Kolumna Flag
                }

                // Wymagamy przynajmniej EAN lub ID oraz CENY (chyba że importujemy tylko SKU/Flagi, ale na razie załóżmy cenę jako standard)
                if ((eanCol < 0 && idCol < 0))
                {
                    _logger.LogError("Brak kolumn identyfikacyjnych (EAN lub ID)");
                    return null;
                }

                // --- Iteracja po wierszach ---
                for (int r = 1; r <= sheet.LastRowNum; r++)
                {
                    var row = sheet.GetRow(r);
                    if (row == null) continue;

                    var importItem = new AllegroImportRow();

                    // 1. EAN
                    if (eanCol >= 0) importItem.Ean = GetCellValue(row.GetCell(eanCol), evaluator)?.Trim();

                    // 2. ID
                    if (idCol >= 0) importItem.IdOnAllegro = GetCellValue(row.GetCell(idCol), evaluator)?.Trim();

                    // Jeśli nie ma ani EAN ani ID, pomijamy wiersz
                    if (string.IsNullOrEmpty(importItem.Ean) && string.IsNullOrEmpty(importItem.IdOnAllegro)) continue;

                    // 3. CENA
                    if (priceCol >= 0)
                    {
                        var priceTxt = GetCellValue(row.GetCell(priceCol), evaluator)?.Trim().Replace(",", ".");
                        if (decimal.TryParse(priceTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                        {
                            importItem.Price = p;
                        }
                    }

                    // 4. SKU
                    if (skuCol >= 0)
                    {
                        importItem.Sku = GetCellValue(row.GetCell(skuCol), evaluator)?.Trim();
                    }

                    // 5. FLAGI
                    if (flagCol >= 0)
                    {
                        var flagsRaw = GetCellValue(row.GetCell(flagCol), evaluator)?.Trim();
                        if (!string.IsNullOrEmpty(flagsRaw))
                        {
                            // Dzielimy po przecinku lub średniku
                            var parts = flagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                importItem.FlagNames.Add(part.Trim());
                            }
                        }
                    }

                    list.Add(importItem);
                }
            }

            return list;
        }

        private string GenerateRandomColor()
        {
            var random = new Random();
            // Generuje losowy kolor HEX, np. #A3B2C1
            return String.Format("#{0:X6}", random.Next(0x1000000));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllAllegroPurchasePrices(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("Próba usunięcia wszystkich cen zakupu Allegro dla StoreId={StoreId} przez UserId={UserId}", storeId, userId);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null)
            {
                _logger.LogWarning("User {UserId} nie ma dostępu do StoreId {StoreId} (ClearAllAllegroPurchasePrices).", userId, storeId);
                return Forbid();
            }

            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
            {
                _logger.LogWarning("Sklep o ID {StoreId} nie znaleziony (ClearAllAllegroPurchasePrices).", storeId);
                return NotFound(new { success = false, message = "Sklep nie został znaleziony." });
            }

            try
            {

                var productsInStore = await _context.AllegroProducts
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                int clearedCount = 0;
                if (productsInStore.Any())
                {
                    foreach (var product in productsInStore)
                    {

                        if (product.AllegroMarginPrice != null)
                        {
                            product.AllegroMarginPrice = null;
                            product.AllegroMarginPriceUpdatedDate = null; // <--- DODAJ TO (czyścimy datę)
                            clearedCount++;
                        }
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Pomyślnie usunięto ceny zakupu dla {ClearedCount} produktów Allegro w StoreId={StoreId}.", clearedCount, storeId);
                    TempData["SuccessMessage"] = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów Allegro.";

                    return Json(new { success = true, message = $"Pomyślnie usunięto ceny zakupu dla {clearedCount} produktów Allegro." });
                }
                else
                {
                    _logger.LogInformation("Nie znaleziono produktów Allegro w StoreId={StoreId} do usunięcia cen.", storeId);
                    TempData["SuccessMessage"] = "Brak produktów Allegro w sklepie, nie usunięto żadnych cen zakupu.";

                    return Json(new { success = true, message = "Brak produktów Allegro w sklepie, nie usunięto żadnych cen zakupu." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas ClearAllAllegroPurchasePrices dla StoreId={StoreId}.", storeId);

                return StatusCode(500, new { success = false, message = "Wystąpił wewnętrzny błąd serwera podczas usuwania cen zakupu Allegro." });
            }
        }

        private async Task<Dictionary<string, decimal>> ParseExcelFile(IFormFile file)
        {
            var marginData = new Dictionary<string, decimal>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                _logger.LogInformation("Ładowanie skoroszytu, rozszerzenie: {Ext}", extension);

                IWorkbook workbook = extension switch
                {
                    ".xls" => new HSSFWorkbook(stream),
                    ".xlsx" => new XSSFWorkbook(stream),
                    _ => null
                };
                if (workbook == null)
                {
                    _logger.LogError("Nie udało się załadować skoroszytu");
                    return null;
                }
                _logger.LogInformation("Skoroszyt załadowany: {Type}", workbook.GetType().Name);

                var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                var sheet = workbook.GetSheetAt(0);
                if (sheet == null)
                {
                    _logger.LogError("Brak arkusza w skoroszycie");
                    return null;
                }
                _logger.LogInformation("Odczyt arkusza: {SheetName}", sheet.SheetName);

                var eanHeaders = new[] { "EAN", "KODEAN" };
                var priceHeaders = new[] { "CENA", "PRICE" };
                int eanCol = -1, priceCol = -1;

                var headerRow = sheet.GetRow(0);
                if (headerRow == null)
                {
                    _logger.LogError("Brak wiersza nagłówkowego");
                    return null;
                }

                for (int c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
                {
                    var cell = headerRow.GetCell(c);
                    var txt = GetCellValue(cell, evaluator)?
                                 .Trim()
                                 .ToUpperInvariant()
                                 .Replace(" ", "");
                    if (eanHeaders.Contains(txt)) eanCol = c;
                    if (priceHeaders.Contains(txt)) priceCol = c;
                }
                _logger.LogInformation("Znalezione kolumny — EAN: {EanCol}, CENA: {PriceCol}", eanCol, priceCol);

                if (eanCol < 0 || priceCol < 0)
                {
                    _logger.LogError("Nie znaleziono wymaganych kolumn w nagłówku");
                    return null;
                }

                for (int r = 1; r <= sheet.LastRowNum; r++)
                {
                    var row = sheet.GetRow(r);
                    if (row == null) continue;

                    var eCell = row.GetCell(eanCol);
                    var pCell = row.GetCell(priceCol);

                    var ean = GetCellValue(eCell, evaluator)?.Trim();
                    var priceText = GetCellValue(pCell, evaluator)?.Trim();

                    if (string.IsNullOrWhiteSpace(ean) || string.IsNullOrWhiteSpace(priceText))
                        continue;

                    _logger.LogDebug("Wiersz {Row}: EAN={Ean}, Cena surowa='{PriceText}'", r, ean, priceText);

                    priceText = priceText.Replace(",", ".");
                    if (decimal.TryParse(priceText,
                                         NumberStyles.Any,
                                         CultureInfo.InvariantCulture,
                                         out var mVal))
                    {
                        _logger.LogDebug("Parsowanie OK: marża={Margin} dla EAN={Ean}", mVal, ean);
                        if (!marginData.ContainsKey(ean))
                            marginData.Add(ean, mVal);
                        else
                            _logger.LogWarning("Duplikat EAN: {Ean}, pomijam drugi wpis", ean);
                    }
                    else
                    {
                        _logger.LogWarning("Nie udało się sparsować ceny '{PriceText}' w wierszu {Row}", priceText, r);
                    }
                }
            }

            _logger.LogInformation("Zakończono parsowanie, znaleziono {Count} marż", marginData.Count);
            return marginData.Count > 0 ? marginData : null;
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
    }
}