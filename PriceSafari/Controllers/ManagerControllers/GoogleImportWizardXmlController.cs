using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PriceSafari.Data;
using PriceSafari.Models; // GoogleFieldMapping
using System.Collections.Generic;
using System.Linq;
using PriceSafari.Models.ProductXML;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class GoogleImportWizardXmlController : Controller
    {
        private readonly PriceSafariContext _context;

        public GoogleImportWizardXmlController(PriceSafariContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Wyświetla widok kreatora z mapowaniami w JSON (ViewBag.ExistingMappings).
        /// </summary>
        [HttpGet]
        public IActionResult ShowGoogleFeedXml(int storeId)
        {
            var store = _context.Stores.Find(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = "Sklep nie znaleziony.";
                return RedirectToAction("Index", "ProductMapping");
            }

            // Odczytujemy mapowania z bazy
            var existingMappings = _context.GoogleFieldMappings
                .Where(m => m.StoreId == storeId)
                .ToList();

            // Serializujemy do JSON
            var existingMappingsJson = System.Text.Json.JsonSerializer.Serialize(existingMappings);

            ViewBag.StoreId = storeId;
            ViewBag.ExistingMappings = existingMappingsJson;
            return View("~/Views/ManagerPanel/ProductMapping/WizardGoogleFeedXml.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> ProxyXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
                return NotFound("Brak sklepu.");

            if (string.IsNullOrEmpty(store.ProductMapXmlUrlGoogle))
                return BadRequest("Brak URL do pliku w bazie.");

            var url = store.ProductMapXmlUrlGoogle;


            if (url.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase))
            {
                try
                {

                    string filePathEncoded = url.Substring("file://".Length);
                    // Dekoduj URL, aby zamienić np. %20 na spację
                    string filePath = Uri.UnescapeDataString(filePathEncoded);
                    // Opcjonalnie usuń wiodące ukośniki
                    filePath = filePath.TrimStart('/', '\\');

                    // Odczytujemy zawartość pliku
                    var xmlContent = System.IO.File.ReadAllText(filePath);
                    return Content(xmlContent, "text/xml");
                }
                catch (Exception ex)
                {
                    return BadRequest($"Błąd odczytu pliku {url}: {ex.Message}");
                }
            }
            else
            {
                // Dla URL hostowanych (http/https) używamy HttpClient
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyFeedBot/1.0)");

                try
                {
                    var xmlContent = await client.GetStringAsync(url);
                    return Content(xmlContent, "text/xml");
                }
                catch (Exception ex)
                {
                    return BadRequest($"Błąd pobierania z {url}: {ex.Message}");
                }
            }
        }




        /// <summary>
        /// Zwraca aktualne mapowania (GoogleFieldMapping) w formie JSON.
        /// </summary>
        [HttpGet]
        public IActionResult GetGoogleMappings(int storeId)
        {
            var existing = _context.GoogleFieldMappings
                .Where(m => m.StoreId == storeId)
                .ToList();
            return Json(existing);
        }

        /// <summary>
        /// Zapisuje mapowania (nadpisuje stare) w tabeli GoogleFieldMappings.
        /// </summary>
        [HttpPost]
        public IActionResult SaveGoogleMappings([FromBody] List<FieldMappingDto> mappings, int storeId)
        {
            if (mappings == null) mappings = new List<FieldMappingDto>();

            // Usuń stare
            var oldMappings = _context.GoogleFieldMappings
                .Where(x => x.StoreId == storeId)
                .ToList();
            _context.GoogleFieldMappings.RemoveRange(oldMappings);

            // Dodaj nowe
            foreach (var m in mappings)
            {
                _context.GoogleFieldMappings.Add(new GoogleFieldMapping
                {
                    StoreId = storeId,
                    FieldName = m.FieldName,
                    LocalName = m.LocalName
                });
            }

            _context.SaveChanges();

            return Json(new { success = true, message = "Mapowania zapisane w bazie." });
        }

        /// <summary>
        /// Usuwa wszystkie mapowania dla danego storeId.
        /// </summary>
        [HttpPost]
        public IActionResult ClearGoogleMappings(int storeId)
        {
            var toRemove = _context.GoogleFieldMappings
                .Where(x => x.StoreId == storeId)
                .ToList();

            if (toRemove.Any())
            {
                _context.GoogleFieldMappings.RemoveRange(toRemove);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Usunięto mapowania (StoreId={storeId}).";
            }
            else
            {
                TempData["InfoMessage"] = "Nie było żadnych mapowań do usunięcia.";
            }

            return RedirectToAction("ShowGoogleFeedXml", new { storeId });
        }

        public class FieldMappingDto
        {
            public string FieldName { get; set; }  // "ExternalId" itp.
            public string LocalName { get; set; }  // "g:id" itp.
        }



        [HttpPost]
        public async Task<IActionResult> SaveProductMapsFromFront([FromBody] List<ProductMapDto> productMaps)
        {
            if (productMaps == null || productMaps.Count == 0)
            {
                return Json(new { success = false, message = "Brak productMaps" });
            }

            int storeId = productMaps[0].StoreId;
            var existing = await _context.ProductMaps
                .Where(pm => pm.StoreId == storeId)
                .ToListAsync();

            int added = 0, updated = 0;

            foreach (var pmDto in productMaps)
            {
                // Wyczyszczenie ExternalId z niedozwolonych znaków (zostawiamy cyfry)
                if (!string.IsNullOrEmpty(pmDto.ExternalId))
                {
                    pmDto.ExternalId = new string(pmDto.ExternalId.Where(c => char.IsDigit(c)).ToArray());
                }

                // Nowy - 1 do 1 (URL i ExternalId muszą się zgadzać)
                var found = existing.FirstOrDefault(x =>
                    x.Url == pmDto.Url
                    && x.ExternalId == pmDto.ExternalId
                );

                if (found == null)
                {
                    // Nie ma takiego produktu -> tworzymy nowy
                    var newMap = new ProductMap
                    {
                        StoreId = pmDto.StoreId,
                        ExternalId = pmDto.ExternalId,
                        Url = pmDto.Url,
                        GoogleEan = pmDto.GoogleEan,
                        GoogleImage = pmDto.GoogleImage,
                        GoogleExportedName = pmDto.GoogleExportedName,
                        GoogleExportedProducer = pmDto.GoogleExportedProducer,

                        // Pola GoogleXMLPrice, GoogleDeliveryXMLPrice
                        GoogleXMLPrice = pmDto.GoogleXMLPrice,
                        GoogleDeliveryXMLPrice = pmDto.GoogleDeliveryXMLPrice
                    };

                    _context.ProductMaps.Add(newMap);
                    existing.Add(newMap);
                    added++;
                }
                else
                {
                   
                    found.ExternalId = pmDto.ExternalId;
                    found.Url = pmDto.Url;
                    found.GoogleEan = pmDto.GoogleEan;
                    found.GoogleImage = pmDto.GoogleImage;
                    found.GoogleExportedName = pmDto.GoogleExportedName;
                    found.GoogleExportedProducer = pmDto.GoogleExportedProducer;

                    found.GoogleXMLPrice = pmDto.GoogleXMLPrice;
                    found.GoogleDeliveryXMLPrice = pmDto.GoogleDeliveryXMLPrice;

                    _context.ProductMaps.Update(found);
                    updated++;
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"Dodano {added}, zaktualizowano {updated}." });
        }




        public class ProductMapDto
        {
            public int StoreId { get; set; }
            public string ExternalId { get; set; }
            public string Url { get; set; }
            public string GoogleEan { get; set; }
            public string GoogleImage { get; set; }
            public string GoogleExportedName { get; set; }
            public string? GoogleExportedProducer { get; set; }
            public decimal? GoogleXMLPrice { get; set; }
            public decimal? GoogleDeliveryXMLPrice { get; set; }
        }



    }
}
