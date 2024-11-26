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
using EFCore.BulkExtensions;
using AngleSharp.Dom;
using NPOI.SS.Formula.Functions;

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
        public async Task<IActionResult> ImportProductsFromXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.ProductMapXmlUrl))
            {
                TempData["ErrorMessage"] = "Nie znaleziono sklepu lub brak adresu URL pliku XML.";
                return RedirectToAction("Index");
            }

            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync(store.ProductMapXmlUrl);
                    var xml = XDocument.Parse(response);

                    var ceneoProducts = xml.Descendants(XName.Get("item"))
                        .Select(x => new
                        {
                            ExternalId = x.Element(XName.Get("id", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            Url = x.Element(XName.Get("link", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            Ean = x.Element(XName.Get("gtin", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            ExportedName = x.Element(XName.Get("title", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            MainUrl = x.Element(XName.Get("image_link", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value
                        })
                        .ToList();

                    var existingProducts = await _context.ProductMaps
                        .Where(p => p.StoreId == storeId)
                        .ToListAsync();

                    foreach (var existingProduct in existingProducts)
                    {
                        var ceneoProduct = ceneoProducts.FirstOrDefault(p => p.Url == existingProduct.Url);

                        if (ceneoProduct != null)
                        {
                            existingProduct.Ean = ceneoProduct.Ean;
                            existingProduct.ExportedName = ceneoProduct.ExportedName;
                            existingProduct.MainUrl = ceneoProduct.MainUrl;
                        }
                        else
                        {
                            // Jeśli produkt nie istnieje w nowym pliku Ceneo, aktualizujemy tylko dane z tego źródła
                            existingProduct.Ean = null;
                            existingProduct.ExportedName = null;
                            existingProduct.MainUrl = null;
                        }
                    }

                    foreach (var ceneoProduct in ceneoProducts)
                    {
                        if (!existingProducts.Any(p => p.Url == ceneoProduct.Url))
                        {
                            _context.ProductMaps.Add(new ProductMap
                            {
                                StoreId = storeId,
                                ExternalId = ceneoProduct.ExternalId,
                                Url = ceneoProduct.Url,
                                Ean = ceneoProduct.Ean,
                                ExportedName = ceneoProduct.ExportedName,
                                MainUrl = ceneoProduct.MainUrl
                            });
                        }
                    }

                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "Import produktów z Ceneo zakończony sukcesem.";
                return RedirectToAction("MappedProducts", new { storeId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Błąd podczas importu produktów z Ceneo: {ex.Message}";
                return RedirectToAction("Index");
            }
        }




        [HttpPost]
        public async Task<IActionResult> ImportProductsFromGoogleXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.ProductMapXmlUrlGoogle))
            {
                TempData["ErrorMessage"] = "Nie znaleziono sklepu lub brak adresu URL pliku XML Google.";
                return RedirectToAction("Index");
            }

            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetStringAsync(store.ProductMapXmlUrlGoogle);
                    var xml = XDocument.Parse(response);

                    var googleProducts = xml.Descendants(XName.Get("item"))
                        .Select(x => new
                        {
                            ExternalId = x.Element(XName.Get("id", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            Url = x.Element(XName.Get("link", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            GoogleEan = x.Element(XName.Get("gtin", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            GoogleImage = x.Element(XName.Get("image_link", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value,
                            GoogleExportedName = x.Element(XName.Get("title", xml.Root.GetNamespaceOfPrefix("g").NamespaceName))?.Value
                        })
                        .Where(p => !string.IsNullOrEmpty(p.GoogleEan)) // Filtruj produkty bez EAN
                        .ToList();

                    var existingProducts = await _context.ProductMaps
                        .Where(p => p.StoreId == storeId)
                        .ToListAsync();

                    foreach (var existingProduct in existingProducts)
                    {
                        var googleProduct = googleProducts.FirstOrDefault(p => p.Url == existingProduct.Url);

                        if (googleProduct != null)
                        {
                            existingProduct.GoogleEan = googleProduct.GoogleEan;
                            existingProduct.GoogleImage = googleProduct.GoogleImage;
                            existingProduct.GoogleExportedName = googleProduct.GoogleExportedName;
                        }
                        else
                        {
                            // Jeśli produkt nie istnieje w nowym pliku Google, aktualizujemy dane Google
                            existingProduct.GoogleEan = null;
                            existingProduct.GoogleImage = null;
                            existingProduct.GoogleExportedName = null;
                        }
                    }

                    foreach (var googleProduct in googleProducts)
                    {
                        if (!existingProducts.Any(p => p.Url == googleProduct.Url))
                        {
                            _context.ProductMaps.Add(new ProductMap
                            {
                                StoreId = storeId,
                                ExternalId = googleProduct.ExternalId,
                                Url = googleProduct.Url,
                                GoogleEan = googleProduct.GoogleEan,
                                GoogleImage = googleProduct.GoogleImage,
                                GoogleExportedName = googleProduct.GoogleExportedName
                            });
                        }
                    }

                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "Import produktów z Google zakończony sukcesem.";
                return RedirectToAction("MappedProducts", new { storeId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Błąd podczas importu produktów z Google: {ex.Message}";
                return RedirectToAction("Index");
            }
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

            // Pobieramy produkty zmapowane
            var mappedProducts = await _context.ProductMaps
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var unmappedProducts = new List<ProductClass>(); // Lista produktów, których nie udało się zmapować po nazwie

            // Etap 1: Mapowanie po nazwie
            foreach (var storeProduct in storeProducts)
            {
                // Oczyszczamy nazwę produktu sklepowego
                string cleanedStoreProductName = SimplifyName(storeProduct.ExportedNameCeneo);

                // Próbujemy znaleźć odpowiedni produkt mapowany na podstawie nazwy
                var possibleMappedProducts = mappedProducts
                    .Where(mp => !string.IsNullOrEmpty(mp.ExportedName) &&
                                 (SimplifyName(mp.ExportedName).Contains(cleanedStoreProductName) ||
                                  cleanedStoreProductName.Contains(SimplifyName(mp.ExportedName))))
                    .ToList();

                if (possibleMappedProducts.Count == 1)
                {
                    // Jeśli znaleźliśmy jedno dokładne dopasowanie, mapujemy produkt
                    var mappedProduct = possibleMappedProducts.First();
                    MapProductFields(storeProduct, mappedProduct);
                }
                else
                {
                    // Jeśli nie udało się jednoznacznie przypisać, dodajemy do listy produktów do dalszego przetwarzania
                    unmappedProducts.Add(storeProduct);
                }
            }

            // Etap 2: Mapowanie po kodzie producenta (CatalogNumber) i innymi polami
            foreach (var storeProduct in unmappedProducts.ToList())
            {
                foreach (var mappedProduct in mappedProducts)
                {
                    if (!string.IsNullOrEmpty(mappedProduct.CatalogNumber) &&
                        storeProduct.ProductName.Contains(mappedProduct.CatalogNumber))
                    {
                        // Mapujemy produkt na podstawie kodu producenta
                        MapProductFields(storeProduct, mappedProduct);
                        unmappedProducts.Remove(storeProduct);
                        break;
                    }
                    else if (!string.IsNullOrEmpty(mappedProduct.Ean) &&
                             storeProduct.ProductName.Contains(mappedProduct.Ean))
                    {
                        // Mapowanie na podstawie EAN
                        MapProductFields(storeProduct, mappedProduct);
                        unmappedProducts.Remove(storeProduct);
                        break;
                    }
                }

                // Jeśli nadal nie udało się zmapować, dodajemy log
                if (unmappedProducts.Contains(storeProduct))
                {
                    Console.WriteLine($"Nie udało się zmapować produktu:");
                    Console.WriteLine($"Nazwa produktu: {storeProduct.ProductName}, Kod producenta: {storeProduct.CatalogNumber}, EAN: {storeProduct.Ean}");
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MappedProducts", new { storeId });
        }

        // Funkcja pomocnicza do mapowania pól produktu
        private void MapProductFields(ProductClass storeProduct, ProductMap mappedProduct)
        {
            storeProduct.ExternalId = int.TryParse(mappedProduct.ExternalId, out var externalId) ? externalId : (int?)null;
            storeProduct.Url = mappedProduct.Url;
            storeProduct.CatalogNumber = mappedProduct.CatalogNumber;
            storeProduct.Ean = mappedProduct.Ean;
            storeProduct.MainUrl = mappedProduct.MainUrl;
        }

        // Funkcja do uproszczania nazw poprzez usunięcie zbędnych spacji i standaryzację znaków
        private string SimplifyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Zachowujemy ważne znaki specjalne i usuwamy jedynie niepożądane
            var simplifiedName = Regex.Replace(name, @"[^\w\s\-+/*=°²,.()]", "").ToUpperInvariant().Replace(" ", "");
            return simplifiedName;
        }





        [HttpPost]
        public async Task<IActionResult> RemoveWordFromProductNamesCeneo(string wordToRemove)
        {
            if (string.IsNullOrEmpty(wordToRemove))
            {
                return BadRequest("Słowo do usunięcia jest wymagane.");
            }

            var products = await _context.Products
                .Where(p => !string.IsNullOrEmpty(p.ExportedNameCeneo))
                .ToListAsync();

            foreach (var product in products)
            {
                if (product.ExportedNameCeneo.Contains(wordToRemove, StringComparison.OrdinalIgnoreCase))
                {
                    product.ExportedNameCeneo = product.ExportedNameCeneo.Replace(wordToRemove, "", StringComparison.OrdinalIgnoreCase).Trim();
                    _context.Products.Update(product);
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MappedProducts", new { storeId = products.FirstOrDefault()?.StoreId });
        }
    }
}
