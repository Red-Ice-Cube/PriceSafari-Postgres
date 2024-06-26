using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HtmlAgilityPack;
using PriceTracker.Hubs;
using PriceTracker.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Heat_Lead.Data;

namespace PriceTracker.Controllers
{
    public class StoreController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public StoreController(Heat_LeadContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }
        [HttpGet]
        public IActionResult CreateStore()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateStore(string storeName, string storeProfile)
        {
            if (string.IsNullOrEmpty(storeName) || string.IsNullOrEmpty(storeProfile))
            {
                return BadRequest("Store name and profile are required.");
            }

            var store = new StoreClass
            {
                StoreName = storeName,
                StoreProfile = storeProfile
            };

            _context.Stores.Add(store);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }


        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();
            return View(stores);
        }

        [HttpGet]
        public async Task<IActionResult> ScrapeProducts(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var baseUrl = store.StoreProfile;
            var web = new HtmlWeb();
            var doc = web.Load(baseUrl);

            // Pobieramy liczbę stron
            var pageCountNode = doc.DocumentNode.SelectSingleNode("//input[@id='page-counter']");
            int pageCount = 1;
            if (pageCountNode != null)
            {
                int.TryParse(pageCountNode.GetAttributeValue("data-pagecount", "1"), out pageCount);
            }

            var existingProductUrls = _context.Products.Where(p => p.StoreId == storeId).Select(p => p.OfferUrl).ToHashSet();
            int totalScraped = 0;
            int totalProducts = 0;
            HashSet<string> newProductUrls = new HashSet<string>();

            for (int page = 0; page < pageCount; page++)
            {
                var url = $"{baseUrl.Replace("0-0-0-0.htm", $"0-0-0-{page}.htm")}";
                doc = web.Load(url);

                var products = doc.DocumentNode.SelectNodes("//div[contains(@class, 'cat-prod-row')]");
                if (products != null && products.Count > 0)
                {
                    foreach (var product in products)
                    {
                        var nameNode = product.SelectSingleNode(".//strong[contains(@class, 'cat-prod-row__name')]//a");
                        var categoryNode = product.SelectSingleNode(".//div[contains(@class, 'cat-prod-row__category')]//a");
                        var pid = product.GetAttributeValue("data-pid", "");

                        if (nameNode != null && categoryNode != null && !string.IsNullOrEmpty(pid))
                        {
                            var name = nameNode.InnerText.Trim();
                            var category = categoryNode.InnerText.Trim();
                            var fullOfferUrl = "https://www.ceneo.pl/" + pid;

                            if (existingProductUrls.Contains(fullOfferUrl) || newProductUrls.Contains(fullOfferUrl))
                            {
                                continue;
                            }

                            var productEntity = new ProductClass
                            {
                                StoreId = storeId,
                                ProductName = name,
                                Category = category,
                                OfferUrl = fullOfferUrl
                            };

                            _context.Products.Add(productEntity);
                            newProductUrls.Add(fullOfferUrl);
                        }
                        totalScraped++;
                        await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, newProductUrls.Count, page + 1, pageCount, storeId);
                    }

                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction("ProductList", new { storeId = storeId });
        }



        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
            var categories = products.Select(p => p.Category).Distinct().ToList();

            ViewBag.StoreName = store.StoreName;
            ViewBag.Categories = categories;
            ViewBag.StoreId = storeId;

            return View(products);
        }
    }
}
