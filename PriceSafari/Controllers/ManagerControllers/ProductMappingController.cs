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
                // Truncate Table jest szybki, ale nie działa z tabelami powiązanymi z kluczami obcymi
                await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE ProductMaps");

                TempData["SuccessMessage"] = "Wszystkie wpisy w tabeli ProductMaps zostały pomyślnie usunięte.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Zwraca wiadomość o błędzie
                TempData["ErrorMessage"] = $"Wystąpił błąd podczas usuwania raportów: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        public async Task<IActionResult> MappedProducts(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            var mappedProducts = await _context.ProductMaps
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var storeProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreProducts = storeProducts;
            ViewBag.StoreId = storeId;

            return View("~/Views/ManagerPanel/ProductMapping/MappedProducts.cshtml", mappedProducts);
        }

        [HttpPost]
        public async Task<IActionResult> MapProducts(int storeId)
        {
            // Pobieramy produkty ze sklepu
            var storeProducts = await _context.Products
                .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.ExportedNameCeneo))
                .ToListAsync();

            // Grupujemy produkty według oczyszczonej nazwy z Ceneo
            var groupedProducts = storeProducts
                .GroupBy(p => SimplifyName(p.ExportedNameCeneo))
                .ToList();

            foreach (var group in groupedProducts)
            {
                var productsInGroup = group.ToList();

                if (productsInGroup.Count > 1)
                {
                    // Jeśli mamy więcej niż jeden produkt w grupie, dokonujemy scalenia
                    var mainProduct = productsInGroup.First();

                    for (int i = 1; i < productsInGroup.Count; i++)
                    {
                        var duplicateProduct = productsInGroup[i];

                        // Łączymy dane z duplikatu do głównego produktu
                        MergeProductData(mainProduct, duplicateProduct);

                        // Usuwamy duplikat z kontekstu bazy danych
                        _context.Products.Remove(duplicateProduct);
                    }

                    // Aktualizujemy główny produkt w bazie danych
                    _context.Products.Update(mainProduct);
                }
                else
                {
                    // Jeśli jest tylko jeden produkt w grupie, nie musimy nic robić
                    continue;
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MappedProducts", new { storeId });
        }


        // Funkcja do uproszczania nazw poprzez usunięcie zbędnych znaków i standaryzację
        private string SimplifyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Usuwamy niepożądane znaki i konwertujemy na wielkie litery
            var simplifiedName = Regex.Replace(name, @"[^\w]", "").ToUpperInvariant();
            return simplifiedName;
        }





        // Funkcja do łączenia danych z duplikatu do głównego produktu
        private void MergeProductData(ProductClass mainProduct, ProductClass duplicateProduct)
        {
            // Jeśli główny produkt nie ma wartości w polu, a duplikat ma, to kopiujemy dane

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

            if (mainProduct.IsScrapable != duplicateProduct.IsScrapable)
                mainProduct.IsScrapable = mainProduct.IsScrapable || duplicateProduct.IsScrapable;

            if (mainProduct.IsRejected != duplicateProduct.IsRejected)
                mainProduct.IsRejected = mainProduct.IsRejected && duplicateProduct.IsRejected;

            if (string.IsNullOrEmpty(mainProduct.ExportedNameCeneo) && !string.IsNullOrEmpty(duplicateProduct.ExportedNameCeneo))
                mainProduct.ExportedNameCeneo = duplicateProduct.ExportedNameCeneo;

            // Google Shopping fields
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

            // Marża
            if (!mainProduct.MarginPrice.HasValue && duplicateProduct.MarginPrice.HasValue)
                mainProduct.MarginPrice = duplicateProduct.MarginPrice;

            // Łączymy historię cen
            foreach (var priceHistory in duplicateProduct.PriceHistories)
            {
                if (!mainProduct.PriceHistories.Contains(priceHistory))
                {
                    mainProduct.PriceHistories.Add(priceHistory);
                }
            }

            // Łączymy flagi produktu
            foreach (var productFlag in duplicateProduct.ProductFlags)
            {
                if (!mainProduct.ProductFlags.Contains(productFlag))
                {
                    mainProduct.ProductFlags.Add(productFlag);
                }
            }
        }



        [HttpPost]
        public async Task<IActionResult> CreateOrUpdateProductsFromProductMap(int storeId)
        {
            // Pobierz wszystkie wpisy ProductMap dla danego sklepu
            var mappedProducts = await _context.ProductMaps
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            // Pobierz istniejące produkty dla sklepu
            var existingProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            foreach (var mappedProduct in mappedProducts)
            {
                int? externalId = int.TryParse(mappedProduct.ExternalId, out var extId) ? extId : (int?)null;
                string url = mappedProduct.Url;

                // Sprawdź, czy produkt już istnieje na podstawie ExternalId i Url
                var existingProduct = existingProducts.FirstOrDefault(p => p.ExternalId == externalId && p.Url == url);

                if (existingProduct != null)
                {
                    // Aktualizuj istniejący produkt
                    MapProductFields(existingProduct, mappedProduct);
                    _context.Products.Update(existingProduct);
                }
                else
                {
                    // Utwórz nowy produkt
                    var newProduct = new ProductClass
                    {
                        StoreId = storeId,
                        ExternalId = externalId,
                        Url = url
                    };

                    // Mapuj pola z mappedProduct
                    MapProductFields(newProduct, mappedProduct);

                    _context.Products.Add(newProduct);
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Produkty zostały pomyślnie utworzone lub zaktualizowane.";
            return RedirectToAction("MappedProducts", new { storeId });
        }

        // Funkcja pomocnicza do mapowania pól produktu
        private void MapProductFields(ProductClass product, ProductMap mappedProduct)
        {
            // Ustaw ExternalId i Url (jeśli nie zostały już ustawione)
            if (!product.ExternalId.HasValue)
                product.ExternalId = int.TryParse(mappedProduct.ExternalId, out var externalId) ? externalId : (int?)null;

            if (string.IsNullOrEmpty(product.Url))
                product.Url = mappedProduct.Url;

            // Mapuj nazwę produktu z Google lub Ceneo, w zależności od dostępności
            if (!string.IsNullOrEmpty(mappedProduct.GoogleExportedName))
            {
                product.ProductName = mappedProduct.GoogleExportedName;
                product.ProductNameInStoreForGoogle = mappedProduct.GoogleExportedName;
                product.ExportedNameCeneo = mappedProduct.ExportedName; // Jeśli chcesz również zachować nazwę z Ceneo
            }
            else if (!string.IsNullOrEmpty(mappedProduct.ExportedName))
            {
                product.ProductName = mappedProduct.ExportedName;
                product.ExportedNameCeneo = mappedProduct.ExportedName;
            }
            else
            {
                product.ProductName = "Brak nazwy produktu";
            }

            // Mapuj EAN z Google lub Ceneo
            if (!string.IsNullOrEmpty(mappedProduct.GoogleEan))
            {
                product.EanGoogle = mappedProduct.GoogleEan;
                product.Ean = mappedProduct.GoogleEan; // Jeśli chcesz, aby EAN główny był z Google
            }
            else if (!string.IsNullOrEmpty(mappedProduct.Ean))
            {
                product.Ean = mappedProduct.Ean;
            }

            // Mapuj MainUrl i ImgUrlGoogle
            if (!string.IsNullOrEmpty(mappedProduct.GoogleImage))
            {
                product.ImgUrlGoogle = mappedProduct.GoogleImage;
                product.MainUrl = mappedProduct.GoogleImage; // Jeśli chcesz, aby główne zdjęcie było z Google
            }
            else if (!string.IsNullOrEmpty(mappedProduct.MainUrl))
            {
                product.MainUrl = mappedProduct.MainUrl;
            }

        }





        [HttpPost]
        public async Task<IActionResult> RemoveAllProductsForStore(int storeId)
        {
            try
            {
                // Pobieramy wszystkie produkty dla danego storeId
                var productsToRemove = await _context.Products
                    .Where(p => p.StoreId == storeId)
                    .ToListAsync();

                // Usuwamy z kontekstu
                _context.Products.RemoveRange(productsToRemove);

                // Zapisujemy
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
