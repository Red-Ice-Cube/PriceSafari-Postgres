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

    




        // Możliwe nazwy (synonimy) w węźle <attrs><a name="...">
        private static readonly string[] EanPossibleNames = new[]
        {
            "EAN", "Kod EAN", "EAN CODE", "kod ean"
        };

                private static readonly string[] ProducerCodePossibleNames = new[]
                {
            "Kod producenta", "kod_producenta", "producer code", "Kod Prod."
        };

                private static readonly string[] ProducerPossibleNames = new[]
                {
            "Producent", "Brand", "Manufacturer"
        };






        private string GetAttributeValue(XElement attrsElement, string[] possibleNames)
        {
            if (attrsElement == null) return null;

            // Szukamy <a> spośród children "attrsElement.Elements("a")"
            // i sprawdzamy atrybut "name"
            foreach (var name in possibleNames)
            {
                var match = attrsElement.Elements("a")
                    .FirstOrDefault(a => (string)a.Attribute("name") != null
                                         && ((string)a.Attribute("name")).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    // Zwracamy treść wewnątrz <a> (CDATA)
                    return match.Value?.Trim();
                }
            }

            return null; // Nic nie znaleźliśmy
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
                    // Pobieramy zawartość XML ze zdalnego URL-a
                    var response = await client.GetStringAsync(store.ProductMapXmlUrl);
                    var xml = XDocument.Parse(response);

                    // Znajdź wszystkie węzły <o> (oferty)
                    var offers = xml.Descendants("o").ToList();

                    // Pobieramy istniejące ProductMap-y (jeśli tak przechowujesz)
                    var existingProducts = await _context.ProductMaps
                        .Where(p => p.StoreId == storeId)
                        .ToListAsync();

                    // Iterujemy po już istniejących w bazie i aktualizujemy
                    foreach (var existingProduct in existingProducts)
                    {
                        // Szukamy w pliku XML oferty, która ma "url" takie samo jak existingProduct.Url
                        var matchOffer = offers.FirstOrDefault(o =>
                        {
                            var urlAttr = (string)o.Attribute("url");
                            return urlAttr == existingProduct.Url;
                        });

                        if (matchOffer != null)
                        {
                            // Odczyt danych z matchOffer i przypisanie do existingProduct
                            UpdateCeneoProductMapFields(existingProduct, matchOffer);
                        }
                        else
                        {
                            // Nie ma już tej oferty w nowym pliku XML - np. resetujemy pewne pola
                            existingProduct.Ean = null;
                            existingProduct.ExportedName = null;
                            existingProduct.MainUrl = null;
       
                        }
                    }

                    // Iterujemy po węzłach <o> w pliku XML i jeśli URL-a nie mamy w existingProducts, dodajemy nowy ProductMap
                    foreach (var offer in offers)
                    {
                        var urlAttr = (string)offer.Attribute("url");
                        if (!existingProducts.Any(p => p.Url == urlAttr))
                        {
                            var newMap = new ProductMap
                            {
                                StoreId = storeId,
                                Url = urlAttr
                            };
                            UpdateCeneoProductMapFields(newMap, offer);

                            _context.ProductMaps.Add(newMap);
                            existingProducts.Add(newMap); // aby następnym razem uwzględniać w ewentualnych porównaniach
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


        private void UpdateCeneoProductMapFields(ProductMap productMap, XElement offer)
        {
            // Atrybuty w <o>
            var offerId = (string)offer.Attribute("id");
            var priceAttr = (string)offer.Attribute("price");
            var productUrl = (string)offer.Attribute("url");

            // <cat>, <name>, <desc>
            var categoryNode = offer.Element("cat");
            var category = categoryNode?.Value?.Trim();

            var nameNode = offer.Element("name");
            var productName = nameNode?.Value?.Trim();

            var descNode = offer.Element("desc");
            var productDesc = descNode?.Value?.Trim();

            // <imgs><main url="..."/>
            var imgsNode = offer.Element("imgs");
            var mainImgUrl = imgsNode?.Element("main")?.Attribute("url")?.Value;

            // <attrs>
            var attrsNode = offer.Element("attrs");
            var ean = GetAttributeValue(attrsNode, EanPossibleNames);
            var producerCode = GetAttributeValue(attrsNode, ProducerCodePossibleNames);
            var producer = GetAttributeValue(attrsNode, ProducerPossibleNames);

            // Teraz przypisujesz do productMap
            productMap.ExternalId = offerId;
            productMap.MainUrl = mainImgUrl;
            productMap.ExportedName = productName;
            productMap.Ean = ean;
         
             
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
    }
}
