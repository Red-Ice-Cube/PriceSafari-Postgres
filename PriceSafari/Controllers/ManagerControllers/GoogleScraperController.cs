using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;
using System.Collections.Concurrent;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

[Authorize(Roles = "Admin")]
public class GoogleScraperController : Controller
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INetworkControlService _networkControlService;
    private readonly PriceSafariContext _context;

    private static readonly Random _random = new Random();
    private static readonly object _lockMasterListInit = new object();
    private static readonly object _lockTimer = new object();
    private static ConcurrentDictionary<int, ProductProcessingState> _masterProductStateList =
        new ConcurrentDictionary<int, ProductProcessingState>();

    private static System.Threading.Timer _batchSaveTimer;
    private static volatile bool _isScrapingActive = false;
    private static readonly SemaphoreSlim _timerCallbackSemaphore = new SemaphoreSlim(1, 1);
    private const int MAX_TASK_RETRIES = 3;
    private static readonly object _consoleLock = new object();
    private static readonly ConcurrentDictionary<int, byte> _enqueuedProductIds = new();
    private static readonly TimeSpan PROCESSING_TIMEOUT = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<int, DateTime> _processingStartedAt = new();
    private static readonly ConcurrentDictionary<int, Dictionary<string, int>> _eligibleProductsMapCache = new();
    public GoogleScraperController(IServiceScopeFactory scopeFactory, INetworkControlService networkControlService, PriceSafariContext context)
    {
        _scopeFactory = scopeFactory;
        _networkControlService = networkControlService;
        _context = context;
    }

    public enum ProductStatus
    {
        Pending, Processing, Found, NotFound, Error, CaptchaHalt
    }

    public class ProductProcessingState
    {
        public int ProductId { get; set; }
        public int StoreId { get; set; }
        public string OriginalUrl { get; set; }
        public string CleanedUrl { get; set; }
        public string ProductNameInStoreForGoogle { get; set; }

        public string Ean { get; set; }
        public string OtherVariantEans { get; set; }
        public string ProducerCode { get; set; }

        private ProductStatus _status;
        private string _googleUrl;
        private string _cid;
        private string _googleGid;
        private string _googleHid;
        public ProductStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    IsDirty = true;
                }
            }
        }

        public string GoogleUrl
        {
            get => _googleUrl;
            set
            {
                if (_googleUrl != value)
                {
                    _googleUrl = value;
                    IsDirty = true;
                }
            }
        }

        public string Cid
        {
            get => _cid;
            set
            {
                if (_cid != value)
                {
                    _cid = value;
                    IsDirty = true;
                }
            }
        }

        public string GoogleGid
        {
            get => _googleGid;
            set
            {
                if (_googleGid != value)
                {
                    _googleGid = value;
                    IsDirty = true;
                }
            }
        }

        public string GoogleHid
        {
            get => _googleHid;
            set
            {
                if (_googleHid != value)
                {
                    _googleHid = value;
                    IsDirty = true;
                }
            }
        }

        private string _googleVariant;
        public string GoogleVariant
        {
            get => _googleVariant;
            set
            {
                if (_googleVariant != value)
                {
                    _googleVariant = value;
                    IsDirty = true;
                }
            }
        }

        private string _googleVariantCode;
        public string GoogleVariantCode
        {
            get => _googleVariantCode;
            set
            {
                if (_googleVariantCode != value)
                {
                    _googleVariantCode = value;
                    IsDirty = true;
                }
            }
        }

        public int? ProcessingByTaskId { get; set; }
        public bool IsDirty { get; set; }

        public ConcurrentBag<ProductGoogleCatalog> NewCatalogsFound { get; set; } = new ConcurrentBag<ProductGoogleCatalog>();

        public HashSet<string> KnownCids { get; set; } = new HashSet<string>();

        public ProductProcessingState(ProductClass product, Func<string, string> cleanUrlFunc)
        {
            ProductId = product.ProductId;
            StoreId = product.StoreId;
            OriginalUrl = product.Url;

            CleanedUrl = OriginalUrl;

            ProductNameInStoreForGoogle = product.ProductNameInStoreForGoogle;
            Ean = product.Ean;
            OtherVariantEans = product.OtherVariantEans;
            ProducerCode = product.ProducerCode;

            if (product.FoundOnGoogle == true)
            {
                _status = ProductStatus.Found;
                _googleUrl = product.GoogleUrl;
                _googleGid = product.GoogleGid;
                _googleHid = product.GoogleHid;
                _googleVariant = product.GoogleVariant;
                _googleVariantCode = product.GoogleVariantCode;
            }
            else if (product.FoundOnGoogle == false)
            {
                _status = ProductStatus.NotFound;
            }
            else
            {
                _status = ProductStatus.Pending;
            }

            IsDirty = false;

            string? mainCid = ExtractCidFromUrl(product.GoogleUrl);
            if (!string.IsNullOrEmpty(mainCid))
            {
                KnownCids.Add(mainCid);
            }

            if (!string.IsNullOrEmpty(product.GoogleGid))
            {
                KnownCids.Add(product.GoogleGid);
            }

            if (product.GoogleCatalogs != null)
            {
                foreach (var catalog in product.GoogleCatalogs)
                {

                    if (!string.IsNullOrEmpty(catalog.GoogleCid))
                    {
                        KnownCids.Add(catalog.GoogleCid);
                    }

                    if (!string.IsNullOrEmpty(catalog.GoogleHid))
                    {
                        KnownCids.Add(catalog.GoogleHid);
                    }

                    if (!string.IsNullOrEmpty(catalog.GoogleGid))
                    {
                        KnownCids.Add(catalog.GoogleGid);
                    }
                }
            }
        }

        public void UpdateStatus(ProductStatus newStatus, string googleUrl = null, string cid = null, string gid = null, string hid = null, string googleVariant = null, string googleVariantCode = null)
        {
            if (this.Status == ProductStatus.Found && (newStatus == ProductStatus.NotFound || newStatus == ProductStatus.Error))
            {
                Console.WriteLine($"INFO [{System.Threading.Thread.CurrentThread.ManagedThreadId}]: Produkt ID {ProductId} jest już oznaczony jako ZNALEZIONY. Pomijam próbę zmiany statusu na {newStatus}.");
                return;
            }
            this.Status = newStatus;
            if (newStatus == ProductStatus.Found)
            {
                this.GoogleUrl = googleUrl;
                this.Cid = cid;
                this.GoogleGid = gid;
                this.GoogleHid = hid;
                this.GoogleVariant = googleVariant;
                this.GoogleVariantCode = googleVariantCode;
            }
        }

        public void AddCatalog(string cid, string gid, string hid = null)
        {
            string? cleanCid = string.IsNullOrWhiteSpace(cid) ? null : cid;
            string? cleanHid = string.IsNullOrWhiteSpace(hid) ? null : hid;
            string? cleanGid = string.IsNullOrWhiteSpace(gid) ? null : gid;

            string uniqueKey = cleanCid ?? cleanHid;
            if (string.IsNullOrEmpty(uniqueKey)) return;

            lock (KnownCids)
            {
                if (!KnownCids.Contains(uniqueKey))
                {
                    KnownCids.Add(uniqueKey);
                    NewCatalogsFound.Add(new ProductGoogleCatalog
                    {
                        ProductId = this.ProductId,
                        GoogleCid = cleanCid,
                        GoogleGid = cleanGid,
                        GoogleHid = cleanHid,

                        IsExtendedOfferByHid = (cleanCid == null),
                        FoundDate = DateTime.UtcNow
                    });
                    IsDirty = true;
                }
            }
        }
    }

    [HttpPost("stop")]
    public IActionResult StopScraping()
    {
        _externalTaskQueue.Clear();
        _enqueuedProductIds.Clear();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ZATRZYMANIE: Kolejka zewnętrzna wyczyszczona.");
        return Ok("Zatrzymano i wyczyszczono kolejki.");
    }

    private async Task TimerBatchUpdateCallback(CancellationToken cancellationToken)
    {
        CheckStuckProcessingProducts();

        if (!Volatile.Read(ref _isScrapingActive) && !_masterProductStateList.Values.Any(p => p.IsDirty))
        {

            return;
        }

        bool lockTaken = false;
        try
        {

            lockTaken = await _timerCallbackSemaphore.WaitAsync(TimeSpan.FromSeconds(1));
            if (lockTaken)
            {
                if (!_masterProductStateList.Values.Any(p => p.IsDirty)) { return; }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer wywołał BatchUpdateDatabaseAsync...");
                await BatchUpdateDatabaseAsync(false, CancellationToken.None);
            }

        }
        catch (OperationCanceledException) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer: Operacja zapisu wsadowego anulowana."); }
        catch (Exception ex) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer: Błąd podczas zapisu wsadowego: {ex.Message}"); }
        finally { if (lockTaken) { _timerCallbackSemaphore.Release(); } }
    }

    private void CheckStuckProcessingProducts()
    {
        var now = DateTime.UtcNow;
        var stuck = _processingStartedAt
            .Where(kvp => (now - kvp.Value) > PROCESSING_TIMEOUT)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var productId in stuck)
        {
            _processingStartedAt.TryRemove(productId, out _);
            if (_masterProductStateList.TryGetValue(productId, out var state))
            {
                lock (state)
                {
                    if (state.Status == ProductStatus.Processing)
                    {
                        state.Status = ProductStatus.Pending;
                        state.IsDirty = false;
                        _enqueuedProductIds.TryRemove(productId, out _);

                        var retryTask = BuildRetryTask(state,
                            new ExternalScraperResult { ErrorMessage = "Zombie Processing timeout", FinalStatus = "Error", RetryCount = 0 },
                            1);
                        _externalTaskQueue.Enqueue(retryTask);
                        _enqueuedProductIds.TryAdd(productId, 1);

                        Console.WriteLine($"[EXT-API] 🧟 Reset zombie Processing dla ID {productId} - re-enqueued");
                    }
                }
            }
        }
    }

    private void InitializeMasterProductListIfNeeded(
     int storeId,
     List<int> productIds,

     bool isRestartAfterCaptcha,
     bool requireUrl = true)
    {
        lock (_lockMasterListInit)
        {

            bool needsReinitialization = (productIds != null && productIds.Any())
                || isRestartAfterCaptcha
                || !_masterProductStateList.Any()
                || _masterProductStateList.Values.All(p => p.Status != ProductStatus.Pending && p.Status != ProductStatus.Processing);

            if (!needsReinitialization)
            {
                var firstProduct = _masterProductStateList.Values.FirstOrDefault();
                if (firstProduct != null)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    var productInDb = context.Products.AsNoTracking().FirstOrDefault(p => p.ProductId == firstProduct.ProductId);
                    if (productInDb == null || productInDb.StoreId != storeId)
                    {
                        needsReinitialization = true;
                    }
                }
            }

            if (needsReinitialization)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizuję _masterProductStateList dla sklepu ID: {storeId}...");
                _masterProductStateList.Clear();

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    IQueryable<ProductClass> query;

                    if (productIds != null && productIds.Any())
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizacja: Wybrano {productIds.Count} konkretnych produktów do przetworzenia.");

                        query = context.Set<ProductClass>().AsNoTracking()
                                    .Where(p => p.StoreId == storeId && productIds.Contains(p.ProductId));
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizacja: Brak wybranych produktów, przetwarzam wszystkie kwalifikujące się.");

                        query = context.Set<ProductClass>().AsNoTracking()
                                    .Where(p => p.StoreId == storeId && p.OnGoogle);

                        if (requireUrl)
                        {
                            query = query.Where(p => !string.IsNullOrEmpty(p.Url));
                        }
                    }

                    var productsFromDb = query.ToList();

                    foreach (var dbProduct in productsFromDb)
                    {
                        var state = new ProductProcessingState(dbProduct, null);

                        if (productIds != null && productIds.Any())
                        {
                            state.Status = ProductStatus.Pending;
                        }

                        _masterProductStateList.TryAdd(dbProduct.ProductId, state);
                    }
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] _masterProductStateList zainicjalizowana. Załadowano {_masterProductStateList.Count} produktów.");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] _masterProductStateList jest już zainicjalizowana i nie wymaga odświeżenia.");
            }
        }
    }

    private async Task BatchUpdateDatabaseAsync(bool isFinalSave = false, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested && !isFinalSave)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Anulowanie żądane, pomijam zapis (nie jest to zapis końcowy).");
            return;
        }

        List<ProductProcessingState> productsToConsiderForUpdate;
        lock (_lockMasterListInit)
        {
            productsToConsiderForUpdate = _masterProductStateList.Values
                                          .Where(p => p.IsDirty)
                                          .ToList();
        }

        if (!productsToConsiderForUpdate.Any())
        {
            if (!isFinalSave) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Brak zmian (IsDirty=false dla wszystkich) do zapisania.");
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Przygotowuję zapis dla {productsToConsiderForUpdate.Count} produktów oznaczonych jako IsDirty...");

        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            int changesMadeToContext = 0;

            foreach (var productState in productsToConsiderForUpdate)
            {
                if (cancellationToken.IsCancellationRequested && !isFinalSave) break;

                ProductStatus currentStatusSnapshot = default;
                string currentGoogleUrlSnapshot = null;
                string currentCidSnapshot = null;
                string currentGidSnapshot = null;
                string currentHidSnapshot = null;
                string currentGoogleVariantSnapshot = null;
                string currentGoogleVariantCodeSnapshot = null;
                bool processThisProduct = false;

                lock (productState)
                {
                    if (productState.IsDirty)
                    {
                        processThisProduct = true;
                        currentStatusSnapshot = productState.Status;
                        currentGoogleUrlSnapshot = productState.GoogleUrl;
                        currentCidSnapshot = productState.Cid;
                        currentGidSnapshot = productState.GoogleGid;
                        currentHidSnapshot = productState.GoogleHid;
                        currentGoogleVariantSnapshot = productState.GoogleVariant;
                        currentGoogleVariantCodeSnapshot = productState.GoogleVariantCode;
                    }
                }

                if (!processThisProduct) { continue; }

                ProductClass dbProduct = await context.Set<ProductClass>().FindAsync(new object[] { productState.ProductId }, cancellationToken);

                if (cancellationToken.IsCancellationRequested && !isFinalSave) break;

                bool changedInDb = false;
                lock (productState)
                {
                    if (dbProduct != null)
                    {
                        if (currentStatusSnapshot == ProductStatus.Found)
                        {

                            if (dbProduct.FoundOnGoogle != true
                                || dbProduct.GoogleUrl != currentGoogleUrlSnapshot
                                || dbProduct.GoogleGid != currentGidSnapshot
                                || dbProduct.GoogleHid != currentHidSnapshot
                                || dbProduct.GoogleVariant != currentGoogleVariantSnapshot
                                || dbProduct.GoogleVariantCode != currentGoogleVariantCodeSnapshot)
                            {
                                dbProduct.FoundOnGoogle = true;
                                dbProduct.GoogleUrl = currentGoogleUrlSnapshot;
                                dbProduct.GoogleGid = currentGidSnapshot;
                                dbProduct.GoogleHid = currentHidSnapshot;
                                dbProduct.GoogleVariant = currentGoogleVariantSnapshot;
                                dbProduct.GoogleVariantCode = currentGoogleVariantCodeSnapshot;
                                changedInDb = true;
                            }

                        }
                        else if (currentStatusSnapshot == ProductStatus.NotFound)
                        {
                            if (dbProduct.FoundOnGoogle != false || dbProduct.GoogleUrl != null || dbProduct.GoogleGid != null || dbProduct.GoogleVariant != null || dbProduct.GoogleVariantCode != null || dbProduct.GoogleHid != null)
                            {
                                dbProduct.FoundOnGoogle = false;
                                dbProduct.GoogleUrl = null;
                                dbProduct.GoogleGid = null;
                                dbProduct.GoogleHid = null;
                                dbProduct.GoogleVariant = null;
                                dbProduct.GoogleVariantCode = null;
                                changedInDb = true;
                            }
                        }

                        else if (currentStatusSnapshot == ProductStatus.Error)
                        {

                            if (dbProduct.FoundOnGoogle != false || dbProduct.GoogleUrl != null || dbProduct.GoogleGid != null || dbProduct.GoogleVariant != null || dbProduct.GoogleVariantCode != null || dbProduct.GoogleHid != null)
                            {
                                dbProduct.FoundOnGoogle = false;
                                dbProduct.GoogleUrl = null;
                                dbProduct.GoogleGid = null;
                                dbProduct.GoogleHid = null;
                                dbProduct.GoogleVariant = null;
                                dbProduct.GoogleVariantCode = null;
                                changedInDb = true;
                            }
                        }
                        else if (currentStatusSnapshot == ProductStatus.CaptchaHalt)
                        {

                        }

                        if (changedInDb)
                        { context.Set<ProductClass>().Update(dbProduct); changesMadeToContext++; }
                    }
                    else
                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: OSTRZEŻENIE - Produkt ID {productState.ProductId} (snapshot status: {currentStatusSnapshot}) nie znaleziony w DB."); }
                    productState.IsDirty = false;
                }
            }

            if (changesMadeToContext > 0 && (!cancellationToken.IsCancellationRequested || isFinalSave))
            {
                try
                {
                    int savedEntitiesCount = await context.SaveChangesAsync(isFinalSave ? CancellationToken.None : cancellationToken);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Pomyślnie zapisano zmiany. Liczba zmienionych encji: {savedEntitiesCount}.");
                }
                catch (OperationCanceledException) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Zapis zmian anulowany."); }
                catch (Exception ex) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Błąd podczas SaveChangesAsync: {ex.Message}"); }
            }
            else if (productsToConsiderForUpdate.Any() && changesMadeToContext == 0)
            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BatchUpdate: Produkty IsDirty, ale brak zmian w kontekście DB."); }
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductList(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/ProductList.cshtml", products);
    }

    [HttpPost]
    public async Task<IActionResult> SetOnGoogleForAll(int storeId, bool ignoreUrlRequirement)
    {

        var productsToUpdateQuery = _context.Products
            .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle));

        if (!ignoreUrlRequirement)
        {
            productsToUpdateQuery = productsToUpdateQuery
                .Where(p => !string.IsNullOrEmpty(p.Url));
        }

        await productsToUpdateQuery.ExecuteUpdateAsync(s => s.SetProperty(p => p.OnGoogle, true));

        return RedirectToAction("ProductList", new { storeId = storeId });
    }

    [HttpGet]
    public async Task<IActionResult> GoogleProducts(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null) return NotFound();

        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (isAjax)
        {
            var rawData = await _context.Products
                .AsNoTracking()
                .Where(p => p.StoreId == storeId && p.OnGoogle)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductNameInStoreForGoogle,
                    p.Url,
                    p.FoundOnGoogle,
                    p.GoogleUrl,
                    p.Ean,
                    p.ProducerCode,
                    p.GoogleGid,
                    p.GoogleHid,
                    p.GoogleVariant,
                    p.GoogleVariantCode,
                    p.OtherVariantEans,
                    Catalogs = p.GoogleCatalogs.Select(gc => new {
                        gc.GoogleCid,
                        gc.GoogleGid,
                        gc.GoogleHid,
                        gc.IsExtendedOfferByHid
                    })
                })
                .ToListAsync();

            var jsonProducts = rawData.Select(p => {
                string generatedUrl = null;
                string productIdCid = ExtractProductIdFromUrl(p.GoogleUrl);

                if (!string.IsNullOrEmpty(productIdCid))
                {
                    string productNameForUrl = System.Net.WebUtility.UrlEncode(p.ProductNameInStoreForGoogle);
                    generatedUrl = $"https://www.google.com/search?q={productNameForUrl}&udm=28#oshopproduct=cid:{productIdCid},pvt:hg,pvo:3&oshop=apv";
                }

                return new
                {
                    p.ProductId,
                    p.ProductNameInStoreForGoogle,
                    p.Url,
                    p.FoundOnGoogle,
                    p.GoogleUrl,
                    p.Ean,
                    p.ProducerCode,
                    p.GoogleGid,
                    p.GoogleHid,
                    p.GoogleVariant,
                    p.GoogleVariantCode,
                    p.OtherVariantEans,
                    GeneratedGoogleUrl = generatedUrl,
                    GoogleCatalogs = p.Catalogs
                };
            });

            return Json(jsonProducts);
        }

        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.GoogleCatalogs)
            .Where(p => p.StoreId == storeId && p.OnGoogle)
            .ToListAsync();

        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/GoogleProducts.cshtml", products);
    }

    private string ExtractProductIdFromUrl(string googleUrl)
    {
        if (string.IsNullOrEmpty(googleUrl) || !googleUrl.Contains("/product/"))
        {
            return null;
        }
        try
        {
            var uri = new Uri(googleUrl);
            string lastSegment = uri.Segments.LastOrDefault()?.Trim('/');

            if (!string.IsNullOrEmpty(lastSegment))
            {

                if (ulong.TryParse(lastSegment, out _))

                {
                    return lastSegment;
                }
            }
        }
        catch (UriFormatException) { return null; }

        return null;
    }

    [HttpPost]
    public async Task<IActionResult> ResetNotFoundProducts(int storeId)
    {

        var notFoundProducts = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == false)
            .ToListAsync();

        foreach (var product in notFoundProducts)
        {

            product.FoundOnGoogle = null;
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> ToggleGoogleStatus(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return NotFound();
        }

        product.OnGoogle = !product.OnGoogle;
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = product.StoreId });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProductNameInStoreForGoogle(int productId, string productNameInStoreForGoogle)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return NotFound();
        }

        product.ProductNameInStoreForGoogle = productNameInStoreForGoogle;
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = product.StoreId });
    }

    [HttpPost]
    public async Task<IActionResult> ClearGoogleUrls(int storeId)
    {
        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        foreach (var product in products)
        {
            product.GoogleUrl = null;
            product.FoundOnGoogle = null;
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok();
    }

    private static string ExtractCidFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(url, @"/product/(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    [HttpPost]
    public async Task<IActionResult> BulkRemoveTextFromUrls(int storeId, string textToRemove)
    {
        if (string.IsNullOrWhiteSpace(textToRemove))
        {
            TempData["ErrorMessage"] = "Musisz wpisać fragment tekstu do wycięcia.";
            return RedirectToAction("ProductList", new { storeId });
        }

        try
        {
            int updatedRowsCount = await _context.Products
                .Where(p => p.StoreId == storeId && p.Url != null && p.Url.Contains(textToRemove))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Url, p => p.Url.Replace(textToRemove, "")));

            TempData["SuccessMessage"] = $"Pomyślnie usunięto '{textToRemove}' z {updatedRowsCount} linków ofert.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BŁĄD BulkRemoveTextFromUrls: {ex.Message}");
            TempData["ErrorMessage"] = $"Wystąpił błąd podczas masowej aktualizacji: {ex.Message}";
        }

        return RedirectToAction("ProductList", new { storeId });
    }

    #region ============== ZEWNĘTRZNE SCRAPERY API ==============

    private const string EXTERNAL_SCRAPER_API_KEY = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

    private static readonly ConcurrentDictionary<string, ExternalScraperInfo> _registeredScrapers = new();

    private static readonly ConcurrentQueue<ExternalScraperTask> _externalTaskQueue = new();

    private static readonly ConcurrentDictionary<string, ExternalScraperResult> _externalResults = new();

    private static ExternalScraperSettings _externalScraperSettings = new();

    private static readonly object _settingsLock = new object();

    #endregion

    #region ============== MODELE DLA ZEWNĘTRZNYCH SCRAPERÓW ==============

    public class ExternalScraperInfo
    {
        public string ScraperName { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public DateTime RegisteredAt { get; set; }
        public bool IsActive => (DateTime.UtcNow - LastHeartbeat).TotalSeconds < 120;
        public int TasksCompleted { get; set; }
        public int TasksFailed { get; set; }
    }

    public class ExternalScraperSettings
    {
        [JsonPropertyName("generatorsCount")]
        public int GeneratorsCount { get; set; } = 2;

        [JsonPropertyName("headlessMode")]
        public bool HeadlessMode { get; set; } = true;

        [JsonPropertyName("maxWorkers")]
        public int MaxWorkers { get; set; } = 1;

        [JsonPropertyName("headStartDuration")]
        public int HeadStartDuration { get; set; } = 50;

        [JsonPropertyName("maxCookiesInQueue")]
        public int MaxCookiesInQueue { get; set; } = 200;

        [JsonPropertyName("nukeThreshold")]
        public int NukeThreshold { get; set; } = 7;

        [JsonPropertyName("maxSessionErrorsPerUrl")]
        public int MaxSessionErrorsPerUrl { get; set; } = 2;

        [JsonPropertyName("networkResetMethod")]
        public string NetworkResetMethod { get; set; } = "mullvad";

        [JsonPropertyName("autoNetworkResetOnCaptcha")]
        public bool AutoNetworkResetOnCaptcha { get; set; } = true;

        [JsonPropertyName("captchaCountBeforeNetworkReset")]
        public int CaptchaCountBeforeNetworkReset { get; set; } = 5;

        [JsonPropertyName("useGpid")]
        public bool UseGpid { get; set; } = false;

        [JsonPropertyName("mullvadPath")]
        public string MullvadPath { get; set; } = @"C:\Program Files\Mullvad VPN\resources\mullvad.exe";

        [JsonPropertyName("mullvadCountryCode")]
        public string MullvadCountryCode { get; set; } = "pl";

        [JsonPropertyName("mullvadCityCode")]
        public string MullvadCityCode { get; set; } = "waw";

        [JsonPropertyName("modemUrl")]
        public string ModemUrl { get; set; } = "http://192.168.1.1";

        [JsonPropertyName("modemPassword")]
        public string ModemPassword { get; set; } = "QqD9wWUF";

        [JsonPropertyName("modemRestartWaitSeconds")]
        public int ModemRestartWaitSeconds { get; set; } = 50;

        [JsonPropertyName("scrapingMode")]
        public string ScrapingMode { get; set; } = "Standard";

        [JsonPropertyName("maxCidsToProcess")]
        public int MaxCidsToProcess { get; set; } = 3;

        [JsonPropertyName("appendProducerCode")]
        public bool AppendProducerCode { get; set; } = false;

        [JsonPropertyName("compareOnlyCurrentProductCode")]
        public bool CompareOnlyCurrentProductCode { get; set; } = false;

        [JsonPropertyName("productNamePrefix")]
        public string ProductNamePrefix { get; set; } = null;

        [JsonPropertyName("searchModeUdm")]
        public int SearchModeUdm { get; set; } = 3;

        [JsonPropertyName("enableVariantSearch")]
        public bool EnableVariantSearch { get; set; } = false;

        [JsonPropertyName("useVariantEans")]
        public bool UseVariantEans { get; set; } = false;
    }

    public class ExternalScraperTask
    {
        [JsonPropertyName("taskId")]
        public string TaskId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("assignedTo")]
        public string AssignedTo { get; set; }

        [JsonPropertyName("assignedAt")]
        public DateTime? AssignedAt { get; set; }

        [JsonPropertyName("productId")]
        public int ProductId { get; set; }

        [JsonPropertyName("productName")]
        public string ProductName { get; set; }

        [JsonPropertyName("cleanedUrl")]
        public string CleanedUrl { get; set; }

        [JsonPropertyName("originalUrl")]
        public string OriginalUrl { get; set; }

        [JsonPropertyName("useGpid")]
        public bool UseGpid { get; set; } = false;

        [JsonPropertyName("ean")]
        public string Ean { get; set; }
        [JsonPropertyName("extraEans")]
        public List<string> ExtraEans { get; set; } = new();

        [JsonPropertyName("producerCode")]
        public string ProducerCode { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; }

        [JsonPropertyName("searchTerm")]
        public string SearchTerm { get; set; }

        [JsonPropertyName("maxItemsToExtract")]
        public int MaxItemsToExtract { get; set; } = 20;

        [JsonPropertyName("udmValue")]
        public int UdmValue { get; set; } = 3;

        [JsonPropertyName("googleCid")]
        public string GoogleCid { get; set; }

        [JsonPropertyName("googleGid")]
        public string GoogleGid { get; set; }

        [JsonPropertyName("googleHid")]
        public string GoogleHid { get; set; }

        [JsonPropertyName("targetCode")]
        public string TargetCode { get; set; }

        [JsonPropertyName("storeId")]
        public int StoreId { get; set; }

        [JsonPropertyName("eligibleProductsMap")]
        public Dictionary<string, int> EligibleProductsMap { get; set; }

        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; } = 0;

        [JsonPropertyName("lastFailReason")]
        public string LastFailReason { get; set; }

    }
    public class ExternalScraperResult
    {
        [JsonPropertyName("taskId")]
        public string TaskId { get; set; }

        [JsonPropertyName("scraperName")]
        public string ScraperName { get; set; }

        [JsonPropertyName("completedAt")]
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("isSuccess")]
        public bool IsSuccess { get; set; }

        [JsonPropertyName("captchaEncountered")]
        public bool CaptchaEncountered { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("productId")]
        public int? ProductId { get; set; }

        [JsonPropertyName("identifiers")]
        public List<GoogleProductIdentifierDto> Identifiers { get; set; }

        [JsonPropertyName("storeUrls")]
        public List<string> StoreUrls { get; set; }

        [JsonPropertyName("productDetails")]
        public GoogleProductDetailsDto ProductDetails { get; set; }

        [JsonPropertyName("rawResponse")]
        public string RawResponse { get; set; }

        [JsonPropertyName("finalStatus")]
        public string FinalStatus { get; set; }

        [JsonPropertyName("foundGoogleUrl")]
        public string FoundGoogleUrl { get; set; }

        [JsonPropertyName("foundCid")]
        public string FoundCid { get; set; }

        [JsonPropertyName("foundGid")]
        public string FoundGid { get; set; }

        [JsonPropertyName("foundHid")]
        public string FoundHid { get; set; }

        [JsonPropertyName("foundVariant")]
        public string FoundVariant { get; set; }

        [JsonPropertyName("foundVariantCode")]                    // BYLO: foundVariantFilter
        public string FoundVariantCode { get; set; }              // BYLO: FoundVariantFilter

        [JsonPropertyName("variantModeUsed")]
        public bool VariantModeUsed { get; set; }

        [JsonPropertyName("matchedProducts")]
        public List<MatchedProductDto> MatchedProducts { get; set; }

        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; } = 0;
    }
    public class GoogleProductIdentifierDto
    {
        [JsonPropertyName("cid")]
        public string Cid { get; set; }

        [JsonPropertyName("gid")]
        public string Gid { get; set; }

        [JsonPropertyName("hid")]
        public string Hid { get; set; }
    }

    public class GoogleProductDetailsDto
    {
        [JsonPropertyName("mainTitle")]
        public string MainTitle { get; set; }

        [JsonPropertyName("offerTitles")]
        public List<string> OfferTitles { get; set; }
    }

    public class MatchedProductDto
    {
        [JsonPropertyName("productId")]
        public int ProductId { get; set; }

        [JsonPropertyName("matchedCode")]
        public string MatchedCode { get; set; }

        [JsonPropertyName("googleUrl")]
        public string GoogleUrl { get; set; }

        [JsonPropertyName("cid")]
        public string Cid { get; set; }

        [JsonPropertyName("gid")]
        public string Gid { get; set; }

        [JsonPropertyName("hid")]
        public string Hid { get; set; }

        [JsonPropertyName("variant")]
        public string Variant { get; set; }

        [JsonPropertyName("variantCode")]
        public string VariantCode { get; set; }
    }

    public class NukeReportDto
    {
        [JsonPropertyName("scraperName")]
        public string ScraperName { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        [JsonPropertyName("newIpAddress")]
        public string NewIpAddress { get; set; }
    }

    public class RegisterScraperRequest
    {
        [JsonPropertyName("scraperName")]
        public string ScraperName { get; set; }
    }

    private bool ValidateApiKey(string apiKey)
    {
        return !string.IsNullOrEmpty(apiKey) && apiKey == EXTERNAL_SCRAPER_API_KEY;
    }

    private string GetApiKeyFromHeader()
    {
        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
            return apiKey.ToString();
        return null;
    }

    [HttpPost]
    [Route("api/external-scraper/register")]
    [AllowAnonymous]
    public IActionResult RegisterExternalScraper([FromBody] RegisterScraperRequest request)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (string.IsNullOrEmpty(request?.ScraperName))
            return BadRequest(new { error = "ScraperName is required" });

        var info = _registeredScrapers.AddOrUpdate(
            request.ScraperName,
            new ExternalScraperInfo
            {
                ScraperName = request.ScraperName,
                RegisteredAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.LastHeartbeat = DateTime.UtcNow;
                return existing;
            }
        );

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Scraper '{request.ScraperName}' zarejestrowany/odświeżony.");

        return Ok(new
        {
            message = "Registered successfully",
            scraperName = info.ScraperName,
            registeredAt = info.RegisteredAt,
            isActive = info.IsActive
        });
    }

    [HttpGet]
    [Route("api/external-scraper/settings")]
    [AllowAnonymous]
    public IActionResult GetExternalScraperSettings()
    {

        var apiKey = GetApiKeyFromHeader();
        bool isAuthorizedScraper = ValidateApiKey(apiKey);
        bool isAdminUser = User.Identity != null && User.Identity.IsAuthenticated;

        if (!isAuthorizedScraper && !isAdminUser)
        {
            return Unauthorized(new { error = "Invalid API key or Session" });
        }

        lock (_settingsLock)
        {
            return Ok(_externalScraperSettings);
        }
    }

    [HttpPost]
    [Route("api/external-scraper/settings")]
    [AllowAnonymous]
    public IActionResult UpdateExternalScraperSettings([FromBody] ExternalScraperSettings settings)
    {

        var apiKey = GetApiKeyFromHeader();
        bool isAuthorizedScraper = ValidateApiKey(apiKey);
        bool isAdminUser = User.Identity != null && User.Identity.IsAuthenticated;

        if (!isAuthorizedScraper && !isAdminUser)
            return Unauthorized(new { error = "Unauthorized" });

        if (settings == null)
            return BadRequest(new { error = "Settings object is required" });

        lock (_settingsLock)
        {
            _externalScraperSettings = settings;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Ustawienia zaktualizowane.");
        return Ok(new { message = "Settings updated" });
    }

    [HttpGet]
    [Route("api/external-scraper/get-task")]
    [AllowAnonymous]
    public IActionResult GetExternalScraperTask([FromQuery] string scraperName = null)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (!string.IsNullOrEmpty(scraperName) && _registeredScrapers.TryGetValue(scraperName, out var info))
        {
            info.LastHeartbeat = DateTime.UtcNow;
        }

        while (_externalTaskQueue.TryDequeue(out var task))
        {

            if (_masterProductStateList.TryGetValue(task.ProductId, out var currentState))
            {
                if (currentState.Status == ProductStatus.Found || currentState.Status == ProductStatus.NotFound || currentState.Status == ProductStatus.Error)
                {
                    _enqueuedProductIds.TryRemove(task.ProductId, out _);
                    continue;
                }

                lock (currentState)
                {
                    currentState.Status = ProductStatus.Processing;
                }
                _processingStartedAt[task.ProductId] = DateTime.UtcNow;
            }
            else
            {
                _enqueuedProductIds.TryRemove(task.ProductId, out _);
                continue;
            }

            task.AssignedTo = scraperName ?? "unknown";
            task.AssignedAt = DateTime.UtcNow;
            _enqueuedProductIds.TryRemove(task.ProductId, out _);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Zadanie {task.TaskId.Substring(0, Math.Min(20, task.TaskId.Length))}... przydzielone do '{scraperName}'");
            Console.ResetColor();

            return Ok(new
            {
                hasTask = true,
                task = new
                {
                    taskId = task.TaskId,
                    productId = task.ProductId,
                    productName = task.ProductName,
                    searchTerm = task.SearchTerm,
                    cleanedUrl = task.CleanedUrl,
                    originalUrl = task.OriginalUrl,
                    ean = task.Ean,
                    producerCode = task.ProducerCode,
                    mode = task.Mode,
                    maxItemsToExtract = task.MaxItemsToExtract,
                    udmValue = task.UdmValue,
                    storeId = task.StoreId,
                    googleCid = task.GoogleCid,
                    googleGid = task.GoogleGid,
                    googleHid = task.GoogleHid,
                    targetCode = task.TargetCode,
                    eligibleProductsMap = task.EligibleProductsMap,
                    useGpid = task.UseGpid,
                    extraEans = task.ExtraEans,
                }
            });
        }

        return Ok(new
        {
            hasTask = false,
            message = "No tasks available"
        });
    }

    [HttpGet]
    [Route("api/external-scraper/get-task-batch")]
    [AllowAnonymous]
    public IActionResult GetExternalScraperTaskBatch([FromQuery] string scraperName, [FromQuery] int maxTasks = 10)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (string.IsNullOrEmpty(scraperName))
            return BadRequest(new { error = "scraperName is required" });

        if (_registeredScrapers.TryGetValue(scraperName, out var info))
        {
            info.LastHeartbeat = DateTime.UtcNow;
        }

        var tasks = new List<ExternalScraperTask>();

        while (tasks.Count < maxTasks && _externalTaskQueue.TryDequeue(out var task))
        {
            if (_masterProductStateList.TryGetValue(task.ProductId, out var currentState))
            {
                if (currentState.Status == ProductStatus.Found || currentState.Status == ProductStatus.NotFound || currentState.Status == ProductStatus.Error)
                {
                    _enqueuedProductIds.TryRemove(task.ProductId, out _);
                    continue;
                }

                lock (currentState)
                {
                    currentState.Status = ProductStatus.Processing;
                }
                _processingStartedAt[task.ProductId] = DateTime.UtcNow;
            }
            else
            {
                _enqueuedProductIds.TryRemove(task.ProductId, out _);
                continue;
            }

            task.AssignedTo = scraperName;
            task.AssignedAt = DateTime.UtcNow;
            _enqueuedProductIds.TryRemove(task.ProductId, out _);
            tasks.Add(task);
        }

        if (tasks.Any())
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] {tasks.Count} zadań przydzielonych do '{scraperName}'");
        }

        return Ok(new
        {
            hasTasks = tasks.Any(),
            tasksCount = tasks.Count,
            tasks = tasks,
            queueRemaining = _externalTaskQueue.Count
        });
    }

    [HttpPost]
    [Route("api/external-scraper/submit-result")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitExternalScraperResult([FromBody] ExternalScraperResult result)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (result == null || string.IsNullOrEmpty(result.TaskId))
            return BadRequest(new { error = "Invalid result" });

        _externalResults[result.TaskId] = result;

        if (!string.IsNullOrEmpty(result.ScraperName) && _registeredScrapers.TryGetValue(result.ScraperName, out var info))
        {
            info.LastHeartbeat = DateTime.UtcNow;
            if (result.IsSuccess)
                info.TasksCompleted++;
            else
                info.TasksFailed++;
        }

        await ProcessExternalResultAsync(result);

        if (result.IsSuccess && result.FinalStatus == "Found")
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] ✓ Wynik {result.TaskId.Substring(0, Math.Min(20, result.TaskId.Length))}... od '{result.ScraperName}': FOUND");
            Console.ResetColor();
        }
        else if (result.CaptchaEncountered)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] ⚠ Wynik {result.TaskId.Substring(0, Math.Min(20, result.TaskId.Length))}... od '{result.ScraperName}': CAPTCHA");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Wynik {result.TaskId.Substring(0, Math.Min(20, result.TaskId.Length))}... od '{result.ScraperName}': {result.FinalStatus}");
        }

        return Ok(new { message = "Result received", taskId = result.TaskId });
    }

    [HttpPost]
    public IActionResult StartExternalScraping(
         int storeId,
         List<int> productIds,
         string mode = "Standard",
         int maxCids = 3,
         bool appendProducerCode = false,
         bool compareOnlyCode = false,
         string prefix = null,
         int udm = 3,
         bool useVariantEans = false)
    {

        int activeScrapers = _registeredScrapers.Values.Count(s => s.IsActive);
        if (activeScrapers == 0)
        {
            return Json(new { success = false, message = "Błąd: Brak aktywnych scraperów zewnętrznych (Python). Uruchom skrypt Pythona." });
        }

        lock (_lockTimer)
        {
            if (_batchSaveTimer == null)
            {

                _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Timer do zapisu wsadowego został uruchomiony (interwał 10s).");
            }
        }

        lock (_settingsLock)
        {
            _externalScraperSettings.ScrapingMode = mode;
            _externalScraperSettings.MaxCidsToProcess = maxCids;
            _externalScraperSettings.AppendProducerCode = appendProducerCode;
            _externalScraperSettings.CompareOnlyCurrentProductCode = compareOnlyCode;
            _externalScraperSettings.ProductNamePrefix = prefix;
            _externalScraperSettings.SearchModeUdm = udm;
            _externalScraperSettings.UseVariantEans = useVariantEans;
        }

        InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: mode == "Standard");

        var pendingProducts = new List<ProductProcessingState>();

        lock (_lockMasterListInit)
        {
            pendingProducts = _masterProductStateList.Values
                .Where(p => p.Status == ProductStatus.Pending)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();
        }

        if (!pendingProducts.Any())
        {

            return Json(new { success = true, message = "Wszystkie wybrane produkty są już przetworzone lub nie kwalifikują się." });
        }

        var existingTaskProductIds = _externalTaskQueue.Select(t => t.ProductId).ToHashSet();

        int addedCount = 0;

        Dictionary<string, int> eligibleProductsMap = null;
        if (mode == "Standard" || mode == "full_process")
        {
            lock (_lockMasterListInit)
            {
                eligibleProductsMap = _masterProductStateList.Values
                    .Where(p => !string.IsNullOrEmpty(p.CleanedUrl) && p.Status == ProductStatus.Pending)
                    .GroupBy(p => p.CleanedUrl, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ProductId, StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (var product in pendingProducts)
        {

            if (!_enqueuedProductIds.TryAdd(product.ProductId, 1))
            {
                continue;

            }

            string searchTerm;

            if (mode == "MatchByEan")
            {
                if (string.IsNullOrWhiteSpace(product.Ean))
                {
                    _enqueuedProductIds.TryRemove(product.ProductId, out _);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] [MatchByEan] Pominięto produkt ID {product.ProductId} - brak EAN.");
                    continue;
                }
                searchTerm = product.Ean;
            }
            else
            {
                searchTerm = product.ProductNameInStoreForGoogle;
                if (appendProducerCode && !string.IsNullOrEmpty(product.ProducerCode))
                    searchTerm = $"{searchTerm} {product.ProducerCode}";
                if (!string.IsNullOrEmpty(prefix))
                    searchTerm = $"{prefix} {searchTerm}";
            }

            List<string> extraEans = new();
            if (mode == "MatchByEan" && useVariantEans && !string.IsNullOrWhiteSpace(product.OtherVariantEans))
            {
                var mainEanTrimmed = product.Ean?.Trim() ?? "";
                extraEans = product.OtherVariantEans
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0 && !string.Equals(e, mainEanTrimmed, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (extraEans.Any())
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] [MatchByEan] Produkt ID {product.ProductId}: główny EAN={product.Ean}, dodatkowych wariantów: {extraEans.Count} ({string.Join(",", extraEans)})");
                }
            }

            var task = new ExternalScraperTask
            {
                TaskId = $"product_{product.ProductId}_{Guid.NewGuid():N}",
                ProductId = product.ProductId,
                ProductName = product.ProductNameInStoreForGoogle,
                SearchTerm = searchTerm,
                CleanedUrl = product.CleanedUrl,
                OriginalUrl = product.OriginalUrl,
                Ean = product.Ean,
                ProducerCode = product.ProducerCode,
                Mode = mode,
                MaxItemsToExtract = maxCids,
                UdmValue = udm,
                StoreId = storeId,
                EligibleProductsMap = eligibleProductsMap,
                TargetCode = product.ProducerCode,
                UseGpid = _externalScraperSettings.UseGpid,
                ExtraEans = extraEans,
            };

            _externalTaskQueue.Enqueue(task);
            addedCount++;
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Zakolejkowano {addedCount} nowych zadań. (Pominięto {pendingProducts.Count - addedCount} duplikatów).");

        return Json(new
        {
            success = true,
            message = $"Dodano {addedCount} nowych zadań do kolejki (pominięto {pendingProducts.Count - addedCount} duplikatów). Zapis do bazy aktywny.",
            queueSize = _externalTaskQueue.Count
        });
    }

    [HttpPost]
    [Route("api/external-scraper/submit-result-batch")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitExternalScraperResultBatch([FromBody] List<ExternalScraperResult> results)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (results == null || !results.Any())
            return BadRequest(new { error = "No results provided" });

        int successCount = 0;
        int failCount = 0;

        foreach (var result in results)
        {
            if (string.IsNullOrEmpty(result?.TaskId)) continue;

            _externalResults[result.TaskId] = result;

            if (!string.IsNullOrEmpty(result.ScraperName) && _registeredScrapers.TryGetValue(result.ScraperName, out var info))
            {
                info.LastHeartbeat = DateTime.UtcNow;
                if (result.IsSuccess)
                    info.TasksCompleted++;
                else
                    info.TasksFailed++;
            }

            await ProcessExternalResultAsync(result);

            if (result.IsSuccess)
                successCount++;
            else
                failCount++;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Paczka {results.Count} wyników: {successCount} OK, {failCount} FAIL");

        return Ok(new
        {
            message = "Batch received",
            totalProcessed = results.Count,
            successCount,
            failCount
        });
    }

    [HttpPost]
    [Route("api/external-scraper/report-nuke")]
    [AllowAnonymous]
    public IActionResult ReportNuke([FromBody] NukeReportDto report)
    {
        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey))
            return Unauthorized(new { error = "Invalid API key" });

        if (report == null)
            return BadRequest(new { error = "Report is required" });

        Console.ForegroundColor = report.Status == "started" ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [NUKE] Scraper '{report.ScraperName}': {report.Status.ToUpper()}");
        if (!string.IsNullOrEmpty(report.Reason))
            Console.WriteLine($"   Powód: {report.Reason}");
        if (!string.IsNullOrEmpty(report.NewIpAddress))
            Console.WriteLine($"   Nowe IP: {report.NewIpAddress}");
        Console.ResetColor();

        return Ok(new { message = "Nuke report received" });
    }

    [HttpGet]
    [Route("api/external-scraper/status")]
    [AllowAnonymous]

    public IActionResult GetExternalScrapersStatus()
    {

        var apiKey = GetApiKeyFromHeader();
        if (!ValidateApiKey(apiKey) && (!User.Identity.IsAuthenticated))
        {
            return Unauthorized(new { error = "Unauthorized" });
        }
        var scrapers = _registeredScrapers.Values.Select(s => new
        {
            s.ScraperName,
            s.RegisteredAt,
            s.LastHeartbeat,
            s.IsActive,
            s.TasksCompleted,
            s.TasksFailed,
            SecondsSinceHeartbeat = (DateTime.UtcNow - s.LastHeartbeat).TotalSeconds
        }).ToList();

        return Ok(new
        {
            totalScrapers = scrapers.Count,
            activeScrapers = scrapers.Count(s => s.IsActive),
            queuedTasks = _externalTaskQueue.Count,
            pendingResults = _externalResults.Count,
            scrapers
        });
    }

    private async Task ProcessExternalResultAsync(ExternalScraperResult result)
    {
        if (result == null) return;

        try
        {
            int? productId = result.ProductId;

            if (!productId.HasValue && result.TaskId.Contains("product_"))
            {
                var parts = result.TaskId.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedId))
                {
                    productId = parsedId;
                }
            }

            if (productId.HasValue)
            {
                _enqueuedProductIds.TryRemove(productId.Value, out _);
                _processingStartedAt.TryRemove(productId.Value, out _);

                if (_masterProductStateList.TryGetValue(productId.Value, out var productState))
                {
                    lock (productState)
                    {
                        switch (result.FinalStatus)
                        {
                            case "Found":
                                productState.UpdateStatus(
                                    ProductStatus.Found,
                                    result.FoundGoogleUrl,
                                    result.FoundCid,
                                    result.FoundGid,
                                    result.FoundHid,
                                    result.FoundVariant,
                                    result.FoundVariantCode);

                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"\n[EXT-API - BEZPOŚREDNIE TRAFIENIE]");
                                    Console.WriteLine($" ├─ ID Produktu : {productId.Value}");
                                    Console.WriteLine($" ├─ Nazwa       : {productState.ProductNameInStoreForGoogle}");
                                    Console.WriteLine($" ├─ URL z bazy  : {productState.CleanedUrl}");
                                    Console.WriteLine($" ├─ CID Google  : {result.FoundCid}");

                                    if (!string.IsNullOrEmpty(result.FoundGid)) Console.WriteLine($" ├─ GID Google  : {result.FoundGid}");
                                    if (!string.IsNullOrEmpty(result.FoundHid)) Console.WriteLine($" ├─ HID Google  : {result.FoundHid}");

                                    if (!string.IsNullOrEmpty(result.FoundVariant))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.Write(" ├─ 🎨 Wariant   : ");
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine(result.FoundVariant);

                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.Write(" ├─ 🎨 PVF Code  : ");
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine(result.FoundVariantCode);

                                        Console.ForegroundColor = ConsoleColor.Green;
                                    }
                                    Console.WriteLine($" └─ URL Google  : {result.FoundGoogleUrl}");
                                    Console.ResetColor();
                                }
                                break;

                            case "NotFound":
                                productState.UpdateStatus(ProductStatus.NotFound);
                                break;

                            case "CaptchaHalt":
                            case "Error":
                            default:
                                int currentRetry = result.RetryCount;

                                if (currentRetry < MAX_TASK_RETRIES)
                                {
                                    productState.Status = ProductStatus.Pending;

                                    var retryTask = BuildRetryTask(productState, result, currentRetry + 1);
                                    _externalTaskQueue.Enqueue(retryTask);
                                    _enqueuedProductIds.TryAdd(productState.ProductId, 1);

                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"[EXT-API] ↻ RETRY {currentRetry + 1}/{MAX_TASK_RETRIES} - " +
                                                          $"ID {productState.ProductId} ({result.FinalStatus}: {result.ErrorMessage?.Substring(0, Math.Min(50, result.ErrorMessage?.Length ?? 0))})");
                                        Console.ResetColor();
                                    }
                                }
                                else
                                {
                                    productState.UpdateStatus(ProductStatus.NotFound);
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"[EXT-API] ✗ ID {productState.ProductId}: wyczerpano {MAX_TASK_RETRIES} prób ({result.FinalStatus})");
                                        Console.ResetColor();
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            if (result.MatchedProducts != null && result.MatchedProducts.Any())
            {
                foreach (var matched in result.MatchedProducts)
                {
                    if (productId.HasValue && matched.ProductId == productId.Value)
                        continue;

                    if (_masterProductStateList.TryGetValue(matched.ProductId, out var matchedState))
                    {
                        lock (matchedState)
                        {
                            if (matchedState.Status != ProductStatus.Found)
                            {
                                matchedState.UpdateStatus(
                                    ProductStatus.Found,
                                    matched.GoogleUrl,
                                    matched.Cid,
                                    matched.Gid,
                                    matched.Hid,
                                    matched.Variant,
                                    matched.VariantCode);
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine($"\n[EXT-API - CROSS-MATCH (RYKOSZET Z PULI!)]");
                                    Console.WriteLine($" ├─ Wątek szukał : Głównego produktu ID {productId}");
                                    Console.WriteLine($" ├─ DOPASOWANO   : Produkt ID {matched.ProductId} z puli oczekujących!");
                                    Console.WriteLine($" ├─ Nazwa w bazie: {matchedState.ProductNameInStoreForGoogle}");
                                    Console.WriteLine($" ├─ URL z bazy   : {matchedState.CleanedUrl}");
                                    Console.WriteLine($" ├─ CID Google   : {matched.Cid}");

                                    if (!string.IsNullOrEmpty(matched.Gid)) Console.WriteLine($" ├─ GID Google   : {matched.Gid}");
                                    if (!string.IsNullOrEmpty(matched.Hid)) Console.WriteLine($" ├─ HID Google   : {matched.Hid}");

                                    if (!string.IsNullOrEmpty(matched.Variant))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.Write(" ├─ 🎨 Wariant     : ");
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine(matched.Variant);

                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.Write(" ├─ 🎨 PVF Code  : ");
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine(matched.VariantCode);

                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                    }
                                    Console.WriteLine($" └─ URL Google   : {matched.GoogleUrl}");
                                    Console.ResetColor();
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[EXT-API] Błąd przetwarzania wyniku: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private ExternalScraperTask BuildRetryTask(ProductProcessingState state, ExternalScraperResult result, int newRetryCount)
    {
        string mode = _externalScraperSettings.ScrapingMode;
        string searchTerm;

        if (mode == "MatchByEan")
        {
            searchTerm = state.Ean;
        }
        else
        {
            searchTerm = state.ProductNameInStoreForGoogle;
            if (_externalScraperSettings.AppendProducerCode && !string.IsNullOrEmpty(state.ProducerCode))
                searchTerm = $"{searchTerm} {state.ProducerCode}";
            if (!string.IsNullOrEmpty(_externalScraperSettings.ProductNamePrefix))
                searchTerm = $"{_externalScraperSettings.ProductNamePrefix} {searchTerm}";
        }

        Dictionary<string, int> eligibleProductsMap = null;
        if (mode == "Standard" || mode == "full_process")
        {
            _eligibleProductsMapCache.TryGetValue(state.StoreId, out eligibleProductsMap);
        }

        List<string> extraEans = new();
        if (mode == "MatchByEan"
            && _externalScraperSettings.UseVariantEans
            && !string.IsNullOrWhiteSpace(state.OtherVariantEans))
        {
            var mainEanTrimmed = state.Ean?.Trim() ?? "";
            extraEans = state.OtherVariantEans
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => e.Length > 0 && !string.Equals(e, mainEanTrimmed, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new ExternalScraperTask
        {
            TaskId = $"product_{state.ProductId}_retry{newRetryCount}_{Guid.NewGuid():N}",
            ProductId = state.ProductId,
            ProductName = state.ProductNameInStoreForGoogle,
            SearchTerm = searchTerm,
            CleanedUrl = state.CleanedUrl,
            OriginalUrl = state.OriginalUrl,
            Ean = state.Ean,
            ProducerCode = state.ProducerCode,
            Mode = mode,
            MaxItemsToExtract = _externalScraperSettings.MaxCidsToProcess,
            UdmValue = _externalScraperSettings.SearchModeUdm,
            StoreId = state.StoreId,
            TargetCode = state.ProducerCode,
            EligibleProductsMap = eligibleProductsMap,
            ExtraEans = extraEans,
            RetryCount = newRetryCount,
            LastFailReason = result.ErrorMessage ?? result.FinalStatus,
            UseGpid = _externalScraperSettings.UseGpid,
        };
    }

    public void EnqueueTasksForExternalScrapers(int storeId, List<ProductProcessingState> products, string mode)
    {
        lock (_settingsLock)
        {

            Dictionary<string, int> eligibleProductsMap = null;

            if (mode == "Standard" || mode == "full_process")
            {
                lock (_lockMasterListInit)
                {

                    eligibleProductsMap = _masterProductStateList.Values
                        .Where(p => !string.IsNullOrEmpty(p.CleanedUrl))

                        .Where(p => p.Status == ProductStatus.Pending || p.Status == ProductStatus.NotFound || p.Status == ProductStatus.Error)
                        .GroupBy(p => p.CleanedUrl, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().ProductId, StringComparer.OrdinalIgnoreCase);
                }
            }
            if (eligibleProductsMap != null)
            {
                _eligibleProductsMapCache[storeId] = eligibleProductsMap;
            }

            var pendingProducts = products
                .Where(p => p.Status == ProductStatus.Pending)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            if (!pendingProducts.Any()) return;

            var previewIds = string.Join(", ", pendingProducts.Take(3).Select(p => p.ProductId));
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Kolejność po losowaniu (start): {previewIds}...");

            int addedCount = 0;

            foreach (var product in pendingProducts)
            {
                if (!_enqueuedProductIds.TryAdd(product.ProductId, 1))
                {
                    continue;
                }
                string searchTerm;

                if (mode == "MatchByEan")
                {
                    if (string.IsNullOrWhiteSpace(product.Ean))
                        continue;
                    searchTerm = product.Ean;
                }
                else
                {
                    searchTerm = product.ProductNameInStoreForGoogle;

                    if (_externalScraperSettings.AppendProducerCode && !string.IsNullOrEmpty(product.ProducerCode))
                        searchTerm = $"{searchTerm} {product.ProducerCode}";

                    if (!string.IsNullOrEmpty(_externalScraperSettings.ProductNamePrefix))
                        searchTerm = $"{_externalScraperSettings.ProductNamePrefix} {searchTerm}";
                }

                List<string> extraEans = new();
                if (mode == "MatchByEan"
                    && _externalScraperSettings.UseVariantEans
                    && !string.IsNullOrWhiteSpace(product.OtherVariantEans))
                {
                    var mainEanTrimmed = product.Ean?.Trim() ?? "";
                    extraEans = product.OtherVariantEans
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim())
                        .Where(e => e.Length > 0 && !string.Equals(e, mainEanTrimmed, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (extraEans.Any())
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] [MatchByEan] Produkt ID {product.ProductId}: główny EAN={product.Ean}, dodatkowych wariantów: {extraEans.Count} ({string.Join(",", extraEans)})");
                    }
                }

                var task = new ExternalScraperTask
                {
                    TaskId = $"product_{product.ProductId}_{Guid.NewGuid():N}",
                    ProductId = product.ProductId,
                    ProductName = product.ProductNameInStoreForGoogle,
                    SearchTerm = searchTerm,
                    CleanedUrl = product.CleanedUrl,
                    OriginalUrl = product.OriginalUrl,
                    Ean = product.Ean,
                    ProducerCode = product.ProducerCode,
                    Mode = mode,
                    MaxItemsToExtract = _externalScraperSettings.MaxCidsToProcess,
                    UdmValue = _externalScraperSettings.SearchModeUdm,
                    StoreId = storeId,
                    EligibleProductsMap = eligibleProductsMap,

                    TargetCode = product.ProducerCode,
                    UseGpid = _externalScraperSettings.UseGpid,
                    ExtraEans = extraEans,
                };

                _externalTaskQueue.Enqueue(task);
                addedCount++;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Dodano {addedCount} zadań. Mapa rykoszetów zawiera {eligibleProductsMap?.Count ?? 0} adresów (w tym NotFound).");
            Console.ResetColor();
        }
    }

    [HttpPost]
    [Route("api/external-scraper/start-scraping")]
    public async Task<IActionResult> StartScrapingWithExternalScrapers(
        int storeId,
        List<int> productIds,
        string mode = "Standard")
    {
        if (!_registeredScrapers.Values.Any(s => s.IsActive))
            return BadRequest(new { error = "Brak aktywnych zewnętrznych scraperów. Uruchom scraper Python." });

        InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: mode == "Standard");

        var pendingProducts = _masterProductStateList.Values
            .Where(p => p.Status == ProductStatus.Pending)
            .ToList();

        if (!pendingProducts.Any())
            return Ok(new { message = "Brak produktów do przetworzenia" });

        EnqueueTasksForExternalScrapers(storeId, pendingProducts, mode);

        return Ok(new
        {
            message = "Zadania dodane do kolejki",
            tasksEnqueued = pendingProducts.Count,
            activeScrapers = _registeredScrapers.Values.Count(s => s.IsActive),
            queueSize = _externalTaskQueue.Count
        });
    }
    [HttpPost]
    [Route("api/external-scraper/reset-queue")]
    [AllowAnonymous]
    public IActionResult ResetExternalQueue()
    {
        var apiKey = GetApiKeyFromHeader();
        bool isAuthorizedScraper = ValidateApiKey(apiKey);
        bool isAdminUser = User.Identity != null && User.Identity.IsAuthenticated;

        if (!isAuthorizedScraper && !isAdminUser)
        {
            return Unauthorized(new { error = "Unauthorized" });
        }

        _externalTaskQueue.Clear();
        _enqueuedProductIds.Clear();
        _externalResults.Clear();
        _processingStartedAt.Clear();
        _eligibleProductsMapCache.Clear();

        _registeredScrapers.Clear();

        lock (_lockMasterListInit)
        {
            _masterProductStateList.Clear();
        }

        lock (_lockTimer)
        {
            if (_batchSaveTimer != null)
            {
                _batchSaveTimer.Dispose();
                _batchSaveTimer = null;
            }
        }

        _isScrapingActive = false;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] PEŁNY RESET: kolejka, scrapery, master list, wyniki, tracking, timer.");
        return Ok(new { message = "Pełny reset wykonany — stan w pamięci wyzerowany." });
    }
    #endregion
}
