using HtmlAgilityPack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
using PriceSafari.Services.ScheduleService;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class StoreController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;

        private readonly UrlGroupingService _urlGroupingService;

        public StoreController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, UrlGroupingService urlGroupingService)
        {
            _context = context;
            _hubContext = hubContext;
            _urlGroupingService = urlGroupingService;

        }

        [HttpGet]
        public IActionResult CreateStore()
        {
            return View("~/Views/ManagerPanel/Store/CreateStore.cshtml");
        }

        [HttpPost]
        public async Task<IActionResult> CreateStore(string storeName, string storeProfile, string? apiUrl, string? apiKey, string? logoUrl, int? productPack)
        {
            if (string.IsNullOrEmpty(storeName) || string.IsNullOrEmpty(storeProfile))
            {
                return BadRequest("Store name and profile are required.");
            }

            var store = new StoreClass
            {
                StoreName = storeName,
                StoreProfile = storeProfile,
                StoreApiUrl = apiUrl,
                StoreApiKey = apiKey,
                StoreLogoUrl = logoUrl,
                ProductsToScrap = productPack
            };

            _context.Stores.Add(store);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> EditStore(int storeId)
        {

            var store = await _context.Stores
                .Include(s => s.PaymentData)
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null)
            {
                return NotFound();
            }

            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName", store.PlanId);

            return View("~/Views/ManagerPanel/Store/EditStore.cshtml", store);
        }

        [HttpPost]
        public async Task<IActionResult> EditStore(int storeId, StoreClass store)
        {
            if (storeId != store.StoreId)
            {
                return BadRequest("Mismatched Store ID");
            }

            ModelState.Remove("Plan");
            ModelState.Remove("Invoices");
            ModelState.Remove("ScrapHistories");
            ModelState.Remove("AllegroProducts");

            ModelState.Remove("PaymentData.Store");

            bool isPaymentDataEmpty = store.PaymentData == null ||
                                      (string.IsNullOrWhiteSpace(store.PaymentData.CompanyName) &&
                                       string.IsNullOrWhiteSpace(store.PaymentData.NIP));

            if (isPaymentDataEmpty)
            {
                foreach (var key in ModelState.Keys.Where(k => k.StartsWith("PaymentData")).ToList())
                {
                    ModelState.Remove(key);
                }
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors);
                foreach (var error in errors)
                {
                    Console.WriteLine($"!!! BŁĄD WALIDACJI: {error.ErrorMessage} (Exception: {error.Exception?.Message})");
                }

                var plans = await _context.Plans.ToListAsync();
                ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName", store.PlanId);
                return View("~/Views/ManagerPanel/Store/EditStore.cshtml", store);
            }

            var existingStore = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .Include(s => s.Invoices)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (existingStore == null)
                return NotFound();

            existingStore.StoreName = store.StoreName;
            existingStore.StoreProfile = store.StoreProfile;
            existingStore.StoreApiUrl = store.StoreApiUrl;
            existingStore.StoreApiKey = store.StoreApiKey;
            existingStore.StoreLogoUrl = store.StoreLogoUrl;
            existingStore.ProductMapXmlUrl = store.ProductMapXmlUrl;
            existingStore.ProductMapXmlUrlGoogle = store.ProductMapXmlUrlGoogle;
            existingStore.DiscountPercentage = store.DiscountPercentage ?? 0;
            existingStore.RemainingDays = store.RemainingDays;
            existingStore.ProductsToScrap = store.ProductsToScrap;

            existingStore.StoreNameAllegro = store.StoreNameAllegro;
            existingStore.StoreNameGoogle = store.StoreNameGoogle;
            existingStore.StoreNameCeneo = store.StoreNameCeneo;
            existingStore.FetchExtendedData = store.FetchExtendedData;
            existingStore.StoreSystemType = store.StoreSystemType;
            existingStore.UseGPID = store.UseGPID;
            existingStore.UseWRGA = store.UseWRGA;
            existingStore.CollectGoogleStoreLinks = store.CollectGoogleStoreLinks;
            existingStore.UseGoogleXMLFeedPrice = store.UseGoogleXMLFeedPrice;
            existingStore.UseCeneoXMLFeedPrice = store.UseCeneoXMLFeedPrice;
            existingStore.OnCeneo = store.OnCeneo;
            existingStore.OnGoogle = store.OnGoogle;
            existingStore.OnAllegro = store.OnAllegro;
            existingStore.ProductsToScrapAllegro = store.ProductsToScrapAllegro;

            existingStore.IsAllegroPriceBridgeActive = store.IsAllegroPriceBridgeActive;
            existingStore.FetchExtendedAllegroData = store.FetchExtendedAllegroData;
            existingStore.IsPayingCustomer = store.IsPayingCustomer;
            existingStore.SubscriptionStartDate = store.SubscriptionStartDate;

            if (!isPaymentDataEmpty && store.PaymentData != null)
            {

                if (existingStore.PaymentData == null)
                {
                    existingStore.PaymentData = new UserPaymentData();
                }

                existingStore.PaymentData.CompanyName = store.PaymentData.CompanyName;
                existingStore.PaymentData.NIP = store.PaymentData.NIP;
                existingStore.PaymentData.Address = store.PaymentData.Address;
                existingStore.PaymentData.PostalCode = store.PaymentData.PostalCode;
                existingStore.PaymentData.City = store.PaymentData.City;
                existingStore.PaymentData.InvoiceAutoMail = store.PaymentData.InvoiceAutoMail;

                if (string.IsNullOrWhiteSpace(store.PaymentData.InvoiceAutoMail))
                {
                    existingStore.PaymentData.InvoiceAutoMailSend = false;
                }
                else
                {
                    existingStore.PaymentData.InvoiceAutoMailSend = store.PaymentData.InvoiceAutoMailSend;
                }
            }
            else if (isPaymentDataEmpty && existingStore.PaymentData != null)
            {

                _context.Remove(existingStore.PaymentData);
                existingStore.PaymentData = null;
            }

            if (existingStore.AllegroRefreshToken != store.AllegroRefreshToken)
            {

                if (!string.IsNullOrEmpty(store.AllegroRefreshToken))
                {
                    existingStore.AllegroRefreshToken = store.AllegroRefreshToken
                        .Replace(" ", "")
                        .Replace("\r", "")
                        .Replace("\n", "")
                        .Trim();

                    existingStore.IsAllegroTokenActive = true;

                    existingStore.AllegroApiToken = null;
                }
                else
                {
                    existingStore.AllegroRefreshToken = null;
                    existingStore.IsAllegroTokenActive = false;
                }
            }

            if (existingStore.PlanId != store.PlanId)
            {
                existingStore.PlanId = store.PlanId;
                var newPlan = await _context.Plans.FindAsync(store.PlanId);

                if (newPlan != null)
                {
                    existingStore.ProductsToScrap = newPlan.ProductsToScrap;
                    if (newPlan.NetPrice == 0 || newPlan.IsTestPlan)
                    {
                        existingStore.RemainingDays = newPlan.DaysPerInvoice;

                        if (existingStore.Invoices != null)
                        {
                            var unpaidInvoices = existingStore.Invoices.Where(i => !i.IsPaid).ToList();
                            foreach (var invoice in unpaidInvoices)
                            {
                                invoice.IsPaid = true;
                            }
                        }
                    }
                    else
                    {
                        existingStore.RemainingDays = 0;
                    }
                }
                else
                {
                    existingStore.ProductsToScrap = null;
                }
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .Include(s => s.ScrapHistories)
                .ToListAsync();

            var lastScrapDates = stores
                .Select(s => new
                {
                    StoreId = s.StoreId,
                    LastScrapDate = s.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date
                })
                .ToDictionary(x => x.StoreId, x => (DateTime?)x.LastScrapDate);

            ViewBag.LastScrapDates = lastScrapDates;

            return View("~/Views/ManagerPanel/Store/Index.cshtml", stores);
        }

        [HttpGet]
        public async Task<IActionResult> ScrapeProducts(int storeId, int depth)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var categories = await _context.Categories
                .Where(c => c.StoreId == storeId && c.Depth == depth)
                .ToListAsync();

            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {

                settings = new Settings
                {
                    HeadLess = true,
                    JavaScript = true,
                    Styles = false,
                    WarmUpTime = 5
                };
            }

            using (var productScraper = new ProductScraper(_context, _hubContext, settings))
            {

                await productScraper.NavigateToCaptchaAsync();

                await productScraper.WaitForCaptchaSolutionAsync();

                foreach (var category in categories)
                {
                    var baseUrlTemplate = $"https://www.ceneo.pl/{category.CategoryUrl};0192;{store.StoreProfile}-0v;0020-15-0-0-{{0}}.htm";
                    await productScraper.ScrapeCategoryProducts(storeId, category.CategoryName, baseUrlTemplate);
                }
            }

            return RedirectToAction("ProductList", new { storeId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStore(int storeId)
        {
            bool storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
            {
                Console.WriteLine($"Próba usunięcia Store o ID={storeId}, ale nie znaleziono go w bazie.");
                return NotFound();
            }

            _context.Database.SetCommandTimeout(300);

            Console.WriteLine($"Rozpoczynam usuwanie Store o ID={storeId}...");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int chunkSize = 100;

                Console.WriteLine($"Rozpoczynam usuwanie [ProductFlags] dla StoreId={storeId}...");
                int totalProductFlagsDeleted = 0;
                while (true)
                {
                    int deleted = await _context.Database.ExecuteSqlRawAsync($@"
                DELETE TOP({chunkSize}) PF 
                FROM [ProductFlags] AS PF
                JOIN [Flags] AS F ON PF.FlagId = F.FlagId
                WHERE F.StoreId = @storeId",
                        new SqlParameter("@storeId", storeId)
                    );
                    totalProductFlagsDeleted += deleted;
                    if (deleted == 0) break;
                    Console.WriteLine($"[ProductFlags] - usunięto {deleted} rekordów (łącznie {totalProductFlagsDeleted}).");
                }

                Console.WriteLine($"Rozpoczynam usuwanie [AllegroOffersToScrape] dla StoreId={storeId}...");
                int totalOffersDeleted = 0;
                while (true)
                {
                    int deleted = await _context.Database.ExecuteSqlRawAsync($@"
                ;WITH OffersToDelete AS (
                    SELECT TOP({chunkSize}) AOTS.Id
                    FROM AllegroOffersToScrape AS AOTS
                    CROSS APPLY (
                        SELECT CAST('<id>' + REPLACE(LTRIM(RTRIM(REPLACE(REPLACE(AOTS.AllegroProductIds, '[', ''), ']', ''))), ',', '</id><id>') + '</id>' AS XML) AS ProductIdsXml
                    ) AS XmlData
                    CROSS APPLY (
                        SELECT n.value('.', 'int') AS ProductId
                        FROM XmlData.ProductIdsXml.nodes('/id') AS t(n)
                    ) AS ParsedIds
                    WHERE ParsedIds.ProductId IN (
                        SELECT AP.AllegroProductId FROM AllegroProducts AS AP WHERE AP.StoreId = @storeId
                    ) AND AOTS.AllegroProductIds != '[]' AND AOTS.AllegroProductIds IS NOT NULL
                )
                DELETE FROM AllegroOffersToScrape 
                WHERE Id IN (SELECT Id FROM OffersToDelete);",
                        new SqlParameter("@storeId", storeId)
                    );
                    totalOffersDeleted += deleted;
                    if (deleted == 0) break;
                    Console.WriteLine($"[AllegroOffersToScrape] - usunięto {deleted} rekordów (łącznie {totalOffersDeleted}).");
                }

                Console.WriteLine($"Zabezpieczanie faktur dla StoreId={storeId} (archiwizacja nazwy i odpięcie)...");
                int securedInvoices = await _context.Database.ExecuteSqlRawAsync(@"
            UPDATE Invoices 
            SET 
                ArchivedStoreName = (SELECT StoreName FROM Stores WHERE Stores.StoreId = Invoices.StoreId),
                StoreId = NULL 
            WHERE StoreId = @storeId",
                    new SqlParameter("@storeId", storeId)
                );
                Console.WriteLine($"Zaktualizowano {securedInvoices} faktur (zachowano w systemie).");

                Console.WriteLine("Usuwanie powiązań z nowych modułów (Automatyzacje, Mosty Cenowe)...");

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE APA FROM [AutomationProductAssignments] APA
            INNER JOIN [AutomationRules] AR ON APA.AutomationRuleId = AR.Id
            WHERE AR.StoreId = @storeId", new SqlParameter("@storeId", storeId));
                await DeleteInChunksAsync("AutomationRules", "StoreId", storeId, chunkSize);

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE ABI FROM [AllegroPriceBridgeItems] ABI
            INNER JOIN [AllegroPriceBridgeBatches] ABB ON ABI.AllegroPriceBridgeBatchId = ABB.Id
            WHERE ABB.StoreId = @storeId", new SqlParameter("@storeId", storeId));
                await DeleteInChunksAsync("AllegroPriceBridgeBatches", "StoreId", storeId, chunkSize);

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE PBI FROM [PriceBridgeItems] PBI
            INNER JOIN [PriceBridgeBatches] PBB ON PBI.PriceBridgeBatchId = PBB.Id
            WHERE PBB.StoreId = @storeId", new SqlParameter("@storeId", storeId));
                await DeleteInChunksAsync("PriceBridgeBatches", "StoreId", storeId, chunkSize);

                Console.WriteLine("Usuwanie mapowań sklepu...");
                await DeleteInChunksAsync("GoogleFieldMappings", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("CeneoFieldMappings", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("ProductMaps", "StoreId", storeId, chunkSize);

                Console.WriteLine("Usuwanie rozszerzonych logów ze scrapowania...");

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE PHEI FROM [PriceHistoryExtendedInfos] PHEI
            INNER JOIN [ScrapHistories] SH ON PHEI.ScrapHistoryId = SH.Id
            WHERE SH.StoreId = @storeId", new SqlParameter("@storeId", storeId));

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE APHEI FROM [AllegroPriceHistoryExtendedInfos] APHEI
            INNER JOIN [AllegroScrapeHistories] ASH ON APHEI.ScrapHistoryId = ASH.Id
            WHERE ASH.StoreId = @storeId", new SqlParameter("@storeId", storeId));

                Console.WriteLine("Usuwanie historii cen Allegro paczkami...");
                while (true)
                {
                    int deleted = await _context.Database.ExecuteSqlRawAsync($@"
                DELETE TOP({chunkSize}) APH 
                FROM [AllegroPriceHistories] APH
                INNER JOIN [AllegroProducts] AP ON APH.AllegroProductId = AP.AllegroProductId
                WHERE AP.StoreId = @storeId",
                        new SqlParameter("@storeId", storeId));
                    if (deleted == 0) break;
                    Console.WriteLine($"[AllegroPriceHistories] - usunięto {deleted} rekordów.");
                }

                Console.WriteLine("Usuwanie historii cen paczkami...");
                while (true)
                {
                    int deleted = await _context.Database.ExecuteSqlRawAsync($@"
                DELETE TOP({chunkSize}) PH 
                FROM [PriceHistories] PH
                INNER JOIN [Products] P ON PH.ProductId = P.ProductId
                WHERE P.StoreId = @storeId",
                        new SqlParameter("@storeId", storeId));
                    if (deleted == 0) break;
                    Console.WriteLine($"[PriceHistories] - usunięto {deleted} rekordów.");
                }

                Console.WriteLine("Usuwanie starych struktur (Produkty, ScrapHistories)...");

                await _context.Database.ExecuteSqlRawAsync($@"
            DELETE PGC FROM [ProductGoogleCatalogs] PGC
            INNER JOIN [Products] P ON PGC.ProductId = P.ProductId
            WHERE P.StoreId = @storeId", new SqlParameter("@storeId", storeId));

                await DeleteInChunksAsync("ScheduleTaskStores", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("UserPaymentDatas", "StoreId", storeId, chunkSize);

                await DeleteInChunksAsync("AllegroProducts", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("AllegroScrapeHistories", "StoreId", storeId, chunkSize);

                await DeleteInChunksAsync("PriceValues", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("Products", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("Categories", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("ScrapHistories", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("Flags", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("UserStores", "StoreId", storeId, chunkSize);
                await DeleteInChunksAsync("PriceSafariReports", "StoreId", storeId, chunkSize);

                int deletedStores = await _context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [Stores] WHERE StoreId = {0}",
                    storeId
                );
                Console.WriteLine($"Usunięto {deletedStores} rekord(ów) z tabeli [Stores].");

                await transaction.CommitAsync();

                Console.WriteLine($"Zakończono pomyślnie usuwanie Store o ID={storeId} wraz z powiązanymi danymi (faktury zachowano).");

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Wystąpił błąd podczas usuwania Store o ID={storeId}. Transakcja została wycofana. Błąd: {ex.Message}");

                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : "";
                TempData["ErrorMessage"] = $"Nie udało się usunąć sklepu. Błąd: {ex.Message} | Detale: {innerMsg}";

                return RedirectToAction("Index");
            }
        }

        private async Task<int> DeleteInChunksAsync(string tableName, string whereColumn, int storeId, int chunkSize)
        {
            int totalDeleted = 0;
            while (true)
            {
                int deleted = await _context.Database.ExecuteSqlRawAsync($@"
            DELETE TOP({chunkSize})
            FROM [{tableName}]
            WHERE [{whereColumn}] = @storeId",
                    new SqlParameter("@storeId", storeId)
                );

                totalDeleted += deleted;
                Console.WriteLine($"[{tableName}] - usunięto {deleted} rekordów (łącznie {totalDeleted}).");

                if (deleted == 0)
                    break;
            }
            return totalDeleted;
        }

        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .ToListAsync();

            var allProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var rejectedProducts = allProducts
                .Where(p => p.IsRejected && p.IsScrapable)
                .ToList();

            var categories = products.Select(p => p.Category).Distinct().ToList();

            ViewBag.StoreName = store.StoreName;
            ViewBag.Categories = categories;
            ViewBag.StoreId = storeId;
            ViewBag.AllProducts = allProducts;
            ViewBag.ScrapableProducts = products;
            ViewBag.RejectedProductsCount = rejectedProducts.Count;

            return View("~/Views/ManagerPanel/Store/ProductList.cshtml", products);
        }

        [HttpPost]
        public async Task<IActionResult> MergeStoreUrls(int storeId)
        {
            try
            {

                var result = await _urlGroupingService.GroupAndSaveUniqueUrls(new List<int> { storeId });

                TempData["SuccessMessage"] = $"Sukces! Przeanalizowano {result.totalProducts} produktów. Znaleziono unikalne sklepy: {string.Join(", ", result.distinctStoreNames)}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Błąd podczas scalania URL: {ex.Message}";
            }

            return RedirectToAction("ProductList", new { storeId });
        }

        [HttpPost]
        public async Task<IActionResult> ClearRejectedProducts(int storeId)
        {

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsRejected && p.IsScrapable)
                .ToListAsync();

            foreach (var product in products)
            {
                product.IsRejected = false;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("ProductList", new { storeId });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateInvoiceManually(int storeId)
        {

            var store = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null) return NotFound();

            if (store.PlanId == null || store.Plan == null)
            {
                TempData["ErrorMessage"] = "Nie można wygenerować faktury: Sklep nie ma przypisanego Planu.";
                return RedirectToAction("EditStore", new { storeId });
            }

            var currentYear = DateTime.Now.Year;
            var invoiceCounter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);

            if (invoiceCounter == null)
            {
                invoiceCounter = new InvoiceCounter { Year = currentYear, LastInvoiceNumber = 0, LastProformaNumber = 0 };
                _context.InvoiceCounters.Add(invoiceCounter);
                await _context.SaveChangesAsync();
            }

            invoiceCounter.LastInvoiceNumber++;
            string invNum = $"PS/{invoiceCounter.LastInvoiceNumber:D6}/sDB/{DateTime.Now.Year}";

            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
            {
                appliedDiscountPercentage = store.DiscountPercentage.Value;
                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                netPrice = netPrice - appliedDiscountAmount;
            }

            var invoice = new InvoiceClass
            {
                StoreId = store.StoreId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(14),
                IsPaid = false,

                IsPaidByCard = false,
                InvoiceNumber = invNum,
                NetAmount = netPrice,
                DaysIncluded = store.Plan.DaysPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,

                CompanyName = store.PaymentData?.CompanyName ?? "Brak Danych",
                Address = store.PaymentData?.Address ?? "",
                PostalCode = store.PaymentData?.PostalCode ?? "",
                City = store.PaymentData?.City ?? "",
                NIP = store.PaymentData?.NIP ?? "",
                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount,
                IsSentByEmail = false

            };

            store.RemainingDays += store.Plan.DaysPerInvoice;
            store.ProductsToScrap = store.Plan.ProductsToScrap;
            store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

            _context.Invoices.Add(invoice);

            _context.TaskExecutionLogs.Add(new TaskExecutionLog
            {
                DeviceName = "ManagerPanel",
                OperationName = "FAKTURA_RECZNA",
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                Comment = $"Admin wygenerował ręcznie FV: {invNum} dla sklepu ID: {store.StoreId}. Dodano {store.Plan.DaysPerInvoice} dni."
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Wygenerowano fakturę {invNum} i dodano {store.Plan.DaysPerInvoice} dni.";

            return RedirectToAction("EditStore", new { storeId });
        }

    }
}