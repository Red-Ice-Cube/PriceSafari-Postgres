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
                _logger.LogInformation("Rozpoczynam parsowanie pliku Excel dla Allegro");
                var marginData = await ParseExcelFile(uploadedFile);
                if (marginData == null || !marginData.Any())
                {
                    _logger.LogWarning("Plik dla Allegro nie zawiera poprawnych danych o cenach zakupu.");
                    TempData["ErrorMessage"] = "Plik nie zawiera poprawnych danych.";
                    return RedirectToAction("AllegroProductList", new { storeId });
                }
                _logger.LogInformation("Pobrano {Count} wpisów cen zakupu z pliku dla Allegro", marginData.Count);

                var products = await _context.AllegroProducts
                    .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.AllegroEan))
                    .ToListAsync();
                _logger.LogInformation("Znaleziono {Count} produktów Allegro do potencjalnej aktualizacji", products.Count);

                int updatedCount = 0;
                foreach (var prod in products)
                {

                    if (marginData.TryGetValue(prod.AllegroEan, out var marginValue))
                    {

                        _logger.LogInformation("Aktualizuję produkt Allegro EAN={Ean} Cena zakupu={Margin}", prod.AllegroEan, marginValue);
                        prod.AllegroMarginPrice = marginValue;
                        updatedCount++;
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Zaktualizowano ceny zakupu dla {UpdatedCount} produktów Allegro", updatedCount);

                TempData["SuccessMessage"] = $"Ceny zakupu zostały zaktualizowane dla {updatedCount} produktów Allegro.";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas SetAllegroMargins");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("AllegroProductList", new { storeId });
            }
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