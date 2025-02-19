using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PriceSafari.Data;
using System.Xml.Linq;

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
        public async Task<IActionResult> ShowGoogleFeedXml(int storeId)
        {
            var store = _context.Stores.Find(storeId);
            if (store == null)
            {
                TempData["ErrorMessage"] = "Sklep nie znaleziony.";
                return RedirectToAction("Index", "ProductMapping");
            }

            // Nie pobieramy tu pliku; tylko przekazujemy storeId
            // i w widoku użyjemy ProxyXml
            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/ProductMapping/WizardGoogleFeedXml.cshtml");
        }

        // Endpoint-proxy
        [HttpGet]
        public async Task<IActionResult> ProxyXml(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
                return NotFound("Brak sklepu.");

            if (string.IsNullOrEmpty(store.ProductMapXmlUrlGoogle))
                return BadRequest("Brak URL do pliku w bazie.");

            using var client = new HttpClient();
            var xmlContent = await client.GetStringAsync(store.ProductMapXmlUrlGoogle);

            return Content(xmlContent, "text/xml");
        }


        [HttpPost]
        public IActionResult SaveGoogleMappings([FromBody] List<FieldMappingDto> mappings, int storeId)
        {
            // Wypisujemy w konsoli
            Console.WriteLine($"Zapis mapowań dla storeId={storeId}:");
            foreach (var m in mappings)
            {
                Console.WriteLine($"  {m.FieldName} => {m.LocalName}");
            }

            // Ewentualnie zapisz do bazy, itp.

            return Json(new { success = true, message = "Mapowania zapisane." });
        }

        public class FieldMappingDto
        {
            public string FieldName { get; set; }       // "GoogleEan", "ExternalId" itd.
            public string LocalName { get; set; }       // np. "g:id", "g:title"
                                                        // lub "Path" - jeśli używasz pełnych ścieżek
        }

    }
}
