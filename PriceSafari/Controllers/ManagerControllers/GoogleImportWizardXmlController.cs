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
    }
}
