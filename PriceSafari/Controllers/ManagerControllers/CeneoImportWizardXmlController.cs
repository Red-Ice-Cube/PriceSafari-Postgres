using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Linq;
using PriceSafari.Models.ProductXML;
using System.Text.Json;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class CeneoImportWizardXmlController : Controller
    {
        private readonly PriceSafariContext _context;

        public CeneoImportWizardXmlController(PriceSafariContext context)
        {
            _context = context;
        }

       
        [HttpGet]
        public IActionResult ShowCeneoFeedXml(int storeId)
        {
            var store = _context.Stores.Find(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = "Sklep nie znaleziony.";
                return RedirectToAction("Index", "ProductMapping");
            }

            var existingMappings = _context.CeneoFieldMappings
              .Where(m => m.StoreId == storeId)
              .ToList();

            var existingMappingsJson = JsonSerializer.Serialize(existingMappings);
            ViewBag.StoreId = storeId;
            ViewBag.ExistingMappings = existingMappingsJson;

            return View("~/Views/ManagerPanel/ProductMapping/WizardCeneoFeedXml.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> ProxyXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
                return NotFound("Brak sklepu.");

            if (string.IsNullOrEmpty(store.ProductMapXmlUrl))
                return BadRequest("Brak URL do pliku w bazie.");

            var url = store.ProductMapXmlUrl;

            // Możesz dodać obsługę auto-redirect
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true // domyślnie True, ale można ustawić jawnie
            };
            using var client = new HttpClient(handler);

            // Opcjonalnie zmień User-Agent, jeśli strona wymaga
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyFeedBot/1.0)");

            try
            {
                var xmlContent = await client.GetStringAsync(url);

                // Zwracamy w formacie XML do frontu:
                return Content(xmlContent, "text/xml");
            }
            catch (Exception ex)
            {
                // Możesz przechwycić błąd i zwrócić np. BadRequest z komunikatem
                return BadRequest($"Błąd pobierania z {url}: {ex.Message}");
            }
        }


        
        [HttpGet]
        public IActionResult GetCeneoMappings(int storeId)
        {
            var existing = _context.CeneoFieldMappings
                .Where(m => m.StoreId == storeId)
                .ToList();
            return Json(existing);
        }

      
        [HttpPost]
        public IActionResult SaveCeneoMappings([FromBody] List<FieldMappingDto> mappings, int storeId)
        {
            if (mappings == null) mappings = new List<FieldMappingDto>();

            // Usuń stare
            var oldMappings = _context.CeneoFieldMappings
                .Where(x => x.StoreId == storeId)
                .ToList();
            _context.CeneoFieldMappings.RemoveRange(oldMappings);

            // Dodaj nowe
            foreach (var m in mappings)
            {
                _context.CeneoFieldMappings.Add(new CeneoFieldMapping
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
        public IActionResult ClearCeneoMappings(int storeId)
        {
            var toRemove = _context.CeneoFieldMappings
                .Where(x => x.StoreId == storeId)
                .ToList();

            if (toRemove.Any())
            {
                _context.CeneoFieldMappings.RemoveRange(toRemove);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Usunięto mapowania (StoreId={storeId}).";
            }
            else
            {
                TempData["InfoMessage"] = "Nie było żadnych mapowań do usunięcia.";
            }

            return RedirectToAction("ShowCeneoFeedXml", new { storeId });
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
                // Oczyszczanie ExternalId, by zostały tylko cyfry
                if (!string.IsNullOrEmpty(pmDto.ExternalId))
                {
                    pmDto.ExternalId = new string(pmDto.ExternalId.Where(c => char.IsDigit(c)).ToArray());
                }

                // Tylko jeśli URL i ExternalId są identyczne
                var found = existing.FirstOrDefault(x =>
                    x.Url == pmDto.Url
                    && x.ExternalId == pmDto.ExternalId
                );

                if (found == null)
                {
                    // Nowy wpis
                    var newMap = new ProductMap
                    {
                        StoreId = pmDto.StoreId,
                        ExternalId = pmDto.ExternalId,
                        Url = pmDto.Url,

                        // Pola Ceneo
                        Ean = pmDto.CeneoEan,
                        MainUrl = pmDto.CeneoImage,
                        ExportedName = pmDto.CeneoExportedName
                    };

                    _context.ProductMaps.Add(newMap);
                    existing.Add(newMap);
                    added++;
                }
                else
                {
                    // Aktualizacja 
                    found.ExternalId = pmDto.ExternalId;
                    found.Url = pmDto.Url;
                    found.Ean = pmDto.CeneoEan;
                    found.MainUrl = pmDto.CeneoImage;
                    found.ExportedName = pmDto.CeneoExportedName;

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
            public string CeneoEan { get; set; }
            public string CeneoImage { get; set; }
            public string CeneoExportedName { get; set; }
        }



    }
}
