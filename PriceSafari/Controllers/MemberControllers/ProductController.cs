using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using System.Security.Claims;
using PriceSafari.Models.ViewModels;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;

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

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                .ThenInclude(s => s.Products)
                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                ProductCount = store.Products.Count(p => p.IsScrapable),
                AllowedProducts = store.ProductsToScrap
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
            ViewBag.ProductCount = store.ProductsToScrap;
            ViewBag.StoreId = storeId;

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
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.Category,
                    p.OfferUrl,
                    p.IsScrapable,
                    p.IsRejected,
                    p.Url,
                    p.Ean,
                    p.MarginPrice,
                    p.MainUrl,
                    p.GoogleUrl,

                    AddedDate = p.AddedDate,
                    FoundOnGoogleDate = p.FoundOnGoogleDate,
                    FoundOnCeneoDate = p.FoundOnCeneoDate
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetMargins(int storeId, IFormFile uploadedFile)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation("Wywołanie SetMargins: StoreId={StoreId}, UserId={UserId}, FileName={FileName}, FileSize={FileSize}",
                                    storeId, userId, uploadedFile?.FileName, uploadedFile?.Length);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null)
            {
                _logger.LogWarning("Brak dostępu użytkownika {UserId} do sklepu {StoreId}", userId, storeId);
                return Forbid();
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                _logger.LogWarning("Nie znaleziono sklepu o ID {StoreId}", storeId);
                return NotFound();
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
                _logger.LogInformation("Rozpoczynam parsowanie pliku Excel");
                var marginData = await ParseExcelFile(uploadedFile);
                if (marginData == null || !marginData.Any())
                {
                    _logger.LogWarning("Plik nie zawiera poprawnych danych o cenach zakupu.");
                    TempData["ErrorMessage"] = "Plik nie zawiera poprawnych danych.";
                    return RedirectToAction("ProductList", new { storeId });
                }
                _logger.LogInformation("Pobrano {Count} wpisów cen zakupu z pliku", marginData.Count);

                var products = await _context.Products
                    .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.Ean))
                    .ToListAsync();
                _logger.LogInformation("Znaleziono {Count} produktów do potencjalnej aktualizacji", products.Count);

                int updatedCount = 0;
                foreach (var prod in products)
                {
                    if (marginData.TryGetValue(prod.Ean, out var m))
                    {
                        _logger.LogInformation("Aktualizuję produkt EAN={Ean} Cena zakupu={Margin}", prod.Ean, m);
                        prod.MarginPrice = m;
                        updatedCount++;
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Zaktualizowano ceny zakupu dla {UpdatedCount} produktów", updatedCount);

                TempData["SuccessMessage"] = $"Ceny zakupu zostały zaktualizowane dla {updatedCount} produktów.";
                return RedirectToAction("ProductList", new { storeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas SetMargins");
                TempData["ErrorMessage"] = $"Wystąpił błąd: {ex.Message}";
                return RedirectToAction("ProductList", new { storeId });
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



        // Umieść tę metodę wewnątrz klasy ProductController

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePurchasePrice(int storeId, [FromBody] UpdatePurchasePriceViewModel model)
        {
            if (model == null)
            {
                return BadRequest(new { success = false, message = "Nieprawidłowe dane." });
            }

            // Walidacja ceny - nie może być ujemna
            if (model.NewPrice.HasValue && model.NewPrice < 0)
            {
                return BadRequest(new { success = false, message = "Cena nie może być ujemna." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Sprawdzenie dostępu użytkownika do sklepu
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
            {
                return Forbid();
            }

            // Znalezienie produktu
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == model.ProductId && p.StoreId == storeId);
            if (product == null)
            {
                return NotFound(new { success = false, message = "Produkt nie został znaleziony." });
            }

            try
            {
                // Ustawienie nowej ceny (lub null w przypadku usunięcia)
                product.MarginPrice = model.NewPrice;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Zaktualizowano cenę zakupu dla produktu ID={ProductId} na {NewPrice} przez użytkownika ID={UserId}",
                                        model.ProductId, model.NewPrice.HasValue ? model.NewPrice.Value.ToString() : "NULL", userId);

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