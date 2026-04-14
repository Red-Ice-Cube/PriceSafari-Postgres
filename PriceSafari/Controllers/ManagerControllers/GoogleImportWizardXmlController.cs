using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PriceSafari.Data;
using PriceSafari.Models;
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

        [HttpGet]
        public IActionResult ShowGoogleFeedXml(int storeId)
        {
            var store = _context.Stores.Find(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = "Sklep nie znaleziony.";
                return RedirectToAction("Index", "ProductMapping");
            }

            var existingMappings = _context.GoogleFieldMappings
                .Where(m => m.StoreId == storeId)
                .ToList();

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

                    string filePath = Uri.UnescapeDataString(filePathEncoded);

                    filePath = filePath.TrimStart('/', '\\');

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

        [HttpGet]
        public IActionResult GetGoogleMappings(int storeId)
        {
            var existing = _context.GoogleFieldMappings
                .Where(m => m.StoreId == storeId)
                .ToList();
            return Json(existing);
        }

        [HttpPost]
        public IActionResult SaveGoogleMappings([FromBody] List<FieldMappingDto> mappings, int storeId)
        {
            if (mappings == null) mappings = new List<FieldMappingDto>();

            var oldMappings = _context.GoogleFieldMappings
                .Where(x => x.StoreId == storeId)
                .ToList();
            _context.GoogleFieldMappings.RemoveRange(oldMappings);

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
            public string FieldName { get; set; }
            public string LocalName { get; set; }
        }



        [HttpPost]
        public async Task<IActionResult> SaveProductMapsFromFront([FromBody] List<ProductMapDto> productMaps)
        {
            if (productMaps == null || productMaps.Count == 0)
                return Json(new { success = false, message = "Brak productMaps" });

            int storeId = productMaps[0].StoreId;

            // 1. Najpierw normalizujemy ExternalId dla wszystkich DTO,
            //    żeby grupowanie i lookupy działały na już oczyszczonych wartościach.
            //
            //    Format z XML: "55-431" gdzie 55 = productId, 431 = wariant rozmiaru.
            //    Wszystkie warianty rozmiaru tego samego produktu mają tę samą cenę,
            //    więc monitorujemy tylko bazowe productId (część przed pierwszym myślnikiem).
            foreach (var pmDto in productMaps)
            {
                pmDto.ExternalId = NormalizeExternalId(pmDto.ExternalId);
            }

            // 2. Zabezpieczenie przed duplikatami z FRONTENDU - po normalizacji ExternalId
            //    wiele wariantów (28-S, 28-M, 28-L) zlepi się w jeden klucz (28, url),
            //    więc bierzemy tylko pierwszy z każdej grupy.
            var uniqueProductMaps = productMaps
                .GroupBy(pm => new { pm.ExternalId, pm.Url })
                .Select(g => g.First())
                .ToList();

            // 3. Pobierz istniejące rekordy z bazy
            var existingList = await _context.ProductMaps
                .Where(pm => pm.StoreId == storeId)
                .ToListAsync();

            // 3a. Główny słownik wyszukiwania - po (ExternalId, Url)
            var existingByKey = existingList
                .GroupBy(pm => new { pm.ExternalId, pm.Url })
                .ToDictionary(g => g.Key, g => g.First());

            // 3b. Dodatkowy lookup po samym Url - łapie przypadki gdzie zmienił się parser ExternalId
            //     (stary rekord ma "60431", nowy przychodzi z "60", ale URL ten sam)
            var existingByUrl = existingList
                .Where(pm => !string.IsNullOrEmpty(pm.Url))
                .GroupBy(pm => pm.Url)
                .ToDictionary(g => g.Key, g => g.First());

            int added = 0, updated = 0, fixedIds = 0;

            foreach (var pmDto in uniqueProductMaps)
            {
                var key = new { ExternalId = pmDto.ExternalId, Url = pmDto.Url };
                ProductMap found = null;

                // Próba 1: dokładne dopasowanie po (ExternalId, Url)
                if (existingByKey.TryGetValue(key, out var byKey))
                {
                    found = byKey;
                }
                // Próba 2: fallback po samym URL - łapie stare rekordy z błędnym ExternalId
                else if (!string.IsNullOrEmpty(pmDto.Url) && existingByUrl.TryGetValue(pmDto.Url, out var byUrl))
                {
                    found = byUrl;

                    // Naprawiamy stary, zepsuty ExternalId
                    if (found.ExternalId != pmDto.ExternalId)
                    {
                        found.ExternalId = pmDto.ExternalId;
                        fixedIds++;
                    }
                }

                if (found != null)
                {
                    // Aktualizacja istniejącego
                    found.GoogleEan = pmDto.GoogleEan;
                    found.GoogleImage = pmDto.GoogleImage;
                    found.GoogleExportedName = pmDto.GoogleExportedName;
                    found.GoogleExportedProducer = pmDto.GoogleExportedProducer;
                    found.GoogleXMLPrice = pmDto.GoogleXMLPrice;
                    found.GoogleDeliveryXMLPrice = pmDto.GoogleDeliveryXMLPrice;
                    found.GoogleExportedProducerCode = pmDto.GoogleExportedProducerCode;
                    found.OtherVariantEans = pmDto.OtherVariantEans;
                    updated++;
                }
                else
                {
                    // Dodawanie nowego
                    var newMap = new ProductMap
                    {
                        StoreId = pmDto.StoreId,
                        ExternalId = pmDto.ExternalId,
                        Url = pmDto.Url,
                        GoogleEan = pmDto.GoogleEan,
                        GoogleImage = pmDto.GoogleImage,
                        GoogleExportedName = pmDto.GoogleExportedName,
                        GoogleExportedProducer = pmDto.GoogleExportedProducer,
                        GoogleXMLPrice = pmDto.GoogleXMLPrice,
                        GoogleDeliveryXMLPrice = pmDto.GoogleDeliveryXMLPrice,
                        GoogleExportedProducerCode = pmDto.GoogleExportedProducerCode,
                        OtherVariantEans = pmDto.OtherVariantEans,
                    };

                    _context.ProductMaps.Add(newMap);
                    added++;
                }
            }

            await _context.SaveChangesAsync();
            return Json(new
            {
                success = true,
                message = $"Dodano {added}, zaktualizowano {updated}, naprawiono ID: {fixedIds}."
            });
        }

        /// <summary>
        /// Normalizuje ExternalId z formatu XML do bazowego productId.
        /// Format wejściowy: "55-431", "70-143", "28-S" → bierzemy część przed pierwszym myślnikiem.
        /// Następnie zostawiamy tylko cyfry (na wypadek prefiksów typu "ABC123" lub spacji).
        /// </summary>
        private static string NormalizeExternalId(string rawExternalId)
        {
            if (string.IsNullOrEmpty(rawExternalId))
                return rawExternalId;

            var trimmed = rawExternalId.Trim();

            // Bierzemy tylko część przed pierwszym myślnikiem
            var dashIndex = trimmed.IndexOf('-');
            if (dashIndex > 0)
            {
                trimmed = trimmed.Substring(0, dashIndex);
            }

            // Zostawiamy tylko cyfry
            return new string(trimmed.Where(char.IsDigit).ToArray());
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
            public string? GoogleExportedProducerCode { get; set; }
            public string? OtherVariantEans { get; set; }

        }

    }
}