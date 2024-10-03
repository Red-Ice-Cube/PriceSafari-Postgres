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

        public ProductController(PriceSafariContext context)
        {
            _context = context;
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
                    p.CatalogNumber,
                    p.Ean,
                    p.MarginPrice,
                    p.MainUrl,
                   
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



        //Dodawnie wgrywania marzy

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetMargins(int storeId, IFormFile uploadedFile)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check if the user has access to the store
            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                ModelState.AddModelError("", "Proszę wgrać poprawny plik XML lub Excel.");
                ViewBag.StoreName = store.StoreName;
                ViewBag.StoreId = storeId;
                ViewBag.ShowUploadMarginsModal = true; // Flag to reopen the modal
                return View("ProductList"); // Return to the ProductList view
            }

            try
            {
                // Determine file type and parse accordingly
                var extension = Path.GetExtension(uploadedFile.FileName).ToLower();
                Dictionary<string, decimal> marginData = null;

                if (extension == ".xlsx" || extension == ".xls")
                {
                    marginData = await ParseExcelFile(uploadedFile);
                }
                else
                {
                    ModelState.AddModelError("", "Niewspierany format pliku. Proszę wgrać plik XML lub Excel.");
                    ViewBag.StoreName = store.StoreName;
                    ViewBag.StoreId = storeId;
                    ViewBag.ShowUploadMarginsModal = true;
                    return View("ProductList");
                }

                if (marginData == null || !marginData.Any())
                {
                    ModelState.AddModelError("", "Wgrany plik jest pusty lub nie zawiera poprawnych kolumn 'EAN' i 'CENA'.");
                    ViewBag.StoreName = store.StoreName;
                    ViewBag.StoreId = storeId;
                    ViewBag.ShowUploadMarginsModal = true;
                    return View("ProductList");
                }

                // Get products in the store with non-null EAN codes
                var products = await _context.Products
                    .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.Ean))
                    .ToListAsync();

                int updatedCount = 0;
                foreach (var product in products)
                {
                    if (marginData.TryGetValue(product.Ean, out decimal margin))
                    {
                        product.MarginPrice = margin;
                        updatedCount++;
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Marże zostały zaktualizowane dla {updatedCount} produktów.";

                return RedirectToAction("ProductList", new { storeId });
            }
            catch (Exception ex)
            {
                // Handle exceptions
                ModelState.AddModelError("", $"Wystąpił błąd: {ex.Message}");
                ViewBag.StoreName = store.StoreName;
                ViewBag.StoreId = storeId;
                ViewBag.ShowUploadMarginsModal = true;
                return View("ProductList");
            }
        }

        private async Task<Dictionary<string, decimal>> ParseExcelFile(IFormFile file)
        {
            var marginData = new Dictionary<string, decimal>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                IWorkbook workbook;
                // Determine the file extension
                var extension = Path.GetExtension(file.FileName).ToLower();

                if (extension == ".xls")
                {
                    workbook = new HSSFWorkbook(stream); // For .xls files
                }
                else if (extension == ".xlsx")
                {
                    workbook = new XSSFWorkbook(stream); // For .xlsx files
                }
                else
                {
                    // Unsupported file type
                    return marginData;
                }

                // Assume data is in the first worksheet
                var sheet = workbook.GetSheetAt(0);
                if (sheet == null)
                    return marginData;

                // Possible header names
                var eanHeaders = new[] { "EAN", "EAN CODE", "EANCODE", "KOD EAN" };
                var cenaHeaders = new[] { "CENA", "PRICE", "MARGIN", "CENA BRUTTO" };

                // Find the columns with headers 'EAN' and 'CENA'
                int eanColumnIndex = -1;
                int cenaColumnIndex = -1;

                // Assume headers are in the first row
                var headerRow = sheet.GetRow(0);
                if (headerRow == null)
                    return marginData;

                for (int i = headerRow.FirstCellNum; i < headerRow.LastCellNum; i++)
                {
                    var cell = headerRow.GetCell(i);
                    if (cell != null)
                    {
                        var headerText = cell.ToString().Trim().ToUpperInvariant();
                        if (eanHeaders.Contains(headerText))
                        {
                            eanColumnIndex = i;
                        }
                        else if (cenaHeaders.Contains(headerText))
                        {
                            cenaColumnIndex = i;
                        }
                    }
                }

                if (eanColumnIndex == -1 || cenaColumnIndex == -1)
                {
                    // Required columns not found
                    return marginData;
                }

                // Read data starting from the second row
                for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    var row = sheet.GetRow(rowIndex);
                    if (row == null)
                        continue;

                    var eanCell = row.GetCell(eanColumnIndex);
                    var cenaCell = row.GetCell(cenaColumnIndex);

                    var ean = eanCell?.ToString().Trim();
                    var cenaText = cenaCell?.ToString().Trim();

                    if (string.IsNullOrEmpty(ean) || string.IsNullOrEmpty(cenaText))
                        continue;

                    // Handle decimal separator and culture
                    cenaText = cenaText.Replace(',', '.');

                    if (decimal.TryParse(cenaText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal margin))
                    {
                        if (!marginData.ContainsKey(ean))
                        {
                            marginData.Add(ean, margin);
                        }
                    }
                }
            }

            return marginData;
        }


    }
}
