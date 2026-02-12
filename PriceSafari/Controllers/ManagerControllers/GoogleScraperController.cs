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

    private static CancellationTokenSource _currentGlobalScrapingOperationCts;
    private static CancellationTokenSource _currentCaptchaGlobalCts;
    private static readonly object _consoleLock = new object(); 

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
        public string OriginalUrl { get; set; }
        public string CleanedUrl { get; set; }
        public string ProductNameInStoreForGoogle { get; set; }

        public string Ean { get; set; }
        public string ProducerCode { get; set; }

        private ProductStatus _status;
        private string _googleUrl;
        private string _cid;
        private string _googleGid;

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
        public int? ProcessingByTaskId { get; set; }
        public bool IsDirty { get; set; }

        public ConcurrentBag<ProductGoogleCatalog> NewCatalogsFound { get; set; } = new ConcurrentBag<ProductGoogleCatalog>();

        public HashSet<string> KnownCids { get; set; } = new HashSet<string>();

        public ProductProcessingState(ProductClass product, Func<string, string> cleanUrlFunc)
        {
            ProductId = product.ProductId;
            OriginalUrl = product.Url;

            CleanedUrl = OriginalUrl;



            ProductNameInStoreForGoogle = product.ProductNameInStoreForGoogle;
            Ean = product.Ean;
            ProducerCode = product.ProducerCode;

            if (product.FoundOnGoogle == true)
            {
                _status = ProductStatus.Found;
                _googleUrl = product.GoogleUrl;
                _googleGid = product.GoogleGid;
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

        public void UpdateStatus(ProductStatus newStatus, string googleUrl = null, string cid = null, string gid = null)
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

    public enum SearchTermSource
    {
        ProductName,
        ProductUrl,
        Ean,
        ProducerCode
    }

    [HttpPost]
    public async Task<IActionResult> StartScrapingForProducts(
      int storeId,
      List<int> productIds,
      int numberOfConcurrentScrapers = 5,
      int maxCidsToProcessPerProduct = 3,
      int searchModeUdm = 3, // To już masz dodane w sygnaturze, jest OK
      SearchTermSource searchTermSource = SearchTermSource.ProductName,
      string productNamePrefix = null,
      bool useFirstMatchLogic = false,
      bool ensureNameMatch = false,
      bool allowManualCaptchaSolving = false,
      bool appendProducerCode = false,
      bool compareOnlyCurrentProductCode = false)
    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już globalnie aktywny.");
        }

        if (ensureNameMatch)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb Pośredni (z weryfikacją nazwy). Uruchamiam scraper...");

            return await StartScrapingForProducts_IntermediateMatchAsync(
                storeId,
                productIds,
                numberOfConcurrentScrapers,
                searchTermSource,
                maxCidsToProcessPerProduct,
                allowManualCaptchaSolving,
                appendProducerCode,
                compareOnlyCurrentProductCode,
                productNamePrefix,
                searchModeUdm // <--- 1. TUTAJ DODAJ (przekazanie do trybu pośredniego)
            );
        }
        else if (useFirstMatchLogic)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb 'Pierwszy Trafiony'. Uruchamiam uproszczony scraper...");

            return await StartScrapingForProducts_FirstMatchAsync(
                storeId,
                productIds,
                numberOfConcurrentScrapers,
                searchTermSource,
                productNamePrefix,
                appendProducerCode,
                searchModeUdm // <--- 2. TUTAJ DODAJ (przekazanie do trybu szybkiego)
            );
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb standardowy (dokładny). Uruchamiam pełny scraper...");

            return await StartScrapingForProducts_StandardAsync(
                storeId,
                productIds,
                numberOfConcurrentScrapers,
                maxCidsToProcessPerProduct,
                searchTermSource,
                productNamePrefix,
                allowManualCaptchaSolving,
                appendProducerCode,
                searchModeUdm // <--- 3. TUTAJ DODAJ (przekazanie do trybu standardowego)
            );
        }
    }

    public async Task<IActionResult> StartScrapingForProducts_StandardAsync(
    int storeId,
    List<int> productIds,
    int numberOfConcurrentScrapers = 5,
    int maxCidsToProcessPerProduct = 3,
    SearchTermSource searchTermSource = SearchTermSource.ProductName,
    string productNamePrefix = null,
    bool allowManualCaptchaSolving = false,
    bool appendProducerCode = false,
    int searchModeUdm = 3)
    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już globalnie aktywny.");
        }
        _isScrapingActive = true;

        if (maxCidsToProcessPerProduct <= 0)
        {
            _isScrapingActive = false;
            return BadRequest("Liczba CIDów do przetworzenia musi być dodatnia.");
        }

        string finalMessage = $"Proces scrapowania dla sklepu {storeId} zainicjowany.";

        lock (_lockTimer)
        {
            if (_batchSaveTimer == null)
            {
                _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer do zapisu wsadowego globalnie uruchomiony.");
            }
        }

        try
        {
            if (allowManualCaptchaSolving)
            {

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Uruchamiam scrapowanie w TRYBIE RĘCZNYM. Operacja nie będzie automatycznie restartowana.");

                _currentGlobalScrapingOperationCts = new CancellationTokenSource();
                _currentCaptchaGlobalCts = new CancellationTokenSource();

                var activeScrapingTasks = new List<Task>();
                var scraperInstances = new List<GoogleScraper>();

                try
                {
                    var initTasks = new List<Task>();
                    for (int i = 0; i < numberOfConcurrentScrapers; i++)
                    {
                        var sc = new GoogleScraper();
                        scraperInstances.Add(sc);
                        initTasks.Add(sc.InitializeBrowserAsync());
                    }
                    await Task.WhenAll(initTasks);
                    var availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstances);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Utworzono i zainicjowano {scraperInstances.Count} instancji scrapera do puli.");

                    InitializeMasterProductListIfNeeded(storeId, productIds, false);

                    var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);

                    while (!_currentGlobalScrapingOperationCts.IsCancellationRequested)
                    {
                        List<int> pendingProductIds;
                        lock (_lockMasterListInit)
                        {
                            pendingProductIds = _masterProductStateList
                                .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                                .Select(kvp => kvp.Key).ToList();
                        }

                        if (!pendingProductIds.Any())
                        {
                            if (activeScrapingTasks.Any(t => !t.IsCompleted))
                            {
                                await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray());
                                activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsFaulted || t.IsCanceled);
                                continue;
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Brak produktów i zadań. Kończę.");
                                break;
                            }
                        }

                        await semaphore.WaitAsync(_currentGlobalScrapingOperationCts.Token);
                        if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { semaphore.Release(); break; }

                        int selectedProductId;
                        ProductProcessingState productStateToProcess;

                        lock (_lockMasterListInit)
                        {

                            var currentPendingIds = _masterProductStateList
                                .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                                .Select(kvp => kvp.Key).ToList();

                            if (!currentPendingIds.Any()) { semaphore.Release(); continue; }

                            selectedProductId = currentPendingIds[_random.Next(currentPendingIds.Count)];
                            if (!_masterProductStateList.TryGetValue(selectedProductId, out productStateToProcess)) { semaphore.Release(); continue; }
                        }

                        bool canProcess = false;
                        lock (productStateToProcess)
                        {
                            if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
                            {
                                productStateToProcess.Status = ProductStatus.Processing;
                                productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                                canProcess = true;
                            }
                        }

                        if (canProcess && availableScrapersPool.TryTake(out var assignedScraper))
                        {
                            var task = Task.Run(async () => {
                                try
                                {
                                    await ProcessSingleProductAsync(productStateToProcess, assignedScraper, storeId, _masterProductStateList, _currentCaptchaGlobalCts, maxCidsToProcessPerProduct, searchTermSource, productNamePrefix, allowManualCaptchaSolving, appendProducerCode, searchModeUdm);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"BŁĄD w zadaniu dla ID {productStateToProcess.ProductId}: {ex.Message}");
                                    lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Error); }
                                }
                                finally
                                {
                                    lock (productStateToProcess) { productStateToProcess.ProcessingByTaskId = null; }
                                    availableScrapersPool.Add(assignedScraper);
                                    semaphore.Release();
                                }
                            });
                            activeScrapingTasks.Add(task);
                        }
                        else
                        {
                            semaphore.Release();
                        }
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Główna pętla zakończona. Czekam na ukończenie {activeScrapingTasks.Count(t => !t.IsCompleted)} zadań...");
                    await Task.WhenAll(activeScrapingTasks.ToArray());
                    finalMessage = $"Scrapowanie w trybie ręcznym dla sklepu {storeId} zakończone.";
                }
                catch (OperationCanceledException)
                {
                    finalMessage = "Scrapowanie w trybie ręcznym zatrzymane przez użytkownika.";
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {finalMessage}");
                }
                catch (Exception ex)
                {
                    finalMessage = $"Błąd krytyczny w trybie ręcznym: {ex.Message}";
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {finalMessage}");
                }
                finally
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Końcowe sprzątanie. Zamykanie {scraperInstances.Count} przeglądarek.");
                    var closingTasks = scraperInstances.Select(sc => sc.CloseBrowserAsync()).ToList();
                    await Task.WhenAll(closingTasks);
                    await BatchUpdateDatabaseAsync(true, CancellationToken.None);
                    _currentGlobalScrapingOperationCts?.Dispose();
                    _currentCaptchaGlobalCts?.Dispose();
                }

            }
            else
            {

                int restartAttempt = 0;
                const int MAX_RESTARTS_ON_CAPTCHA = 100;
                bool overallOperationSuccess = false;
                bool lastAttemptEndedDueToCaptcha = false;

                do
                {
                    bool captchaDetectedInCurrentAttempt = false;
                    _currentGlobalScrapingOperationCts = new CancellationTokenSource();
                    _currentCaptchaGlobalCts = new CancellationTokenSource();
                    var activeScrapingTasks = new List<Task>();
                    overallOperationSuccess = false;

                    List<GoogleScraper> scraperInstancesForThisAttempt = new List<GoogleScraper>();
                    ConcurrentBag<GoogleScraper> availableScrapersPool = null;

                    if (restartAttempt > 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] === Próba restartu scrapowania nr {restartAttempt} po CAPTCHA dla sklepu {storeId} ===");
                        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rozpoczynam próbę scrapowania nr {restartAttempt} dla sklepu ID: {storeId}.");

                    try
                    {
                        using (var initialScope = _scopeFactory.CreateScope())
                        {
                            var context = initialScope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                            var store = await context.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId);
                            if (store == null) { finalMessage = "Sklep nie znaleziony."; _isScrapingActive = false; return NotFound(finalMessage); }
                        }
                        if (numberOfConcurrentScrapers <= 0) { finalMessage = "Liczba scraperów musi być dodatnia."; _isScrapingActive = false; return BadRequest(finalMessage); }

                        var initTasks = new List<Task>();
                        for (int i = 0; i < numberOfConcurrentScrapers; i++)
                        {
                            var sc = new GoogleScraper();
                            scraperInstancesForThisAttempt.Add(sc);
                            initTasks.Add(sc.InitializeBrowserAsync());
                        }
                        await Task.WhenAll(initTasks);
                        availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstancesForThisAttempt);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Utworzono i zainicjowano {scraperInstancesForThisAttempt.Count} instancji scrapera do puli.");

                        InitializeMasterProductListIfNeeded(storeId, productIds, restartAttempt > 0);

                        var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
                        var linkedCtsForAttempt = CancellationTokenSource.CreateLinkedTokenSource(_currentGlobalScrapingOperationCts.Token, _currentCaptchaGlobalCts.Token);

                        while (!_currentGlobalScrapingOperationCts.IsCancellationRequested && !_currentCaptchaGlobalCts.IsCancellationRequested)
                        {
                            List<int> pendingProductIds;
                            lock (_lockMasterListInit)
                            {
                                pendingProductIds = _masterProductStateList
                                    .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                                    .Select(kvp => kvp.Key).ToList();
                            }

                            if (!pendingProductIds.Any())
                            {
                                if (activeScrapingTasks.Any(t => !t.IsCompleted))
                                {
                                    try { await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray()); } catch { }
                                    activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);
                                }
                                else
                                {
                                    overallOperationSuccess = true;
                                    break;
                                }
                            }
                            else
                            {
                                await semaphore.WaitAsync(linkedCtsForAttempt.Token);
                                if (linkedCtsForAttempt.IsCancellationRequested) { semaphore.Release(); break; }

                                int selectedProductId;
                                ProductProcessingState productStateToProcess;
                                lock (_lockMasterListInit)
                                {
                                    var currentPendingIds = _masterProductStateList
                                        .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                                        .Select(kvp => kvp.Key).ToList();
                                    if (!currentPendingIds.Any()) { semaphore.Release(); continue; }
                                    selectedProductId = currentPendingIds[_random.Next(currentPendingIds.Count)];
                                    if (!_masterProductStateList.TryGetValue(selectedProductId, out productStateToProcess)) { semaphore.Release(); continue; }
                                }

                                bool canProcess = false;
                                lock (productStateToProcess)
                                {
                                    if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
                                    {
                                        productStateToProcess.Status = ProductStatus.Processing;
                                        productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                                        canProcess = true;
                                    }
                                }

                                if (canProcess && availableScrapersPool.TryTake(out var assignedScraper))
                                {
                                    var task = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await ProcessSingleProductAsync(productStateToProcess, assignedScraper, storeId, _masterProductStateList, _currentCaptchaGlobalCts, maxCidsToProcessPerProduct, searchTermSource, productNamePrefix, allowManualCaptchaSolving, appendProducerCode, searchModeUdm);
                                        }
                                        catch (Exception ex)
                                        {
                                            lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Error); }
                                        }
                                        finally
                                        {
                                            lock (productStateToProcess) { productStateToProcess.ProcessingByTaskId = null; }
                                            availableScrapersPool.Add(assignedScraper);
                                            semaphore.Release();
                                        }
                                    }, linkedCtsForAttempt.Token);
                                    activeScrapingTasks.Add(task);
                                }
                                else
                                {
                                    semaphore.Release();
                                }
                            }
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Główna pętla zakończona. Czekam na zadania...");
                        try { await Task.WhenAll(activeScrapingTasks.ToArray()); } catch (OperationCanceledException) { }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Operacja ANULOWANA.");
                        if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] BŁĄD KRYTYCZNY: {ex.Message}");
                        finalMessage = $"Błąd krytyczny (sklep {storeId}): {ex.Message}";
                        break;
                    }
                    finally
                    {
                        if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true;
                        if (captchaDetectedInCurrentAttempt && !_currentGlobalScrapingOperationCts.IsCancellationRequested)
                        {
                            _currentGlobalScrapingOperationCts.Cancel();
                        }

                        try { await Task.WhenAll(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray()); } catch { }
                        await BatchUpdateDatabaseAsync(true, CancellationToken.None);

                        _currentGlobalScrapingOperationCts?.Dispose();
                        _currentCaptchaGlobalCts?.Dispose();
                    }

                    lastAttemptEndedDueToCaptcha = captchaDetectedInCurrentAttempt;

                    if (lastAttemptEndedDueToCaptcha)
                    {
                        restartAttempt++;
                        if (restartAttempt <= MAX_RESTARTS_ON_CAPTCHA)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CAPTCHA. Próba {restartAttempt - 1} nieudana. Reset sieci...");
                            bool networkResetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
                            if (!networkResetSuccess) { finalMessage = $"Reset sieci FAIL po CAPTCHA. Stop po {restartAttempt - 1} próbach."; break; }
                        }
                        else
                        {
                            finalMessage = $"MAX ({MAX_RESTARTS_ON_CAPTCHA}) restartów po CAPTCHA. Stop.";
                            break;
                        }
                    }
                    else if (overallOperationSuccess) { finalMessage = $"Sklep {storeId} OK."; break; }
                    else if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { finalMessage = $"Sklep {storeId} STOP (sygnał)."; break; }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sprzątanie po próbie {restartAttempt - 1}: Zamykanie {scraperInstancesForThisAttempt.Count} instancji scrapera.");
                    var closingTasks = scraperInstancesForThisAttempt.Select(sc => sc.CloseBrowserAsync()).ToList();
                    await Task.WhenAll(closingTasks);
                    scraperInstancesForThisAttempt.Clear();

                } while (lastAttemptEndedDueToCaptcha && restartAttempt <= MAX_RESTARTS_ON_CAPTCHA);

            }
        }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] OSTATECZNE finally...");
            lock (_lockTimer)
            {
                if (_batchSaveTimer != null) { _batchSaveTimer.Dispose(); _batchSaveTimer = null; Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Globalny timer Zatrzymany."); }
            }
            _isScrapingActive = false;

            _currentGlobalScrapingOperationCts?.Dispose();
            _currentCaptchaGlobalCts?.Dispose();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Flaga _isScrapingActive=false. Gotowy na nowe żądanie.");
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ostateczny status: {finalMessage}");
        return Content(finalMessage);
    }

    private async Task<IActionResult> StartScrapingForProducts_FirstMatchAsync(int storeId, List<int> productIds, int numberOfConcurrentScrapers, SearchTermSource searchTermSource, string productNamePrefix, bool appendProducerCode = false, int searchModeUdm = 3)
    {
        _isScrapingActive = true;
        _currentGlobalScrapingOperationCts = new CancellationTokenSource();

        lock (_lockTimer)
        {
            if (_batchSaveTimer == null)
            {
                _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            }
        }

        try
        {

            InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: false);

            var scraperInstances = new List<GoogleScraper>();
            var initTasks = new List<Task>();
            for (int i = 0; i < numberOfConcurrentScrapers; i++)
            {
                var sc = new GoogleScraper();
                scraperInstances.Add(sc);
                initTasks.Add(sc.InitializeBrowserAsync());
            }
            await Task.WhenAll(initTasks);
            var availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstances);

            var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
            var activeTasks = new List<Task>();

            while (!_currentGlobalScrapingOperationCts.IsCancellationRequested)
            {
                var pendingProductIds = _masterProductStateList
                    .Where(p => p.Value.Status == ProductStatus.Pending && p.Value.ProcessingByTaskId == null)
                    .Select(p => p.Key).ToList();

                if (!pendingProductIds.Any())
                {
                    if (activeTasks.Any(t => !t.IsCompleted))
                    {
                        await Task.WhenAny(activeTasks.Where(t => !t.IsCompleted).ToArray());
                        activeTasks.RemoveAll(t => t.IsCompleted);
                        continue;
                    }
                    break;
                }

                await semaphore.WaitAsync(_currentGlobalScrapingOperationCts.Token);

                int productId = pendingProductIds[_random.Next(pendingProductIds.Count)];
                if (_masterProductStateList.TryGetValue(productId, out var productState) && productState.Status == ProductStatus.Pending)
                {
                    lock (productState)
                    {
                        productState.Status = ProductStatus.Processing;
                        productState.ProcessingByTaskId = Task.CurrentId;
                    }

                    availableScrapersPool.TryTake(out var scraper);

                    var task = Task.Run(async () =>
                    {
                        try
                        {

                            await ProcessSingleProduct_FirstMatchAsync(
                                productState,
                                scraper,
                                searchTermSource,
                                productNamePrefix,
                                _currentGlobalScrapingOperationCts,
                                appendProducerCode,
                                searchModeUdm

                            );
                        }
                        finally
                        {
                            lock (productState) { productState.ProcessingByTaskId = null; }
                            availableScrapersPool.Add(scraper);
                            semaphore.Release();
                        }
                    }, _currentGlobalScrapingOperationCts.Token);
                    activeTasks.Add(task);
                }
                else
                {
                    semaphore.Release();
                }
            }
            await Task.WhenAll(activeTasks);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tryb 'Pierwszy Trafiony' został zatrzymany.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BŁĄD w trybie 'Pierwszy Trafiony': {ex.Message}");
            return StatusCode(500, $"Błąd w trybie 'Pierwszy Trafiony': {ex.Message}");
        }
        finally
        {
            await BatchUpdateDatabaseAsync(true, CancellationToken.None);
            lock (_lockTimer)
            {
                _batchSaveTimer?.Dispose();
                _batchSaveTimer = null;
            }
            _isScrapingActive = false;
            _currentGlobalScrapingOperationCts?.Dispose();
        }

        return Content("Proces 'Pierwszy Trafiony' zakończony.");
    }

    private async Task ProcessSingleProduct_FirstMatchAsync(ProductProcessingState productState, GoogleScraper scraper, SearchTermSource termSource, string namePrefix, CancellationTokenSource cts, bool appendProducerCode, int searchModeUdm = 3)
    {
        if (cts.IsCancellationRequested) return;

        string searchTermBase;
        switch (termSource)
        {
            case SearchTermSource.ProductUrl: searchTermBase = productState.OriginalUrl; break;
            case SearchTermSource.Ean: searchTermBase = productState.Ean; break;
            case SearchTermSource.ProducerCode: searchTermBase = productState.ProducerCode; break;
            case SearchTermSource.ProductName:
            default:
                searchTermBase = productState.ProductNameInStoreForGoogle;

                if (appendProducerCode && !string.IsNullOrWhiteSpace(productState.ProducerCode))
                {
                    searchTermBase = $"{searchTermBase} {productState.ProducerCode}";
                }

                if (!string.IsNullOrWhiteSpace(namePrefix))
                {
                    searchTermBase = $"{namePrefix} {searchTermBase}";
                }
                break;
        }

        if (string.IsNullOrWhiteSpace(searchTermBase))
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] Przetwarzam ID: {productState.ProductId}, Szukam: '{searchTermBase}'");

        var identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermBase, maxItemsToExtract: 1, udmValue: searchModeUdm);

        if (identifierResult.CaptchaEncountered)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] CAPTCHA! Zatrzymuję operację.");
            if (!cts.IsCancellationRequested) cts.Cancel();
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
            return;
        }

        if (identifierResult.IsSuccess && identifierResult.Data.Any())
        {

            var firstIdentifier = identifierResult.Data.First();
            var firstCid = firstIdentifier.Cid;
            var firstGid = firstIdentifier.Gid;
            var googleUrl = $"https://www.google.com/shopping/product/{firstCid}";

            lock (productState)
            {

                productState.UpdateStatus(ProductStatus.Found, googleUrl, firstCid, firstGid);
            }
            lock (_consoleLock)
            {
                Console.Write($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] ✓ ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Znaleziono");
                Console.ResetColor();
                Console.WriteLine($" dla ID {productState.ProductId}. CID: {firstCid}, GID: {firstGid}");
            }

        }
        else
        {
            lock (productState)
            {
                productState.UpdateStatus(ProductStatus.NotFound);
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] ✗ Nie znaleziono dla ID {productState.ProductId}.");
        }
    }
    private async Task<IActionResult> StartScrapingForProducts_IntermediateMatchAsync(
      int storeId,
      List<int> productIds,
      int numberOfConcurrentScrapers,
      SearchTermSource searchTermSource,
      int maxCidsToProcess,
      bool allowManualCaptchaSolving,
      bool appendProducerCode = false,
      bool compareOnlyCurrentProductCode = false,
      string productNamePrefix = null,
      int searchModeUdm = 3)

    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już globalnie aktywny.");
        }
        _isScrapingActive = true;

        string finalMessage = $"Proces scrapowania (Tryb Pośredni) dla sklepu {storeId} zainicjowany.";

        lock (_lockTimer)
        {
            if (_batchSaveTimer == null)
            {
                _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer do zapisu wsadowego globalnie uruchomiony.");
            }
        }

        try
        {

            if (allowManualCaptchaSolving)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] Uruchamiam scrapowanie w TRYBIE RĘCZNYM. Operacja nie będzie automatycznie restartowana.");
                _currentGlobalScrapingOperationCts = new CancellationTokenSource();
                _currentCaptchaGlobalCts = new CancellationTokenSource();
                var scraperInstances = new List<GoogleScraper>();

                try
                {
                    var initTasks = new List<Task>();
                    for (int i = 0; i < numberOfConcurrentScrapers; i++)
                    {
                        var sc = new GoogleScraper();
                        scraperInstances.Add(sc);
                        initTasks.Add(sc.InitializeBrowserAsync());
                    }
                    await Task.WhenAll(initTasks);
                    var availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstances);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Utworzono i zainicjowano {scraperInstances.Count} instancji scrapera do puli.");

                    InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: false);

                    var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
                    var activeScrapingTasks = new List<Task>();

                    while (!_currentGlobalScrapingOperationCts.IsCancellationRequested)
                    {
                        var pendingProductIds = _masterProductStateList
                            .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                            .Select(kvp => kvp.Key).ToList();

                        if (!pendingProductIds.Any())
                        {
                            if (activeScrapingTasks.Any(t => !t.IsCompleted))
                            {
                                await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray());
                                activeScrapingTasks.RemoveAll(t => t.IsCompleted);
                                continue;
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Brak produktów i zadań. Kończę.");
                                break;
                            }
                        }

                        await semaphore.WaitAsync(_currentGlobalScrapingOperationCts.Token);
                        if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { semaphore.Release(); break; }

                        int selectedProductId = pendingProductIds[_random.Next(pendingProductIds.Count)];
                        if (_masterProductStateList.TryGetValue(selectedProductId, out var productStateToProcess) && availableScrapersPool.TryTake(out var assignedScraper))
                        {
                            bool canProcess = false;
                            lock (productStateToProcess)
                            {
                                if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
                                {
                                    productStateToProcess.Status = ProductStatus.Processing;
                                    productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                                    canProcess = true;
                                }
                            }

                            if (canProcess)
                            {
                                var task = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await ProcessSingleProduct_IntermediateMatchAsync(
                    productStateToProcess,
                    assignedScraper,
                    searchTermSource,
                    _currentCaptchaGlobalCts,
                    maxCidsToProcess,
                    allowManualCaptchaSolving,
                    appendProducerCode,
                    compareOnlyCurrentProductCode,
                    productNamePrefix,
                    searchModeUdm

                );
                                    }
                                    finally
                                    {
                                        lock (productStateToProcess) { productStateToProcess.ProcessingByTaskId = null; }
                                        availableScrapersPool.Add(assignedScraper);
                                        semaphore.Release();
                                    }
                                });
                                activeScrapingTasks.Add(task);
                            }
                            else
                            {
                                availableScrapersPool.Add(assignedScraper);
                                semaphore.Release();
                            }
                        }
                        else
                        {
                            semaphore.Release();
                        }
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Główna pętla zakończona. Czekam na ukończenie zadań...");
                    await Task.WhenAll(activeScrapingTasks.ToArray());
                    finalMessage = $"Scrapowanie w trybie ręcznym (pośrednim) dla sklepu {storeId} zakończone.";
                }
                finally
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Ręczny] Końcowe sprzątanie. Zamykanie {scraperInstances.Count} przeglądarek.");
                    var closingTasks = scraperInstances.Select(sc => sc.CloseBrowserAsync()).ToList();
                    await Task.WhenAll(closingTasks);
                    await BatchUpdateDatabaseAsync(true, CancellationToken.None);
                    _currentGlobalScrapingOperationCts?.Dispose();
                    _currentCaptchaGlobalCts?.Dispose();
                }
            }

            else
            {
                int restartAttempt = 0;
                const int MAX_RESTARTS_ON_CAPTCHA = 100;
                bool overallOperationSuccess = false;
                bool lastAttemptEndedDueToCaptcha = false;

                do
                {
                    bool captchaDetectedInCurrentAttempt = false;
                    _currentGlobalScrapingOperationCts = new CancellationTokenSource();
                    _currentCaptchaGlobalCts = new CancellationTokenSource();
                    var activeScrapingTasks = new List<Task>();
                    overallOperationSuccess = false;

                    List<GoogleScraper> scraperInstancesForThisAttempt = new List<GoogleScraper>();
                    ConcurrentBag<GoogleScraper> availableScrapersPool = null;

                    if (restartAttempt > 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] === Próba restartu (Tryb Pośredni) nr {restartAttempt} po CAPTCHA dla sklepu {storeId} ===");
                        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rozpoczynam próbę (Tryb Pośredni) nr {restartAttempt} dla sklepu ID: {storeId}.");

                    try
                    {
                        var initTasks = new List<Task>();
                        for (int i = 0; i < numberOfConcurrentScrapers; i++)
                        {
                            var sc = new GoogleScraper();
                            scraperInstancesForThisAttempt.Add(sc);
                            initTasks.Add(sc.InitializeBrowserAsync());
                        }
                        await Task.WhenAll(initTasks);
                        availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstancesForThisAttempt);

                        InitializeMasterProductListIfNeeded(storeId, productIds, restartAttempt > 0, requireUrl: false);

                        var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
                        var linkedCtsForAttempt = CancellationTokenSource.CreateLinkedTokenSource(_currentGlobalScrapingOperationCts.Token, _currentCaptchaGlobalCts.Token);

                        while (!_currentGlobalScrapingOperationCts.IsCancellationRequested && !_currentCaptchaGlobalCts.IsCancellationRequested)
                        {

                            List<int> pendingProductIds;
                            lock (_lockMasterListInit)
                            {
                                pendingProductIds = _masterProductStateList
                                    .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
                                    .Select(kvp => kvp.Key).ToList();
                            }

                            if (!pendingProductIds.Any())
                            {
                                if (activeScrapingTasks.Any(t => !t.IsCompleted))
                                {
                                    try { await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray()); } catch (OperationCanceledException) { }
                                    activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);
                                }
                                else
                                {
                                    overallOperationSuccess = true;
                                    break;
                                }
                            }
                            else
                            {
                                await semaphore.WaitAsync(linkedCtsForAttempt.Token);
                                if (linkedCtsForAttempt.Token.IsCancellationRequested) { semaphore.Release(); break; }

                                int selectedProductId = pendingProductIds[_random.Next(pendingProductIds.Count)];

                                if (_masterProductStateList.TryGetValue(selectedProductId, out var productStateToProcess) && availableScrapersPool.TryTake(out var assignedScraper))
                                {
                                    bool canProcess = false;
                                    lock (productStateToProcess)
                                    {
                                        if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
                                        {
                                            productStateToProcess.Status = ProductStatus.Processing;
                                            productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                                            canProcess = true;
                                        }
                                    }

                                    if (canProcess)
                                    {
                                        var task = Task.Run(async () =>
                                        {
                                            try
                                            {

                                                await ProcessSingleProduct_IntermediateMatchAsync(
                                                    productStateToProcess, assignedScraper, searchTermSource,
                                                    _currentCaptchaGlobalCts, maxCidsToProcess, allowManualCaptchaSolving, appendProducerCode,
                                                    compareOnlyCurrentProductCode, productNamePrefix);
                                            }
                                            finally
                                            {
                                                lock (productStateToProcess) { productStateToProcess.ProcessingByTaskId = null; }
                                                availableScrapersPool.Add(assignedScraper);
                                                semaphore.Release();
                                            }
                                        }, linkedCtsForAttempt.Token);
                                        activeScrapingTasks.Add(task);
                                    }
                                    else
                                    {
                                        availableScrapersPool.Add(assignedScraper);
                                        semaphore.Release();
                                    }
                                }
                                else
                                {
                                    semaphore.Release();
                                }
                            }
                        }
                        try { await Task.WhenAll(activeScrapingTasks.ToArray()); } catch (OperationCanceledException) { }
                    }
                    catch (OperationCanceledException)
                    {
                        if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true;
                    }
                    finally
                    {
                        if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true;
                        if (captchaDetectedInCurrentAttempt && !_currentGlobalScrapingOperationCts.IsCancellationRequested)
                        {
                            _currentGlobalScrapingOperationCts.Cancel();
                        }
                        await BatchUpdateDatabaseAsync(true, CancellationToken.None);
                        foreach (var sc in scraperInstancesForThisAttempt) { await sc.CloseBrowserAsync(); }
                        scraperInstancesForThisAttempt.Clear();
                        _currentGlobalScrapingOperationCts?.Dispose();
                        _currentCaptchaGlobalCts?.Dispose();
                    }

                    lastAttemptEndedDueToCaptcha = captchaDetectedInCurrentAttempt;

                    if (lastAttemptEndedDueToCaptcha)
                    {
                        restartAttempt++;
                        if (restartAttempt <= MAX_RESTARTS_ON_CAPTCHA)
                        {
                            bool networkResetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
                            if (!networkResetSuccess) { finalMessage = $"Reset sieci FAIL po CAPTCHA. Stop po {restartAttempt - 1} próbach."; break; }
                        }
                        else
                        {
                            finalMessage = $"Osiągnięto MAX ({MAX_RESTARTS_ON_CAPTCHA}) restartów po CAPTCHA. Zatrzymuję.";
                            break;
                        }
                    }
                    else if (overallOperationSuccess) { finalMessage = $"Sklep {storeId} (Tryb Pośredni) zakończony pomyślnie."; break; }
                } while (lastAttemptEndedDueToCaptcha && restartAttempt <= MAX_RESTARTS_ON_CAPTCHA);
            }
        }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] OSTATECZNE finally (Tryb Pośredni)...");
            lock (_lockTimer)
            {
                if (_batchSaveTimer != null)
                {
                    _batchSaveTimer.Dispose();
                    _batchSaveTimer = null;
                }
            }
            _isScrapingActive = false;
            _currentGlobalScrapingOperationCts?.Dispose();
            _currentCaptchaGlobalCts?.Dispose();
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ostateczny status (Tryb Pośredni): {finalMessage}");
        return Content(finalMessage);
    }

    private async Task ProcessSingleProduct_IntermediateMatchAsync(
    ProductProcessingState productState,
    GoogleScraper scraper,
    SearchTermSource termSource,
    CancellationTokenSource cts,
    int maxItemsToExtract,
    bool allowManualCaptchaSolving,
    bool appendProducerCode,
    bool compareOnlyCurrentProductCode,
    string productNamePrefix,
    int searchModeUdm = 3)

    {
    RestartProductProcessing:

        if (cts.IsCancellationRequested && !allowManualCaptchaSolving)
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
            return;
        }
        string searchTermForGoogle;

        switch (termSource)
        {
            case SearchTermSource.ProductName:
                searchTermForGoogle = productState.ProductNameInStoreForGoogle;
                if (appendProducerCode && !string.IsNullOrWhiteSpace(productState.ProducerCode))
                {
                    searchTermForGoogle = $"{searchTermForGoogle} {productState.ProducerCode}";
                }
                break;

            case SearchTermSource.Ean:
                searchTermForGoogle = productState.Ean;
                break;

            case SearchTermSource.ProducerCode:
            default:
                searchTermForGoogle = productState.ProducerCode;
                break;
        }

        if (!string.IsNullOrWhiteSpace(productNamePrefix) && !string.IsNullOrWhiteSpace(searchTermForGoogle))
        {
            searchTermForGoogle = $"{productNamePrefix} {searchTermForGoogle}";
        }

        if (string.IsNullOrWhiteSpace(searchTermForGoogle))
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Brak terminu do wyszukania."); }
            return;
        }

        Dictionary<string, ProductProcessingState> eligibleProductsMap;
        lock (_lockMasterListInit)
        {
            if (compareOnlyCurrentProductCode)
            {

                string cleanedCurrentCode = productState.ProducerCode?.Replace(" ", "").Trim();

                if (!string.IsNullOrEmpty(cleanedCurrentCode) &&
                    (productState.Status == ProductStatus.Pending || productState.Status == ProductStatus.Processing))
                {
                    eligibleProductsMap = new Dictionary<string, ProductProcessingState>(StringComparer.OrdinalIgnoreCase)
             {
                 { cleanedCurrentCode, productState }
             };
                }
                else
                {

                    eligibleProductsMap = new Dictionary<string, ProductProcessingState>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {

                eligibleProductsMap = _masterProductStateList.Values
                    .Where(p => (p.Status == ProductStatus.Pending || p.Status == ProductStatus.Processing)
                                && !string.IsNullOrEmpty(p.ProducerCode))
                    .GroupBy(p => p.ProducerCode.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First());
            }
        }


        if (!eligibleProductsMap.Any())
        {
            lock (_consoleLock)
            {
                if (compareOnlyCurrentProductCode)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni - OGRANICZONY] Produkt ID: {productState.ProductId} nie kwalifikuje się do dopasowania. Brak kodu lub zły status.");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni - PEŁNA PULA] Brak produktów oczekujących na dopasowanie po kodzie producenta.");
                }
            }

            lock (productState) { if (productState.Status == ProductStatus.Processing) { productState.UpdateStatus(ProductStatus.NotFound); } }
            return;
        }

        lock (_consoleLock)
        {
            string mode = compareOnlyCurrentProductCode ? "OGRANICZONY (tylko własny kod)" : "PEŁNA PULA (wiele kodów)";
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni - {mode}] === Rozpoczynam przetwarzanie produktu ID: {productState.ProductId} ===");
            Console.WriteLine($"  > Wyszukiwana fraza: '{searchTermForGoogle}'");
            Console.WriteLine($"  > Liczba kodów producenta do sprawdzenia: {eligibleProductsMap.Count}");

            if (!compareOnlyCurrentProductCode)
            {
                Console.WriteLine("  > Przykładowe kody do sprawdzenia:");
                foreach (var code in eligibleProductsMap.Keys.Take(5))
                {
                    Console.WriteLine($"    - {code}");
                }
                if (eligibleProductsMap.Count > 5) Console.WriteLine("    - ...");
            }
        }

        var identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermForGoogle, maxItemsToExtract: maxItemsToExtract, udmValue: searchModeUdm);

        if (identifierResult.CaptchaEncountered)
        {
            if (allowManualCaptchaSolving)
            {
                bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                if (solved) { goto RestartProductProcessing; }
                else { lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Timeout ręcznego rozwiązania CAPTCHA"); } return; }
            }
            else
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
                lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                return;
            }
        }

        if (cts.IsCancellationRequested) return;

        if (!identifierResult.IsSuccess || !identifierResult.Data.Any())
        {
            lock (productState) { if (productState.Status == ProductStatus.Processing) { productState.UpdateStatus(ProductStatus.NotFound); } }
            return;
        }

        bool initiatingProductMatched = false;

        foreach (var identifier in identifierResult.Data)
        {

            if (cts.IsCancellationRequested || !eligibleProductsMap.Any()) break;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] Sprawdzam identyfikator CID: {identifier.Cid}, GID: {identifier.Gid}");

            var detailsResult = await scraper.GetProductDetailsFromApiAsync(identifier.Cid, identifier.Gid);

            if (detailsResult.CaptchaEncountered)
            {
                if (allowManualCaptchaSolving)
                {
                    lock (productState) { productState.UpdateStatus(ProductStatus.Error, "CAPTCHA na poziomie API w trybie ręcznym"); }
                    return;
                }
                else
                {
                    if (!cts.IsCancellationRequested) cts.Cancel();
                    lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                    return;
                }
            }

            if (detailsResult.IsSuccess && detailsResult.Data?.Details != null)
            {
                var allTitlesToCheck = new List<string>();
                if (!string.IsNullOrEmpty(detailsResult.Data.Details.MainTitle))
                {
                    allTitlesToCheck.Add(detailsResult.Data.Details.MainTitle);
                }
                allTitlesToCheck.AddRange(detailsResult.Data.Details.OfferTitles);

                lock (_consoleLock)
                {
                    Console.WriteLine($"  > Analizuję odpowiedź z URL: {detailsResult.Data.RequestUrl}");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"    > Tytuł główny: {detailsResult.Data.Details.MainTitle ?? "BRAK"}");
                    Console.WriteLine($"    > Znalezione tytuły ofert ({detailsResult.Data.Details.OfferTitles.Count} szt.):");
                    foreach (var offerTitle in detailsResult.Data.Details.OfferTitles)
                    {
                        Console.WriteLine($"      - {offerTitle}");
                    }
                    Console.ResetColor();
                }

                foreach (var title in allTitlesToCheck.Distinct())
                {

                    if (!eligibleProductsMap.Any()) break;

                    string cleanedTitle = title.Replace(" ", "").Trim();
                    var codesToCheck = eligibleProductsMap.Keys.ToList();

                    foreach (var cleanedCode in codesToCheck)
                    {
                        if (cleanedTitle.Contains(cleanedCode, StringComparison.OrdinalIgnoreCase))
                        {
                            if (eligibleProductsMap.TryGetValue(cleanedCode, out var matchedState))
                            {
                                lock (matchedState)
                                {
                                    if (matchedState.Status != ProductStatus.Found)
                                    {
                                        string googleUrl = $"https://www.google.com/shopping/product/{identifier.Cid}";
                                        matchedState.UpdateStatus(ProductStatus.Found, googleUrl, identifier.Cid, identifier.Gid);

                                        lock (_consoleLock)
                                        {
                                            Console.Write($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✓ ZNALEZIONO ");
                                            Console.ForegroundColor = ConsoleColor.Green;
                                            Console.WriteLine("DOPASOWANIE!");
                                            Console.ResetColor();
                                            Console.WriteLine($"  > Dopasowany kod: '{cleanedCode}'");
                                            Console.WriteLine($"  > W tytule: '{title}'");
                                            Console.WriteLine($"  > Dla produktu ID: {matchedState.ProductId}");
                                        }
                                    }
                                }

                                eligibleProductsMap.Remove(cleanedCode);

                                if (matchedState.ProductId == productState.ProductId)
                                {
                                    initiatingProductMatched = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        lock (productState)
        {

            if (!initiatingProductMatched && productState.Status == ProductStatus.Processing)
            {
                productState.UpdateStatus(ProductStatus.NotFound);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✗ Produkt inicjujący ID {productState.ProductId} NIE został dopasowany.");
            }
        }
    }

    [HttpPost("stop")]
    public IActionResult StopScraping()
    {
        // 1. Zatrzymaj wewnętrzne
        if (_isScrapingActive)
        {
            _currentGlobalScrapingOperationCts?.Cancel();
        }

        // 2. Zatrzymaj zewnętrzne (wyczyść kolejkę)
        _externalTaskQueue.Clear();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ZATRZYMANIE: Wątki wewnętrzne anulowane, kolejka zewnętrzna wyczyszczona.");

        return Ok("Zatrzymano procesy i wyczyszczono kolejki.");
    }

    private async Task TimerBatchUpdateCallback(CancellationToken cancellationToken)
    {

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
                    var tempScraperForCleaning = new GoogleScraper();

                    foreach (var dbProduct in productsFromDb)
                    {
                        var state = new ProductProcessingState(dbProduct, tempScraperForCleaning.CleanUrlParameters);

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

                            if (dbProduct.FoundOnGoogle != true || dbProduct.GoogleUrl != currentGoogleUrlSnapshot || dbProduct.GoogleGid != currentGidSnapshot)
                            {
                                dbProduct.FoundOnGoogle = true;
                                dbProduct.GoogleUrl = currentGoogleUrlSnapshot;
                                dbProduct.GoogleGid = currentGidSnapshot;
                                changedInDb = true;
                            }

                        }
                        else if (currentStatusSnapshot == ProductStatus.NotFound)
                        {

                            if (dbProduct.FoundOnGoogle != false || dbProduct.GoogleUrl != null || dbProduct.GoogleGid != null)
                            {
                                dbProduct.FoundOnGoogle = false;
                                dbProduct.GoogleUrl = null;
                                dbProduct.GoogleGid = null;
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

    private async Task ProcessSingleProductAsync(
    ProductProcessingState productState,
    GoogleScraper scraper,
    int storeId,
    ConcurrentDictionary<int, ProductProcessingState> masterList,
    CancellationTokenSource captchaCts,
    int maxCidsToSearch,
    SearchTermSource termSource,
    string namePrefix,
    bool allowManualCaptchaSolving,
    bool appendProducerCode,
    int udmValue = 3)
    {
        if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving)
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
            return;
        }

        string searchTermBase;
        #region Wyznaczanie searchTermBase

        switch (termSource)
        {
            case SearchTermSource.ProductUrl:
                searchTermBase = productState.OriginalUrl;

                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WARN] URL pusty. Fallback do nazwy.");
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.Ean:
                searchTermBase = productState.Ean;
                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.ProducerCode:
                searchTermBase = productState.ProducerCode;
                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.ProductName:
            default:
                searchTermBase = productState.ProductNameInStoreForGoogle;

                if (appendProducerCode && !string.IsNullOrWhiteSpace(productState.ProducerCode))
                {
                    searchTermBase = $"{searchTermBase} {productState.ProducerCode}";
                }
                break;
        }

        if (termSource != SearchTermSource.ProductUrl &&
            !string.IsNullOrWhiteSpace(namePrefix) &&
            !string.IsNullOrWhiteSpace(searchTermBase))
        {
            searchTermBase = $"{namePrefix} {searchTermBase}";
        }

        if (string.IsNullOrWhiteSpace(searchTermBase))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BŁĄD: Brak terminu wyszukiwania dla ID: {productState.ProductId}.");
            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
            return;
        }
        #endregion

        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Przetwarzam produkt: {productState.ProductNameInStoreForGoogle} (ID: {productState.ProductId}), Szukam: '{searchTermBase}'");

        try
        {
        RestartProductProcessing:

            bool identifierSearchCompleted = false;
            ScraperResult<List<GoogleProductIdentifier>> identifierResult = null;

            while (!identifierSearchCompleted)
            {
                if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving)
                {
                    lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                    return;
                }

                identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermBase, maxCidsToSearch, udmValue);

                if (identifierResult.CaptchaEncountered)
                {
                    if (allowManualCaptchaSolving)
                    {
                        bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                        if (solved)
                        {
                            continue;
                        }
                        else
                        {
                            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
                            return;
                        }
                    }
                    else
                    {
                        if (!captchaCts.IsCancellationRequested) captchaCts.Cancel();
                        lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                        return;
                    }
                }

                identifierSearchCompleted = true;
            }

            if (!identifierResult.IsSuccess || !identifierResult.Data.Any())
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Nie znaleziono identyfikatorów/błąd dla '{searchTermBase}' (ID {productState.ProductId}). Msg: {identifierResult.ErrorMessage}");
                lock (productState) { productState.UpdateStatus(ProductStatus.NotFound); }
            }
            else
            {
                List<GoogleProductIdentifier> initialIdentifiers = identifierResult.Data;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Znaleziono {initialIdentifiers.Count} identyfikatorów dla ID {productState.ProductId}. Sprawdzam oferty.");

                Dictionary<string, ProductProcessingState> localEligibleProductsMap;
                lock (_lockMasterListInit)
                {
                    localEligibleProductsMap = masterList.Values
                        .Where(p => (p.Status != ProductStatus.Found) && !string.IsNullOrEmpty(p.CleanedUrl))
                        .GroupBy(p => p.CleanedUrl).ToDictionary(g => g.Key, g => g.First());
                }

                bool initiatingProductDirectlyMatchedInThisTask = false;
                lock (productState) { if (productState.Status == ProductStatus.Found) initiatingProductDirectlyMatchedInThisTask = true; }

                foreach (var identifier in initialIdentifiers)
                {
                    if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving) break;

                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}: Przetwarzam CID: {identifier.Cid}, GID: {identifier.Gid}");

                    ScraperResult<List<string>> offersResult = await scraper.FindStoreUrlsFromApiAsync(identifier.Cid, identifier.Gid);

                    if (offersResult.CaptchaEncountered)
                    {
                        if (allowManualCaptchaSolving)
                        {
                            bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                            if (solved)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] CAPTCHA rozwiązana podczas sprawdzania ofert. Restartuję przetwarzanie dla produktu ID: {productState.ProductId}");
                                goto RestartProductProcessing;
                            }
                            else
                            {
                                lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
                                return;
                            }
                        }
                        else
                        {
                            if (!captchaCts.IsCancellationRequested) captchaCts.Cancel();
                            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                            return;
                        }
                    }

                    if (offersResult.IsSuccess && offersResult.Data.Any())
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}, CID {identifier.Cid}: Znaleziono łącznie {offersResult.Data.Count} unikalnych ofert.");

                        // --- [LOGOWANIE ANALIZY PULI URL] ---
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[DEBUG-ANALIZA] Rozpoczynam dopasowywanie {offersResult.Data.Count} URLi do puli oczekujących produktów...");
                        // ------------------------------------

                        foreach (var cleanedOfferUrl in offersResult.Data)
                        {
                            // Logujemy co sprawdzamy
                            // Console.WriteLine($"[DEBUG-ANALIZA] Sprawdzam URL: {cleanedOfferUrl}"); 

                            if (localEligibleProductsMap.TryGetValue(cleanedOfferUrl, out var matchedState))
                            {
                                // --- [LOGOWANIE TRAFIENIA] ---
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[DEBUG-ANALIZA] !!! TRAFIENIE !!! URL pasuje do produktu ID: {matchedState.ProductId}");
                                Console.ResetColor();
                                // -----------------------------

                                lock (matchedState)
                                {
                                    string googleProductPageUrl = $"https://www.google.com/shopping/product/{identifier.Cid}";
                                    matchedState.UpdateStatus(ProductStatus.Found, googleProductPageUrl, identifier.Cid, identifier.Gid);

                                    lock (_consoleLock)
                                    {
                                        Console.Write($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ✓ ");
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.Write("DOPASOWANO");
                                        Console.ResetColor();
                                        Console.WriteLine($"! {cleanedOfferUrl} → ID {matchedState.ProductId}. CID: {identifier.Cid}, GID: {identifier.Gid}");
                                    }

                                    if (matchedState.ProductId == productState.ProductId)
                                    {
                                        initiatingProductDirectlyMatchedInThisTask = true;
                                    }
                                }
                            }
                            else
                            {
                                // Opcjonalnie: Logowanie braku trafienia (może generować dużo spamu)
                                // Console.WriteLine($"[DEBUG-ANALIZA] Brak dopasowania dla: {cleanedOfferUrl}");
                            }
                        }

                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[DEBUG-ANALIZA] Zakończono analizę dla tego zestawu ofert.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}, CID {identifier.Cid}: Brak ofert lub błąd. Msg: {offersResult.ErrorMessage}");
                    }

                    if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(200, 400)), CancellationToken.None);
                }

                if (!initiatingProductDirectlyMatchedInThisTask)
                {
                    lock (productState)
                    {
                        if (productState.Status == ProductStatus.Processing || productState.Status == ProductStatus.Pending)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ✗ Produkt inicjujący ID {productState.ProductId} NIE znaleziony. Status: NotFound.");
                            productState.UpdateStatus(ProductStatus.NotFound);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Przetwarzanie ID {productState.ProductId} anulowane.");
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] BŁĄD OGÓLNY (ProcessSingle) dla ID {productState.ProductId}: {ex.GetType().Name} - {ex.Message}");
            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
        }
        finally
        {
            lock (productState)
            {
                if (productState.Status == ProductStatus.Processing)
                {
                    productState.UpdateStatus(ProductStatus.Pending);
                }
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] --- Koniec ProcessSingle dla ID {productState.ProductId}. Finalny Status: {productState.Status} ---");
        }
    }

    private async Task<bool> HandleManualCaptchaAsync(GoogleScraper scraper, TimeSpan timeout)
    {
        var Cts = new CancellationTokenSource(timeout);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Wątek wstrzymany. Oczekuję na ręczne rozwiązanie CAPTCHA (max {timeout.TotalMinutes} min)...");

        while (!Cts.Token.IsCancellationRequested)
        {

            if (scraper.CurrentPage != null && !scraper.CurrentPage.Url.Contains("/sorry/") && !scraper.CurrentPage.Url.Contains("/captcha"))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Wygląda na to, że CAPTCHA została rozwiązana. Wznawiam pracę wątku.");
                return true;
            }

            try
            {

                await Task.Delay(TimeSpan.FromSeconds(10), Cts.Token);
            }
            catch (OperationCanceledException)
            {

                break;
            }
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Minął czas na rozwiązanie CAPTCHA. Wątek nie będzie kontynuowany.");
        return false;
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
    public async Task<IActionResult> ValidateGoogleUrls(int storeId)
    {

        var products = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl))
            .ToListAsync();

        foreach (var product in products)
        {

            if (!product.GoogleUrl.Contains("shopping/product"))
            {

                product.FoundOnGoogle = false;
                product.GoogleUrl = null;

                _context.Products.Update(product);
            }
        }

        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId });
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

        var products = await _context.Products
            .Include(p => p.GoogleCatalogs)
            .Where(p => p.StoreId == storeId && p.OnGoogle)
            .ToListAsync();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var jsonProducts = products.Select(p => {
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

                    GeneratedGoogleUrl = generatedUrl,

                    GoogleCatalogs = p.GoogleCatalogs?.Select(gc => new {
                        gc.GoogleCid,
                        gc.GoogleGid,
                        gc.GoogleHid,
                        gc.IsExtendedOfferByHid

                    }).ToList()
                };
            }).ToList();

            return Json(jsonProducts);
        }

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
    public async Task<IActionResult> UpdatePendingProducts(int storeId)
    {

        var pendingProducts = await _context.Products
            .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.GoogleUrl) && (p.FoundOnGoogle == null || p.FoundOnGoogle == false))
            .ToListAsync();

        foreach (var product in pendingProducts)
        {

            product.FoundOnGoogle = true;
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok();
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
    public async Task<IActionResult> ResetIncorrectGoogleStatuses(int storeId)
    {

        var productsToReset = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl) && string.IsNullOrEmpty(p.Url))
            .ToListAsync();

        foreach (var product in productsToReset)
        {

            product.FoundOnGoogle = null;
            product.GoogleUrl = null;
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok();
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
    public async Task<IActionResult> StartScrapingForAdditionalCatalogs(
    int storeId,
    List<int> productIds,
    int numberOfConcurrentScrapers = 5,
    int maxCidsToProcess = 8,
    string productNamePrefix = null,
    bool allowManualCaptchaSolving = false)
    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już aktywny.");
        }
        _isScrapingActive = true;

        string finalMessage = $"Proces Multi-Catalog dla sklepu {storeId} zainicjowany.";

        lock (_lockTimer)
        {
            if (_batchSaveTimer != null)
            {
                _batchSaveTimer.Dispose();
                _batchSaveTimer = null;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Multi-Catalog] Zatrzymano globalny timer zapisu.");
            }
        }

        _currentGlobalScrapingOperationCts = new CancellationTokenSource();
        _currentCaptchaGlobalCts = new CancellationTokenSource();

        var processedIds = new ConcurrentDictionary<int, byte>();

        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Multi-Catalog] START. Tryb READ-ONLY dla głównego produktu.");

            var scraperInstances = new List<GoogleScraper>();
            var initTasks = new List<Task>();
            for (int i = 0; i < numberOfConcurrentScrapers; i++)
            {
                var sc = new GoogleScraper();
                scraperInstances.Add(sc);
                initTasks.Add(sc.InitializeBrowserAsync());
            }
            await Task.WhenAll(initTasks);
            var availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstances);

            InitializeMasterProductListForMultiCatalog(storeId, productIds);

            var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
            var activeScrapingTasks = new List<Task>();

            while (!_currentGlobalScrapingOperationCts.IsCancellationRequested)
            {

                await BatchUpdateMultiCatalogAsync(false, CancellationToken.None);

                List<int> pendingProductIds;
                lock (_lockMasterListInit)
                {

                    pendingProductIds = _masterProductStateList
                        .Where(kvp => kvp.Value.ProcessingByTaskId == null && !processedIds.ContainsKey(kvp.Key))
                        .Select(kvp => kvp.Key).ToList();
                }

                if (!pendingProductIds.Any())
                {
                    if (activeScrapingTasks.Any(t => !t.IsCompleted))
                    {
                        await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray());
                        activeScrapingTasks.RemoveAll(t => t.IsCompleted);
                        continue;
                    }
                    else
                    {
                        break;

                    }
                }

                await semaphore.WaitAsync(_currentGlobalScrapingOperationCts.Token);
                if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { semaphore.Release(); break; }

                int selectedProductId;
                ProductProcessingState productStateToProcess;

                lock (_lockMasterListInit)
                {

                    var currentPending = _masterProductStateList
                        .Where(kvp => kvp.Value.ProcessingByTaskId == null && !processedIds.ContainsKey(kvp.Key))
                        .Select(kvp => kvp.Key).ToList();

                    if (!currentPending.Any()) { semaphore.Release(); continue; }

                    selectedProductId = currentPending[_random.Next(currentPending.Count)];
                    productStateToProcess = _masterProductStateList[selectedProductId];
                }

                if (availableScrapersPool.TryTake(out var assignedScraper))
                {
                    lock (productStateToProcess)
                    {
                        productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId;
                    }

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessSingleProduct_MultiCatalogMatchAsync(
                                productStateToProcess,
                                assignedScraper,
                                _currentGlobalScrapingOperationCts,
                                maxCidsToProcess,
                                productNamePrefix,
                                allowManualCaptchaSolving);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Multi-Catalog] BŁĄD ID {productStateToProcess.ProductId}: {ex.Message}");
                        }
                        finally
                        {

                            processedIds.TryAdd(productStateToProcess.ProductId, 1);

                            lock (productStateToProcess) { productStateToProcess.ProcessingByTaskId = null; }

                            availableScrapersPool.Add(assignedScraper);
                            semaphore.Release();
                        }
                    });
                    activeScrapingTasks.Add(task);
                }
                else
                {
                    semaphore.Release();
                    await Task.Delay(100);
                }
            }

            await Task.WhenAll(activeScrapingTasks.ToArray());
            finalMessage = "Proces Multi-Catalog zakończony pomyślnie.";
        }
        catch (OperationCanceledException) { finalMessage = "Zatrzymano scrapowanie."; }
        catch (Exception ex) { finalMessage = $"Błąd krytyczny: {ex.Message}"; }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Multi-Catalog] Wykonywanie zapisu końcowego...");

            await BatchUpdateMultiCatalogAsync(true, CancellationToken.None);

            lock (_lockTimer) { _batchSaveTimer?.Dispose(); _batchSaveTimer = null; }
            _isScrapingActive = false;
            _currentGlobalScrapingOperationCts?.Dispose();
            _currentCaptchaGlobalCts?.Dispose();

            _masterProductStateList.Clear();
        }

        return Ok(finalMessage);
    }

    private void InitializeMasterProductListForMultiCatalog(int storeId, List<int> productIds)
    {
        lock (_lockMasterListInit)
        {
            _masterProductStateList.Clear();
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                var query = context.Products
                    .Include(p => p.GoogleCatalogs)
                    .AsNoTracking()
                    .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.ProducerCode));

                if (productIds != null && productIds.Any())
                    query = query.Where(p => productIds.Contains(p.ProductId));

                var products = query.ToList();
                var tempScraper = new GoogleScraper();

                foreach (var p in products)
                {
                    var state = new ProductProcessingState(p, tempScraper.CleanUrlParameters);

                    if (!string.IsNullOrEmpty(p.GoogleGid)) state.KnownCids.Add(p.GoogleGid);

                    foreach (var existing in p.GoogleCatalogs)
                    {
                        if (!string.IsNullOrEmpty(existing.GoogleCid)) state.KnownCids.Add(existing.GoogleCid);
                        if (!string.IsNullOrEmpty(existing.GoogleHid)) state.KnownCids.Add(existing.GoogleHid);
                    }

                    state.IsDirty = false;
                    _masterProductStateList.TryAdd(p.ProductId, state);
                }
            }
        }
    }

    private async Task ProcessSingleProduct_MultiCatalogMatchAsync(
      ProductProcessingState productState,
      GoogleScraper scraper,
      CancellationTokenSource cts,
      int maxCidsToProcess,
      string namePrefix,
      bool allowManualCaptchaSolving)
    {
    RestartSearch:

        if (cts.IsCancellationRequested && !allowManualCaptchaSolving) return;

        string targetProducerCode = productState.ProducerCode?.Replace(" ", "").Trim();

        string searchTerm = productState.ProductNameInStoreForGoogle;
        if (!string.IsNullOrWhiteSpace(namePrefix)) searchTerm = $"{namePrefix} {searchTerm}";
        if (!string.IsNullOrWhiteSpace(productState.ProducerCode)) searchTerm = $"{searchTerm} {productState.ProducerCode}";

        lock (_consoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[Multi-Catalog] >>> START ID: {productState.ProductId}");
            Console.WriteLine($"   > Nazwa: {productState.ProductNameInStoreForGoogle}");
            Console.WriteLine($"   > Kod Producenta (CEL): [{targetProducerCode}]");
            Console.WriteLine($"   > Zapytanie do Google: '{searchTerm}'");
            Console.ResetColor();
        }

        if (string.IsNullOrEmpty(targetProducerCode))
        {
            Console.WriteLine($"   > [SKIP] Brak kodu producenta dla ID {productState.ProductId}.");
            return;
        }

        var identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTerm, maxItemsToExtract: maxCidsToProcess);

        if (identifierResult.CaptchaEncountered)
        {
            if (allowManualCaptchaSolving)
            {
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"!!! CAPTCHA WYKRYTA (Lista wyników) - Czekam na rozwiązanie... !!!");
                Console.ResetColor();

                bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                if (solved) goto RestartSearch;
                else return;
            }
            else
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
                return;
            }
        }

        if (!identifierResult.IsSuccess || !identifierResult.Data.Any())
        {
            Console.WriteLine($"   > [INFO] Brak wyników wyszukiwania dla ID {productState.ProductId}.");
            return;
        }

        Console.WriteLine($"   > Znaleziono {identifierResult.Data.Count} potencjalnych katalogów. Rozpoczynam weryfikację...");

        int verifiedCount = 0;

        foreach (var identifier in identifierResult.Data)
        {
            if (cts.IsCancellationRequested && !allowManualCaptchaSolving) break;

            string effectiveId = !string.IsNullOrEmpty(identifier.Cid) ? identifier.Cid : identifier.Hid;

            bool isKnown = false;
            lock (productState.KnownCids)
            {
                if (!string.IsNullOrEmpty(effectiveId) && productState.KnownCids.Contains(effectiveId))
                    isKnown = true;
            }

            if (isKnown)
            {
                Console.WriteLine($"    > [POMINIĘTO] Katalog {(!string.IsNullOrEmpty(identifier.Cid) ? "CID" : "HID")}: {effectiveId} jest już przypisany.");
                continue;
            }

            var detailsResult = await scraper.GetProductDetailsFromApiAsync(
                identifier.Cid,
                identifier.Gid,
                identifier.Hid,
                targetCode: targetProducerCode
            );

            if (detailsResult.CaptchaEncountered)
            {
                if (allowManualCaptchaSolving)
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"!!! CAPTCHA WYKRYTA (API Detale) - Czekam na rozwiązanie... !!!");
                    Console.ResetColor();

                    bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                    if (solved) goto RestartSearch;
                    else return;
                }
                else
                {
                    if (!cts.IsCancellationRequested) cts.Cancel();
                    break;
                }
            }

            if (!detailsResult.IsSuccess)
            {

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"      [BŁĄD API] Nie udało się pobrać danych dla {effectiveId}.");
                Console.ResetColor();
                continue;
            }

            var details = detailsResult.Data.Details;
            string rawData = detailsResult.Data.RawResponse;

            bool matchFound = rawData.Contains(targetProducerCode, StringComparison.OrdinalIgnoreCase) ||
                              rawData.Replace(" ", "").Contains(targetProducerCode.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

            lock (_consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"      [Analiza {(!string.IsNullOrEmpty(identifier.Cid) ? "CID: " + identifier.Cid : "HID: " + identifier.Hid)}]");
                Console.WriteLine($"      -> Produkt: {details.MainTitle}");
                Console.ResetColor();
            }

            if (matchFound)
            {

                string googleUrl;
                if (!string.IsNullOrEmpty(identifier.Cid))
                {
                    googleUrl = $"https://www.google.com/shopping/product/{identifier.Cid}";
                }
                else
                {
                    string encodedName = System.Net.WebUtility.UrlEncode(productState.ProductNameInStoreForGoogle);
                    googleUrl = $"https://www.google.com/search?q={encodedName}&udm=28#oshopproduct=gid:{identifier.Gid},hid:{identifier.Hid},pvt:hg,pvo:3&oshop=apv";
                }

                productState.AddCatalog(identifier.Cid, identifier.Gid, identifier.Hid);
                verifiedCount++;

                lock (_consoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"      [SUKCES] Kod {targetProducerCode} znaleziony w treści API!");
                    Console.WriteLine($"      >>> Dodano powiązanie do bazy.");
                    Console.ResetColor();
                }
            }
            else
            {

                lock (_consoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"      [INFO] Produkt poprawny technicznie, ale brak kodu {targetProducerCode} w treści.");
                    Console.ResetColor();
                }
            }

            await Task.Delay(250);
        }

        lock (_consoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Multi-Catalog] Zakończono dla ID {productState.ProductId}. Dodano {verifiedCount} nowych powiązań.");
            Console.ResetColor();
        }
    }

    private async Task BatchUpdateMultiCatalogAsync(bool isFinalSave, CancellationToken cancellationToken)
    {
        List<ProductProcessingState> dirtyProducts;
        lock (_lockMasterListInit)
        {
            dirtyProducts = _masterProductStateList.Values.Where(p => !p.NewCatalogsFound.IsEmpty).ToList();
        }

        if (!dirtyProducts.Any()) return;

        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            int addedCount = 0;

            foreach (var state in dirtyProducts)
            {
                ProductGoogleCatalog[] catalogsToSave;
                lock (state.KnownCids)
                {
                    catalogsToSave = state.NewCatalogsFound.ToArray();
                    state.NewCatalogsFound = new ConcurrentBag<ProductGoogleCatalog>();
                }

                foreach (var cat in catalogsToSave)
                {

                    bool exists = await context.Set<ProductGoogleCatalog>().AnyAsync(x =>
                        x.ProductId == cat.ProductId &&
                        ((cat.GoogleCid != null && x.GoogleCid == cat.GoogleCid) ||
                         (cat.GoogleHid != null && x.GoogleHid == cat.GoogleHid)), cancellationToken);

                    if (!exists)
                    {
                        cat.Id = 0;
                        cat.Product = null;
                        await context.Set<ProductGoogleCatalog>().AddAsync(cat, cancellationToken);
                        addedCount++;

                        Console.ForegroundColor = cat.IsExtendedOfferByHid ? ConsoleColor.Yellow : ConsoleColor.Green;
                        Console.WriteLine($"   + [DB] Dodano: {cat.ProductId} | Typ: {(cat.IsExtendedOfferByHid ? "HID (Oferta)" : "CID (Katalog)")}");
                        Console.ResetColor();
                    }
                }
            }

            if (addedCount > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
                Console.BackgroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DB-SUCCESS] Zapisano {addedCount} katalogów/ofert.");
                Console.ResetColor();
            }
        }
    }



    #region ============== ZEWNĘTRZNE SCRAPERY API ==============

    // Klucz API dla zewnętrznych scraperów
    private const string EXTERNAL_SCRAPER_API_KEY = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

    // Zarejestrowane zewnętrzne scrapery
    private static readonly ConcurrentDictionary<string, ExternalScraperInfo> _registeredScrapers = new();

    // Kolejka zadań dla zewnętrznych scraperów
    private static readonly ConcurrentQueue<ExternalScraperTask> _externalTaskQueue = new();

    // Wyniki od zewnętrznych scraperów
    private static readonly ConcurrentDictionary<string, ExternalScraperResult> _externalResults = new();

    // Ustawienia dla zewnętrznych scraperów
    private static ExternalScraperSettings _externalScraperSettings = new();

    // Lock dla ustawień
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

        // Reset sieci
        [JsonPropertyName("networkResetMethod")]
        public string NetworkResetMethod { get; set; } = "mullvad"; // "mullvad" lub "modem_lte"

        [JsonPropertyName("autoNetworkResetOnCaptcha")]
        public bool AutoNetworkResetOnCaptcha { get; set; } = true;

        [JsonPropertyName("captchaCountBeforeNetworkReset")]
        public int CaptchaCountBeforeNetworkReset { get; set; } = 5;

        // Mullvad
        [JsonPropertyName("mullvadPath")]
        public string MullvadPath { get; set; } = @"C:\Program Files\Mullvad VPN\resources\mullvad.exe";

        [JsonPropertyName("mullvadCountryCode")]
        public string MullvadCountryCode { get; set; } = "pl";

        [JsonPropertyName("mullvadCityCode")]
        public string MullvadCityCode { get; set; } = "waw";

        // Modem LTE
        [JsonPropertyName("modemUrl")]
        public string ModemUrl { get; set; } = "http://192.168.1.1";

        [JsonPropertyName("modemPassword")]
        public string ModemPassword { get; set; } = "QqD9wWUF";

        [JsonPropertyName("modemRestartWaitSeconds")]
        public int ModemRestartWaitSeconds { get; set; } = 50;

        // Tryb scrapowania
        [JsonPropertyName("scrapingMode")]
        public string ScrapingMode { get; set; } = "Standard"; // Standard, FirstMatch, Intermediate, MultiCatalog

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

        // Dane produktu
        [JsonPropertyName("productId")]
        public int ProductId { get; set; }

        [JsonPropertyName("productName")]
        public string ProductName { get; set; }

        [JsonPropertyName("cleanedUrl")]
        public string CleanedUrl { get; set; }

        [JsonPropertyName("originalUrl")]
        public string OriginalUrl { get; set; }

        [JsonPropertyName("ean")]
        public string Ean { get; set; }

        [JsonPropertyName("producerCode")]
        public string ProducerCode { get; set; }

        // Tryb i ustawienia
        [JsonPropertyName("mode")]
        public string Mode { get; set; } // "Standard", "FirstMatch", "Intermediate", "MultiCatalog"

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

        // Kontekst dla pełnego przetwarzania
        [JsonPropertyName("storeId")]
        public int StoreId { get; set; }

        [JsonPropertyName("eligibleProductsMap")]
        public Dictionary<string, int> EligibleProductsMap { get; set; } // CleanedUrl -> ProductId
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

        // Wyniki wyszukiwania identyfikatorów
        [JsonPropertyName("identifiers")]
        public List<GoogleProductIdentifierDto> Identifiers { get; set; }

        // Wyniki wyszukiwania URL-i sklepów
        [JsonPropertyName("storeUrls")]
        public List<string> StoreUrls { get; set; }

        // Wyniki szczegółów produktu
        [JsonPropertyName("productDetails")]
        public GoogleProductDetailsDto ProductDetails { get; set; }

        [JsonPropertyName("rawResponse")]
        public string RawResponse { get; set; }

        // Wyniki pełnego przetwarzania
        [JsonPropertyName("finalStatus")]
        public string FinalStatus { get; set; } // "Found", "NotFound", "Error", "CaptchaHalt"

        [JsonPropertyName("foundGoogleUrl")]
        public string FoundGoogleUrl { get; set; }

        [JsonPropertyName("foundCid")]
        public string FoundCid { get; set; }

        [JsonPropertyName("foundGid")]
        public string FoundGid { get; set; }

        // Dopasowane produkty (dla trybu Intermediate)
        [JsonPropertyName("matchedProducts")]
        public List<MatchedProductDto> MatchedProducts { get; set; }
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
    }

    public class NukeReportDto
    {
        [JsonPropertyName("scraperName")]
        public string ScraperName { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } // "started", "completed"

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


    #endregion

    #region ============== ENDPOINTY API DLA ZEWNĘTRZNYCH SCRAPERÓW ==============

    /// <summary>
    /// Walidacja klucza API
    /// </summary>
    private bool ValidateApiKey(string apiKey)
    {
        return !string.IsNullOrEmpty(apiKey) && apiKey == EXTERNAL_SCRAPER_API_KEY;
    }

    /// <summary>
    /// Pobiera klucz API z nagłówka
    /// </summary>
    private string GetApiKeyFromHeader()
    {
        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
            return apiKey.ToString();
        return null;
    }

    /// <summary>
    /// Rejestracja zewnętrznego scrapera
    /// </summary>
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
        // Sprawdź czy to Python (Klucz API) LUB czy to Admin (Zalogowany w przeglądarce)
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
        // Sprawdzamy uprawnienia (Python LUB Admin)
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

    /// <summary>
    /// Pobieranie zadania dla zewnętrznego scrapera - POPRAWIONE
    /// </summary>
    //[HttpGet]
    //[Route("api/external-scraper/get-task")]
    //[AllowAnonymous]
    //public IActionResult GetExternalScraperTask([FromQuery] string scraperName = null)
    //{
    //    var apiKey = GetApiKeyFromHeader();
    //    if (!ValidateApiKey(apiKey))
    //        return Unauthorized(new { error = "Invalid API key" });

    //    // Odśwież heartbeat jeśli podano nazwę
    //    if (!string.IsNullOrEmpty(scraperName) && _registeredScrapers.TryGetValue(scraperName, out var info))
    //    {
    //        info.LastHeartbeat = DateTime.UtcNow;
    //    }

    //    // Pobierz zadanie z kolejki
    //    if (_externalTaskQueue.TryDequeue(out var task))
    //    {
    //        task.AssignedTo = scraperName ?? "unknown";
    //        task.AssignedAt = DateTime.UtcNow;

    //        Console.ForegroundColor = ConsoleColor.Cyan;
    //        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Zadanie {task.TaskId.Substring(0, Math.Min(20, task.TaskId.Length))}... przydzielone do '{scraperName}'");
    //        Console.ResetColor();

    //        // Zwróć zadanie z odpowiednim formatem JSON
    //        return Ok(new
    //        {
    //            hasTask = true,
    //            task = new
    //            {
    //                taskId = task.TaskId,
    //                productId = task.ProductId,
    //                productName = task.ProductName,
    //                searchTerm = task.SearchTerm,
    //                cleanedUrl = task.CleanedUrl,
    //                originalUrl = task.OriginalUrl,
    //                ean = task.Ean,
    //                producerCode = task.ProducerCode,
    //                mode = task.Mode,
    //                maxItemsToExtract = task.MaxItemsToExtract,
    //                udmValue = task.UdmValue,
    //                storeId = task.StoreId,
    //                googleCid = task.GoogleCid,
    //                googleGid = task.GoogleGid,
    //                googleHid = task.GoogleHid,
    //                targetCode = task.TargetCode,
    //                eligibleProductsMap = task.EligibleProductsMap
    //            }
    //        });
    //    }

    //    // Brak zadań
    //    return Ok(new
    //    {
    //        hasTask = false,
    //        message = "No tasks available"
    //    });
    //}


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

        // ZMIANA: Pętla upewniająca się, że wydajemy tylko zadania wciąż wymagające przetworzenia
        while (_externalTaskQueue.TryDequeue(out var task))
        {
            // Sprawdź w Master List, czy ten produkt nie został już znaleziony przy okazji (z puli)
            if (_masterProductStateList.TryGetValue(task.ProductId, out var currentState))
            {
                if (currentState.Status == ProductStatus.Found || currentState.Status == ProductStatus.NotFound || currentState.Status == ProductStatus.Error)
                {
                    // Produkt został już przetworzony. Wyrzucamy to zadanie do śmieci i szukamy następnego.
                    continue;
                }

                // Oznacz produkt jako "W przetwarzaniu", żeby inne instancje go nie dublowały
                lock (currentState)
                {
                    currentState.Status = ProductStatus.Processing;
                }
            }

            task.AssignedTo = scraperName ?? "unknown";
            task.AssignedAt = DateTime.UtcNow;

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
                    eligibleProductsMap = task.EligibleProductsMap
                }
            });
        }

        return Ok(new
        {
            hasTask = false,
            message = "No tasks available"
        });
    }


    //[HttpGet]
    //[Route("api/external-scraper/get-task-batch")]
    //[AllowAnonymous]
    //public IActionResult GetExternalScraperTaskBatch([FromQuery] string scraperName, [FromQuery] int maxTasks = 10)
    //{
    //    var apiKey = GetApiKeyFromHeader();
    //    if (!ValidateApiKey(apiKey))
    //        return Unauthorized(new { error = "Invalid API key" });

    //    if (string.IsNullOrEmpty(scraperName))
    //        return BadRequest(new { error = "scraperName is required" });

    //    // Odśwież heartbeat
    //    if (_registeredScrapers.TryGetValue(scraperName, out var info))
    //    {
    //        info.LastHeartbeat = DateTime.UtcNow;
    //    }

    //    var tasks = new List<ExternalScraperTask>();

    //    for (int i = 0; i < maxTasks; i++)
    //    {
    //        if (_externalTaskQueue.TryDequeue(out var task))
    //        {
    //            task.AssignedTo = scraperName;
    //            task.AssignedAt = DateTime.UtcNow;
    //            tasks.Add(task);
    //        }
    //        else
    //        {
    //            break;
    //        }
    //    }

    //    if (tasks.Any())
    //    {
    //        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] {tasks.Count} zadań przydzielonych do '{scraperName}'");
    //    }

    //    return Ok(new
    //    {
    //        hasTasks = tasks.Any(),
    //        tasksCount = tasks.Count,
    //        tasks = tasks,
    //        queueRemaining = _externalTaskQueue.Count
    //    });
    //}

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

        // ZMIANA: Pobieramy zadania póki nie osiągniemy maxTasks LUB nie wyczerpiemy kolejki
        while (tasks.Count < maxTasks && _externalTaskQueue.TryDequeue(out var task))
        {
            if (_masterProductStateList.TryGetValue(task.ProductId, out var currentState))
            {
                // Jeśli produkt ma już finalny status (np. Found z puli), to go pomijamy!
                if (currentState.Status == ProductStatus.Found || currentState.Status == ProductStatus.NotFound || currentState.Status == ProductStatus.Error)
                {
                    continue;
                }

                lock (currentState)
                {
                    currentState.Status = ProductStatus.Processing;
                }
            }

            task.AssignedTo = scraperName;
            task.AssignedAt = DateTime.UtcNow;
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

        // Zapisz wynik
        _externalResults[result.TaskId] = result;

        // Aktualizuj statystyki scrapera
        if (!string.IsNullOrEmpty(result.ScraperName) && _registeredScrapers.TryGetValue(result.ScraperName, out var info))
        {
            info.LastHeartbeat = DateTime.UtcNow;
            if (result.IsSuccess)
                info.TasksCompleted++;
            else
                info.TasksFailed++;
        }

        // Przetworz wynik - aktualizuj bazę danych
        await ProcessExternalResultAsync(result);

        // Logowanie z kolorami
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
         int udm = 3)
    {
        // 1. Sprawdź czy mamy w ogóle podłączone scrapery
        int activeScrapers = _registeredScrapers.Values.Count(s => s.IsActive);
        if (activeScrapers == 0)
        {
            return Json(new { success = false, message = "Błąd: Brak aktywnych scraperów zewnętrznych (Python). Uruchom skrypt Pythona." });
        }

        // ==============================================================================
        // KROK 2: AKTYWACJA PROCESU I TIMERA ZAPISU
        // ==============================================================================
        _isScrapingActive = true; // Oznaczamy proces jako aktywny globalnie

        lock (_lockTimer)
        {
            if (_batchSaveTimer == null)
            {
                // Timer uruchamia się co 10 sekund i zrzuca zmiany z pamięci do bazy danych
                // Zmieniono z 30s na 10s dla szybszego podglądu wyników
                _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Timer do zapisu wsadowego został uruchomiony (interwał 10s).");
            }
        }

        // ==============================================================================
        // KROK 3: AKTUALIZACJA USTAWIEŃ W LOCIE
        // ==============================================================================
        lock (_settingsLock)
        {
            _externalScraperSettings.ScrapingMode = mode;
            _externalScraperSettings.MaxCidsToProcess = maxCids;
            _externalScraperSettings.AppendProducerCode = appendProducerCode;
            _externalScraperSettings.CompareOnlyCurrentProductCode = compareOnlyCode;
            _externalScraperSettings.ProductNamePrefix = prefix;
            _externalScraperSettings.SearchModeUdm = udm;
        }

        // ==============================================================================
        // KROK 4: INICJALIZACJA LISTY PRODUKTÓW (MASTER LIST)
        // ==============================================================================
        // Tryb Standard wymaga URL-a w bazie, inne tryby mogą szukać po nazwie/kodzie
        InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: mode == "Standard");

        var pendingProducts = new List<ProductProcessingState>();

        // Pobierz produkty, które mają status Pending z pamięci RAM
        lock (_lockMasterListInit)
        {
            pendingProducts = _masterProductStateList.Values
                .Where(p => p.Status == ProductStatus.Pending)
                .ToList();
        }

        if (!pendingProducts.Any())
        {
            // Jeśli nie ma co robić, zwracamy info, ale NIE wyłączamy timera od razu,
            // bo może trwać jeszcze zapis poprzedniej partii.
            return Json(new { success = true, message = "Wszystkie wybrane produkty są już przetworzone lub nie kwalifikują się." });
        }

        // ==============================================================================
        // KROK 5: KOLEJKOWANIE ZADAŃ (Z FILTREM DUPLIKATÓW)
        // ==============================================================================

        // Pobieramy ID produktów, które JUŻ są w kolejce, żeby ich nie dublować
        var existingTaskProductIds = _externalTaskQueue.Select(t => t.ProductId).ToHashSet();

        int addedCount = 0;

        // Budowanie mapy eligibleProductsMap (potrzebne tylko dla trybu Standard/Full)
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
            // ZMIANA: Jeśli produkt już jest w kolejce, pomiń go (nie dodawaj duplikatu)
            if (existingTaskProductIds.Contains(product.ProductId))
            {
                continue;
            }

            // Zbuduj searchTerm
            string searchTerm = product.ProductNameInStoreForGoogle;

            if (appendProducerCode && !string.IsNullOrEmpty(product.ProducerCode))
                searchTerm = $"{searchTerm} {product.ProducerCode}";

            if (!string.IsNullOrEmpty(prefix))
                searchTerm = $"{prefix} {searchTerm}";

            // Stwórz obiekt zadania
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
                TargetCode = product.ProducerCode // Ważne dla trybu Intermediate
            };

            _externalTaskQueue.Enqueue(task);
            existingTaskProductIds.Add(product.ProductId); // Dodaj do lokalnego seta, żeby nie dodać 2x w tej samej pętli
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

    /// <summary>
    /// Wysyłanie paczki wyników przez zewnętrzny scraper
    /// </summary>
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

    /// <summary>
    /// Raport o procedurze NUKE (reset sieci)
    /// </summary>
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
    [AllowAnonymous] // Musi być AllowAnonymous, żeby przyjąć Pythona, ale sprawdzamy też usera
    public IActionResult GetExternalScrapersStatus()
    {
        // Hybrydowa autoryzacja: Klucz API LUB Zalogowany Admin
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
            // Wyciągnij ProductId z TaskId (format: "product_123_guid")
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
                // Znajdź stan produktu w master liście
                if (_masterProductStateList.TryGetValue(productId.Value, out var productState))
                {
                    lock (productState)
                    {
                        switch (result.FinalStatus)
                        {
                            case "Found":
                                productState.UpdateStatus(ProductStatus.Found, result.FoundGoogleUrl, result.FoundCid, result.FoundGid);

                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"\n[EXT-API - BEZPOŚREDNIE TRAFIENIE]");
                                    Console.WriteLine($" ├─ ID Produktu : {productId.Value}");
                                    Console.WriteLine($" ├─ Nazwa       : {productState.ProductNameInStoreForGoogle}");
                                    Console.WriteLine($" ├─ URL z bazy  : {productState.CleanedUrl}");
                                    Console.WriteLine($" ├─ CID Google  : {result.FoundCid}");
                                    Console.WriteLine($" └─ URL Google  : {result.FoundGoogleUrl}");
                                    Console.ResetColor();
                                }
                                break;

                            case "NotFound":
                                productState.UpdateStatus(ProductStatus.NotFound);
                                // To zostawiamy szare/standardowe, żeby nie zaśmiecać konsoli
                                // Console.WriteLine($"[EXT-API] Produkt {productId} NIE ZNALEZIONY");
                                break;

                            case "CaptchaHalt":
                                productState.UpdateStatus(ProductStatus.CaptchaHalt);
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"[EXT-API] ⚠ Produkt {productId} ZATRZYMANY PRZEZ CAPTCHA");
                                    Console.ResetColor();
                                }
                                break;

                            case "Error":
                            default:
                                productState.UpdateStatus(ProductStatus.Error);
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"[EXT-API] ❌ Produkt {productId} BŁĄD: {result.ErrorMessage}");
                                    Console.ResetColor();
                                }
                                break;
                        }
                    }
                }
            }

            // ====================================================================
            // CROSS-MATCHING (Dopasowania rykoszetem z puli)
            // ====================================================================
            if (result.MatchedProducts != null && result.MatchedProducts.Any())
            {
                foreach (var matched in result.MatchedProducts)
                {
                    // ZABEZPIECZENIE: Pomijamy produkt główny, bo on został wylistowany wyżej na zielono
                    if (productId.HasValue && matched.ProductId == productId.Value)
                        continue;

                    if (_masterProductStateList.TryGetValue(matched.ProductId, out var matchedState))
                    {
                        lock (matchedState)
                        {
                            if (matchedState.Status != ProductStatus.Found)
                            {
                                matchedState.UpdateStatus(ProductStatus.Found, matched.GoogleUrl, matched.Cid, matched.Gid);

                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine($"\n[EXT-API - CROSS-MATCH (RYKOSZET Z PULI!)]");
                                    Console.WriteLine($" ├─ Wątek szukał : Głównego produktu ID {productId}");
                                    Console.WriteLine($" ├─ DOPASOWANO   : Produkt ID {matched.ProductId} z puli oczekujących!");
                                    Console.WriteLine($" ├─ Nazwa w bazie: {matchedState.ProductNameInStoreForGoogle}");
                                    Console.WriteLine($" ├─ URL z bazy   : {matchedState.CleanedUrl}");
                                    Console.WriteLine($" ├─ CID Google   : {matched.Cid}");
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

    public void EnqueueTasksForExternalScrapers(int storeId, List<ProductProcessingState> products, string mode)
    {
        lock (_settingsLock)
        {
            // 1. Logika mapowania dla trybu Standard (bez zmian)
            Dictionary<string, int> eligibleProductsMap = null;
            if (mode == "Standard" || mode == "full_process")
            {
                eligibleProductsMap = products
                    .Where(p => !string.IsNullOrEmpty(p.CleanedUrl))
                    .GroupBy(p => p.CleanedUrl, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ProductId, StringComparer.OrdinalIgnoreCase);
            }

            // 2. Filtrowanie i LOSOWANIE (Shuffle)
            // Używamy Random.Shared dla lepszej wydajności niż Guid.NewGuid() przy dużych listach
            var pendingProducts = products
                .Where(p => p.Status == ProductStatus.Pending)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            if (!pendingProducts.Any()) return;

            // DEBUG: Pokaż w konsoli pierwsze 3 ID, aby upewnić się, że są losowe
            var previewIds = string.Join(", ", pendingProducts.Take(3).Select(p => p.ProductId));
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Kolejność po losowaniu (start): {previewIds}...");

            int addedCount = 0;

            foreach (var product in pendingProducts)
            {
                // Budowanie searchTerm
                string searchTerm = product.ProductNameInStoreForGoogle;

                if (_externalScraperSettings.AppendProducerCode && !string.IsNullOrEmpty(product.ProducerCode))
                    searchTerm = $"{searchTerm} {product.ProducerCode}";

                if (!string.IsNullOrEmpty(_externalScraperSettings.ProductNamePrefix))
                    searchTerm = $"{_externalScraperSettings.ProductNamePrefix} {searchTerm}";

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
                    TargetCode = product.ProducerCode
                };

                _externalTaskQueue.Enqueue(task);
                addedCount++;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Dodano {addedCount} zadań do kolejki w LOSOWEJ kolejności. Tryb: {mode}");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Aktualny rozmiar kolejki: {_externalTaskQueue.Count}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Uruchamia scrapowanie z użyciem zewnętrznych scraperów
    /// </summary>
    [HttpPost]
    [Route("api/external-scraper/start-scraping")]
    public async Task<IActionResult> StartScrapingWithExternalScrapers(
        int storeId,
        List<int> productIds,
        string mode = "Standard")
    {
        if (!_registeredScrapers.Values.Any(s => s.IsActive))
            return BadRequest(new { error = "Brak aktywnych zewnętrznych scraperów. Uruchom scraper Python." });

        // Inicjalizuj listę produktów
        InitializeMasterProductListIfNeeded(storeId, productIds, false, requireUrl: mode == "Standard");

        var pendingProducts = _masterProductStateList.Values
            .Where(p => p.Status == ProductStatus.Pending)
            .ToList();

        if (!pendingProducts.Any())
            return Ok(new { message = "Brak produktów do przetworzenia" });

        // Dodaj zadania do kolejki
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
    public IActionResult ResetExternalQueue()
    {
        // 1. Wyczyść kolejkę zadań
        _externalTaskQueue.Clear();

        // 2. Zresetuj stan produktów w pamięci, które są "w trakcie" przetwarzania przez zewnętrzne
        // (czyli te, które nie są Found/NotFound/Error, a wiszą w pamięci)
        lock (_lockMasterListInit)
        {
            var stuckProducts = _masterProductStateList.Values
                .Where(p => p.Status == ProductStatus.Processing && p.ProcessingByTaskId == null) // ProcessingByTaskId jest null dla zewnętrznych (bo nie ma TaskId wątku C#)
                .ToList();

            foreach (var p in stuckProducts)
            {
                p.Status = ProductStatus.Pending; // Cofnij do Pending
            }

            // Opcjonalnie: wyczyść całą listę master, żeby załadować od nowa z bazy przy następnym starcie
            // _masterProductStateList.Clear(); 
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [EXT-API] Kolejka zadań została wyczyszczona ręcznie.");
        return Ok(new { message = "Kolejka i stan zresetowane." });
    }

    #endregion

}

