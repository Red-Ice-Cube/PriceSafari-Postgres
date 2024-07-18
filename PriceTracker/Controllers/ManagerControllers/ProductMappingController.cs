using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Net.Http;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class ProductMappingController : Controller
    {
        private readonly PriceTrackerContext _context;

        public ProductMappingController(PriceTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();
            return View("~/Views/ManagerPanel/ProductMapping/Index.cshtml", stores);
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

                // Grupowanie produktów według EAN i wybieranie najtańszego
                var products = xml.Descendants("o")
                    .GroupBy(x => x.Descendants("a")
                        .FirstOrDefault(a => a.Attribute("name")?.Value == "EAN")?.Value)
                    .Select(g => g.OrderBy(x => decimal.TryParse(x.Attribute("price")?.Value, out var price) ? price : decimal.MaxValue).First())
                    .Select(x => new ProductMap
                    {
                        StoreId = storeId,
                        ExternalId = x.Attribute("id")?.Value,
                        Url = x.Attribute("url")?.Value,
                        Name = x.Element("name")?.Value,
                        CatalogNumber = x.Descendants("a")
                                         .FirstOrDefault(a => a.Attribute("name")?.Value == "Kod_producenta")?.Value,
                        Ean = x.Descendants("a")
                               .FirstOrDefault(a => a.Attribute("name")?.Value == "EAN")?.Value,
                        MainUrl = x.Descendants("main").FirstOrDefault()?.Attribute("url")?.Value
                    }).ToList();

                foreach (var product in products)
                {
                    var existingProduct = await _context.ProductMaps
                        .FirstOrDefaultAsync(p => p.Ean == product.Ean && p.StoreId == storeId);

                    if (existingProduct != null)
                    {
                        existingProduct.Url = product.Url;
                        existingProduct.Name = product.Name;
                        existingProduct.CatalogNumber = product.CatalogNumber;
                        existingProduct.MainUrl = product.MainUrl;
                    }
                    else
                    {
                        var newProduct = new ProductMap
                        {
                            StoreId = storeId,
                            ExternalId = product.ExternalId,
                            Url = product.Url,
                            Name = product.Name,
                            CatalogNumber = product.CatalogNumber,
                            Ean = product.Ean,
                            MainUrl = product.MainUrl
                        };
                        await _context.ProductMaps.AddAsync(newProduct);
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
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

            ViewBag.StoreName = store.StoreName;
            return View("~/Views/ManagerPanel/ProductMapping/MappedProducts.cshtml", mappedProducts);
        }
    }
}
