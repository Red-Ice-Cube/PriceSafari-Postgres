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
                return NotFound();
            }

            using (var client = new HttpClient())
            {
                var response = await client.GetStringAsync(store.ProductMapXmlUrl);
                var xml = XDocument.Parse(response);

                var products = xml.Descendants("o")
                    .GroupBy(x => x.Descendants("a")
                        .FirstOrDefault(a => a.Attribute("name")?.Value == "EAN")?.Value)
                    .Select(g => g.OrderBy(x => decimal.TryParse(x.Attribute("price")?.Value, out var price) ? price : decimal.MaxValue).First())
                    .Select(x =>
                    {
                        var rawProductName = x.Element("name")?.Value ?? "Brak";

                        // Usuwamy specyficzne znaki, takie jak ✔, ale zachowujemy inne znaki specjalne
                        var cleanedProductName = Regex.Replace(rawProductName, @"✔", "")
                                                      .Trim();

                        return new ProductMap
                        {
                            StoreId = storeId,
                            ExternalId = x.Attribute("id")?.Value,
                            Url = x.Attribute("url")?.Value,
                            CatalogNumber = x.Descendants("a")
                                             .FirstOrDefault(a => a.Attribute("name")?.Value == "Kod_producenta")?.Value,
                            Ean = x.Descendants("a")
                                   .FirstOrDefault(a => a.Attribute("name")?.Value == "EAN")?.Value,
                            MainUrl = x.Descendants("main").FirstOrDefault()?.Attribute("url")?.Value,
                            ExportedName = cleanedProductName
                        };
                    }).ToList();

                // Logowanie liczby produktów
                Console.WriteLine($"Znaleziono {products.Count} produktów do zaimportowania.");

                // Użycie BulkConfig do aktualizacji lub dodania nowych produktów
                var bulkConfig = new BulkConfig
                {
                    CustomDestinationTableName = "heatlead1_SQL_user.ProductMaps",  // Niestandardowa nazwa schematu i tabeli
                    PreserveInsertOrder = true,  // Utrzymanie porządku wstawiania
                    SetOutputIdentity = false,   // Wyłącz, jeśli nie potrzebujesz zwracania kluczy
                    BatchSize = 100,             // Definiujemy batch size dla większej optymalizacji
                    BulkCopyTimeout = 0,
                    UpdateByProperties = new List<string> { "Ean", "StoreId" }
                };

                // Wstawienie lub aktualizacja danych za pomocą BulkInsertOrUpdate
                await _context.BulkInsertOrUpdateAsync(products, bulkConfig);

                Console.WriteLine($"Zakończono importowanie produktów.");
            }

            return RedirectToAction("Index");
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
                .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.CatalogNumber))
                .ToListAsync();

            var unmappedProducts = new List<ProductClass>(); // Lista produktów, których nie udało się zmapować po nazwie

            // Etap 1: Mapowanie po nazwie
            foreach (var storeProduct in storeProducts)
            {
                // Oczyszczamy nazwę produktu sklepowego
                string cleanedStoreProductName = SimplifyName(storeProduct.ExportedNameCeneo);

                // Próbujemy znaleźć odpowiedni produkt mapowany na podstawie nazwy
                var possibleMappedProducts = mappedProducts
                    .Where(mp => SimplifyName(mp.ExportedName).Contains(cleanedStoreProductName) ||
                                 cleanedStoreProductName.Contains(SimplifyName(mp.ExportedName)))
                    .ToList();

                if (possibleMappedProducts.Count == 1)
                {
                    // Jeśli znaleźliśmy jedno dokładne dopasowanie, mapujemy produkt
                    var mappedProduct = possibleMappedProducts.First();
                    storeProduct.ExternalId = int.Parse(mappedProduct.ExternalId);
                    storeProduct.Url = mappedProduct.Url;
                    storeProduct.CatalogNumber = mappedProduct.CatalogNumber;
                    storeProduct.Ean = mappedProduct.Ean;
                    storeProduct.MainUrl = mappedProduct.MainUrl;
                }
                else
                {
                    // Jeśli nie udało się jednoznacznie przypisać, dodajemy do listy produktów do dalszego przetwarzania
                    unmappedProducts.Add(storeProduct);
                }
            }

            // Etap 2: Mapowanie po kodzie producenta (CatalogNumber)
            foreach (var storeProduct in unmappedProducts)
            {
                foreach (var mappedProduct in mappedProducts)
                {
                    // Sprawdzamy, czy CatalogNumber z mapowanego produktu znajduje się w nazwie produktu sklepowego
                    if (storeProduct.ProductName.Contains(mappedProduct.CatalogNumber))
                    {
                        // Mapujemy produkt na podstawie kodu producenta
                        storeProduct.ExternalId = int.Parse(mappedProduct.ExternalId);
                        storeProduct.Url = mappedProduct.Url;
                        storeProduct.CatalogNumber = mappedProduct.CatalogNumber;
                        storeProduct.Ean = mappedProduct.Ean;
                        storeProduct.MainUrl = mappedProduct.MainUrl;

                        // Przerwij pętlę, jeśli znajdziemy dopasowanie
                        break;
                    }
                }

                // Jeśli nie udało się zmapować, dodajemy log
                if (string.IsNullOrEmpty(storeProduct.ExternalId.ToString()))
                {
                    Console.WriteLine($"Nie udało się zmapować produktu:");
                    Console.WriteLine($"Nazwa produktu: {storeProduct.ProductName}, Kod producenta: {storeProduct.CatalogNumber}");
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("MappedProducts", new { storeId });
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
