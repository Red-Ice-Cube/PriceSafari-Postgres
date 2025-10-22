using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;
using System.Collections.Concurrent;

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
        public ProductStatus Status { get => _status; set { if (_status != value) { _status = value; IsDirty = true; } } }
        public string GoogleUrl { get => _googleUrl; set { if (_googleUrl != value) { _googleUrl = value; IsDirty = true; } } }
        public string Cid { get => _cid; set { if (_cid != value) { _cid = value; IsDirty = true; } } }
        public string GoogleGid { get => _googleGid; set { if (_googleGid != value) { _googleGid = value; IsDirty = true; } } }

        public int? ProcessingByTaskId { get; set; }
        public bool IsDirty { get; set; }

        public ProductProcessingState(ProductClass product, Func<string, string> cleanUrlFunc)
        {
            ProductId = product.ProductId;
            OriginalUrl = product.Url;
            CleanedUrl = !string.IsNullOrEmpty(product.Url) ? cleanUrlFunc(product.Url) : string.Empty;
            ProductNameInStoreForGoogle = product.ProductNameInStoreForGoogle;
            Ean = product.Ean;
            ProducerCode = product.ProducerCode;
            if (product.FoundOnGoogle == true)
            {
                _status = ProductStatus.Found;
                _googleUrl = product.GoogleUrl;

                _googleGid = product.GoogleGid;
            }
            else if (product.FoundOnGoogle == false) { _status = ProductStatus.NotFound; }
            else { _status = ProductStatus.Pending; }
            IsDirty = false;
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
        int numberOfConcurrentScrapers = 5,
        int maxCidsToProcessPerProduct = 3,
        SearchTermSource searchTermSource = SearchTermSource.ProductName,
        string productNamePrefix = null,
        bool useFirstMatchLogic = false,
        bool ensureNameMatch = false,
         bool allowManualCaptchaSolving = false)
    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już globalnie aktywny.");
        }

        if (ensureNameMatch)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb Pośredni (z weryfikacją nazwy). Uruchamiam scraper...");

            return await StartScrapingForProducts_IntermediateMatchAsync(storeId, numberOfConcurrentScrapers, searchTermSource, maxCidsToProcessPerProduct, allowManualCaptchaSolving);
        }
        else if (useFirstMatchLogic)
        {

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb 'Pierwszy Trafiony'. Uruchamiam uproszczony scraper...");
            return await StartScrapingForProducts_FirstMatchAsync(storeId, numberOfConcurrentScrapers, searchTermSource, productNamePrefix);
        }
        else
        {

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wybrano tryb standardowy (dokładny). Uruchamiam pełny scraper...");
            return await StartScrapingForProducts_StandardAsync(storeId, numberOfConcurrentScrapers, maxCidsToProcessPerProduct, searchTermSource, productNamePrefix, allowManualCaptchaSolving);
        }
    }

    public async Task<IActionResult> StartScrapingForProducts_StandardAsync(
    int storeId,
    int numberOfConcurrentScrapers = 5,
    int maxCidsToProcessPerProduct = 3,
    SearchTermSource searchTermSource = SearchTermSource.ProductName,
    string productNamePrefix = null,
    bool allowManualCaptchaSolving = false)
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

                    InitializeMasterProductListIfNeeded(storeId, false);

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
                                    await ProcessSingleProductAsync(productStateToProcess, assignedScraper, storeId, _masterProductStateList, _currentCaptchaGlobalCts, maxCidsToProcessPerProduct, searchTermSource, productNamePrefix, allowManualCaptchaSolving);
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

                        InitializeMasterProductListIfNeeded(storeId, restartAttempt > 0);

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
                                            await ProcessSingleProductAsync(productStateToProcess, assignedScraper, storeId, _masterProductStateList, _currentCaptchaGlobalCts, maxCidsToProcessPerProduct, searchTermSource, productNamePrefix, allowManualCaptchaSolving);
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

    private async Task<IActionResult> StartScrapingForProducts_FirstMatchAsync(int storeId, int numberOfConcurrentScrapers, SearchTermSource searchTermSource, string productNamePrefix)
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

            InitializeMasterProductListIfNeeded(storeId, false, requireUrl: false);

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
                            await ProcessSingleProduct_FirstMatchAsync(productState, scraper, searchTermSource, productNamePrefix, _currentGlobalScrapingOperationCts);
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

    private async Task ProcessSingleProduct_FirstMatchAsync(ProductProcessingState productState, GoogleScraper scraper, SearchTermSource termSource, string namePrefix, CancellationTokenSource cts)
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
                if (!string.IsNullOrWhiteSpace(namePrefix))
                    searchTermBase = $"{namePrefix} {searchTermBase}";
                break;
        }

        if (string.IsNullOrWhiteSpace(searchTermBase))
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] Przetwarzam ID: {productState.ProductId}, Szukam: '{searchTermBase}'");

        var identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermBase, maxItemsToExtract: 1);

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
        int numberOfConcurrentScrapers,
        SearchTermSource searchTermSource,
        int maxCidsToProcess,
        bool allowManualCaptchaSolving)
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

                    InitializeMasterProductListIfNeeded(storeId, false, requireUrl: false);

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
                                            productStateToProcess, assignedScraper, searchTermSource,
                                            _currentCaptchaGlobalCts, maxCidsToProcess, allowManualCaptchaSolving);
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

                        InitializeMasterProductListIfNeeded(storeId, restartAttempt > 0, requireUrl: false);

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
                                                    _currentCaptchaGlobalCts, maxCidsToProcess, allowManualCaptchaSolving);
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
    bool allowManualCaptchaSolving)
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
                break;
            case SearchTermSource.Ean:
                searchTermForGoogle = productState.Ean;
                break;
            case SearchTermSource.ProducerCode:
            default:
                searchTermForGoogle = productState.ProducerCode;
                break;
        }

        if (string.IsNullOrWhiteSpace(searchTermForGoogle))
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Brak terminu do wyszukania."); }
            return;
        }

        Dictionary<string, ProductProcessingState> eligibleProductsMap;
        lock (_lockMasterListInit)
        {
            eligibleProductsMap = _masterProductStateList.Values
                .Where(p => (p.Status == ProductStatus.Pending || p.Status == ProductStatus.Processing)
                            && !string.IsNullOrEmpty(p.ProducerCode))

                .GroupBy(p => p.ProducerCode.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First());
        }

        if (!eligibleProductsMap.Any())
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] Brak produktów oczekujących na dopasowanie po kodzie producenta.");

            lock (productState)
            {
                if (productState.Status == ProductStatus.Processing)
                {
                    productState.UpdateStatus(ProductStatus.NotFound);
                }
            }
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ID: {productState.ProductId}. Szukam: '{searchTermForGoogle}'. Pula do sprawdzenia: {eligibleProductsMap.Count} kodów producenta.");

        var identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermForGoogle, maxItemsToExtract: maxItemsToExtract);

        if (identifierResult.CaptchaEncountered)
        {
            if (allowManualCaptchaSolving)
            {
                bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                if (solved)
                {
                    goto RestartProductProcessing;
                }
                else
                {
                    lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Timeout ręcznego rozwiązania CAPTCHA"); }
                    return;
                }
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
            lock (productState)
            {
                if (productState.Status == ProductStatus.Processing)
                {
                    productState.UpdateStatus(ProductStatus.NotFound);
                }
            }
            return;
        }

        bool initiatingProductMatched = false;

        foreach (var identifier in identifierResult.Data)
        {
            if (cts.IsCancellationRequested) break;
            if (!eligibleProductsMap.Any()) break;

            var titleResult = await scraper.GetProductDetailsFromApiAsync(identifier.Cid, identifier.Gid);

            if (titleResult.CaptchaEncountered)
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

            if (titleResult.IsSuccess && !string.IsNullOrEmpty(titleResult.Data))
            {
                string cleanedTitle = titleResult.Data.Replace(" ", "").Trim();

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
                                        Console.Write("DOPASOWANIE");
                                        Console.ResetColor();
                                        Console.WriteLine($" (wielokrotne)! Tytuł: '{titleResult.Data}' zawiera kod '{cleanedCode}' -> Produkt ID {matchedState.ProductId}.");
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
        if (!_isScrapingActive)
        {
            return Ok("Scrapowanie nie jest aktywne.");
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Otrzymano żądanie ZATRZYMANIA scrapowania...");
        _currentGlobalScrapingOperationCts?.Cancel();

        return Ok("Żądanie zatrzymania wysłane. Proces zakończy się wkrótce.");
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

    private void InitializeMasterProductListIfNeeded(int storeId, bool isRestartAfterCaptcha, bool requireUrl = true)
    {
        lock (_lockMasterListInit)
        {
            bool needsFullReinitialization = false;

            if (isRestartAfterCaptcha)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] WYKRYTO RESTART PO CAPTCHA. Wymuszam pełną reinicjalizację _masterProductStateList dla sklepu ID: {storeId}.");
                needsFullReinitialization = true;
            }
            else if (!_masterProductStateList.Any())
            {
                needsFullReinitialization = true;
            }
            else
            {
                var firstStateProduct = _masterProductStateList.Values.FirstOrDefault();
                if (firstStateProduct != null)
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                        var productInDb = context.Set<ProductClass>().AsNoTracking().FirstOrDefault(p => p.ProductId == firstStateProduct.ProductId);
                        if (productInDb == null || productInDb.StoreId != storeId)
                        {
                            needsFullReinitialization = true;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wykryto zmianę sklepu/brak produktu w DB. Wymuszam pełną reinicjalizację _masterProductStateList.");
                        }
                        else if (_masterProductStateList.Values.All(p => p.Status != ProductStatus.Pending && p.Status != ProductStatus.Processing))
                        {
                            needsFullReinitialization = true;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wszystkie produkty na liście przetworzone lub zatrzymane. Wymuszam pełną reinicjalizację.");
                        }
                    }
                }
                else
                {
                    needsFullReinitialization = true;
                }
            }

            if (needsFullReinitialization)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizuję (pełna) _masterProductStateList dla sklepu ID: {storeId}...");
                _masterProductStateList.Clear();
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                    var query = context.Set<ProductClass>().AsNoTracking()
                        .Where(p => p.StoreId == storeId && p.OnGoogle);

                    if (requireUrl)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizacja: Stosuję filtr wymagający URL produktu.");
                        query = query.Where(p => !string.IsNullOrEmpty(p.Url));
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Inicjalizacja: Pomijam filtr na URL produktu.");
                    }

                    var productsFromDb = query.ToList();

                    var tempScraperForCleaning = new GoogleScraper();

                    foreach (var dbProduct in productsFromDb)
                    {
                        _masterProductStateList.TryAdd(dbProduct.ProductId, new ProductProcessingState(dbProduct, tempScraperForCleaning.CleanUrlParameters));
                    }
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] _masterProductStateList zainicjalizowana. Załadowano {_masterProductStateList.Count} produktów.");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] _masterProductStateList jest już zainicjalizowana i nie wymaga pełnego odświeżenia dla sklepu {storeId}.");
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
    bool allowManualCaptchaSolving)
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
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] OSTRZEŻENIE: Źródło terminu to URL, ale URL jest pusty dla ID: {productState.ProductId}. Używam nazwy produktu jako fallback.");
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.Ean:
                searchTermBase = productState.Ean;
                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] OSTRZEŻENIE: Źródło terminu to EAN, ale EAN jest pusty dla ID: {productState.ProductId}. Używam nazwy produktu jako fallback.");
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.ProducerCode:
                searchTermBase = productState.ProducerCode;
                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] OSTRZEŻENIE: Źródło terminu to Kod Producenta, ale jest pusty dla ID: {productState.ProductId}. Używam nazwy produktu jako fallback.");
                    searchTermBase = productState.ProductNameInStoreForGoogle;
                }
                break;

            case SearchTermSource.ProductName:
            default:
                searchTermBase = productState.ProductNameInStoreForGoogle;
                if (!string.IsNullOrWhiteSpace(namePrefix))
                {
                    searchTermBase = $"{namePrefix} {searchTermBase}";
                }
                break;
        }

        if (string.IsNullOrWhiteSpace(searchTermBase))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] BŁĄD: Nie można wygenerować terminu wyszukiwania dla produktu ID: {productState.ProductId} (źródło: {termSource}, nazwa/URL puste).");
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

                identifierResult = await scraper.SearchInitialProductIdentifiersAsync(searchTermBase, maxCidsToSearch);

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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}, CID {identifier.Cid}: Znaleziono {offersResult.Data.Count} ofert.");
                        foreach (var cleanedOfferUrl in offersResult.Data)
                        {
                            if (localEligibleProductsMap.TryGetValue(cleanedOfferUrl, out var matchedState))
                            {
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
                        }
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
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId && p.OnGoogle)
            .ToListAsync();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var jsonProducts = products.Select(p => {

                string generatedUrl = null;

                string productIdCid = ExtractProductIdFromUrl(p.GoogleUrl);

                if (!string.IsNullOrEmpty(productIdCid) && !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle))
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
                    GeneratedGoogleUrl = generatedUrl
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
                // ================== POPRAWKA ==================
                // Zmieniamy long.TryParse na ulong.TryParse, aby obsłużyć większe identyfikatory
                if (ulong.TryParse(lastSegment, out _))
                // ============================================
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
}