
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
        private ProductStatus _status;
        private string _googleUrl;
        private string _cid;

        public ProductStatus Status { get => _status; set { if (_status != value) { _status = value; IsDirty = true; } } }
        public string GoogleUrl { get => _googleUrl; set { if (_googleUrl != value) { _googleUrl = value; IsDirty = true; } } }
        public string Cid { get => _cid; set { if (_cid != value) { _cid = value; IsDirty = true; } } }
        public int? ProcessingByTaskId { get; set; }
        public bool IsDirty { get; set; }

        public ProductProcessingState(ProductClass product, Func<string, string> cleanUrlFunc)
        {
            ProductId = product.ProductId;
            OriginalUrl = product.Url;
            CleanedUrl = !string.IsNullOrEmpty(product.Url) ? cleanUrlFunc(product.Url) : string.Empty;
            ProductNameInStoreForGoogle = product.ProductNameInStoreForGoogle;
            if (product.FoundOnGoogle == true) { _status = ProductStatus.Found; _googleUrl = product.GoogleUrl; }
            else if (product.FoundOnGoogle == false) { _status = ProductStatus.NotFound; }
            else { _status = ProductStatus.Pending; }
            IsDirty = false;
        }

        public void UpdateStatus(ProductStatus newStatus, string googleUrl = null, string cid = null)
        {
            if (this.Status == ProductStatus.Found && (newStatus == ProductStatus.NotFound || newStatus == ProductStatus.Error))
            {
                Console.WriteLine($"INFO [{System.Threading.Thread.CurrentThread.ManagedThreadId}]: Produkt ID {ProductId} jest już oznaczony jako ZNALEZIONY. Pomijam próbę zmiany statusu na {newStatus}.");
                return;
            }
            this.Status = newStatus;
            if (newStatus == ProductStatus.Found) { this.GoogleUrl = googleUrl; this.Cid = cid; }
        }
    }


    public enum SearchTermSource
    {
        ProductName, 
        ProductUrl
    }


    [HttpPost]

    public async Task<IActionResult> StartScrapingForProducts(
    int storeId,
    int numberOfConcurrentScrapers = 7,
    int maxCidsToProcessPerProduct = 5,
    SearchTermSource searchTermSource = SearchTermSource.ProductName,
    string productNamePrefix = null)
    {
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
            if (searchTermSource == SearchTermSource.ProductName && string.IsNullOrEmpty(productNamePrefix))
            {
                // To jest ok, prefix jest opcjonalny
            }

            int restartAttempt = 0;
            const int MAX_RESTARTS_ON_CAPTCHA = 100;
            bool overallOperationSuccess = false;
            string finalMessage = $"Proces scrapowania dla sklepu {storeId} zainicjowany.";
            bool lastAttemptEndedDueToCaptcha = false;

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
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Brak produktów. Czekam na zadania...");
                                    try { await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray()); } catch (OperationCanceledException) { }
                                    activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);
                                }
                                else
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Brak produktów i zadań. Kończę próbę.");
                                    overallOperationSuccess = true;
                                    break;
                                }
                            }
                            else
                            {
                                bool taskStartedThisIteration = false;
                                while (pendingProductIds.Any() && !linkedCtsForAttempt.Token.IsCancellationRequested &&
                                       await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100), linkedCtsForAttempt.Token))
                                {
                                    if (linkedCtsForAttempt.Token.IsCancellationRequested) { semaphore.Release(); break; }
                                    int selectedProductId; ProductProcessingState productStateToProcess;
                                    lock (_lockMasterListInit)
                                    {
                                        if (!pendingProductIds.Any()) { semaphore.Release(); break; }
                                        int randomIndex = _random.Next(pendingProductIds.Count); selectedProductId = pendingProductIds[randomIndex];
                                        pendingProductIds.RemoveAt(randomIndex);
                                        if (!_masterProductStateList.TryGetValue(selectedProductId, out productStateToProcess)) { semaphore.Release(); continue; }
                                    }
                                    bool canProcess = false;
                                    lock (productStateToProcess)
                                    {
                                        if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
                                        { productStateToProcess.Status = ProductStatus.Processing; productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId; canProcess = true; }
                                    }

                                    if (canProcess)
                                    {
                                        GoogleScraper assignedScraper = null;
                                        if (!availableScrapersPool.TryTake(out assignedScraper))
                                        {
                                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] KRYTYCZNE: Brak wolnego scrapera w puli! Semafor pozwolił, ale pula pusta. Produkt ID: {selectedProductId}");
                                            lock (productStateToProcess) { productStateToProcess.Status = ProductStatus.Pending; productStateToProcess.ProcessingByTaskId = null; } // Revert status
                                            semaphore.Release();
                                            continue;
                                        }

                                        taskStartedThisIteration = true;
                                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Produkt ID {selectedProductId} ({productStateToProcess.ProductNameInStoreForGoogle}) wzięty do przetwarzania przez scrapera.");
                                        var task = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await ProcessSingleProductAsync(
                                                   productStateToProcess,
                                                   assignedScraper,
                                                   storeId,
                                                   _masterProductStateList,
                                                   _currentCaptchaGlobalCts,
                                                   maxCidsToProcessPerProduct, 
                                                   searchTermSource,           
                                                   productNamePrefix          
                                                );
                                            }
                                            catch (OperationCanceledException oce) when (oce.CancellationToken == _currentCaptchaGlobalCts.Token)
                                            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} anulowane przez CAPTCHA signal."); }
                                            catch (OperationCanceledException)
                                            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} anulowane (ogólnie)."); lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Pending); } }
                                            catch (Exception ex)
                                            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] BŁĄD w zadaniu dla ID {productStateToProcess.ProductId}: {ex.Message}"); lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Error); } }
                                            finally
                                            {
                                                lock (productStateToProcess)
                                                { productStateToProcess.ProcessingByTaskId = null; if (productStateToProcess.Status == ProductStatus.Processing) { productStateToProcess.UpdateStatus(ProductStatus.Pending); } }

                                                availableScrapersPool.Add(assignedScraper);
                                                semaphore.Release();
                                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} zakończone. Scraper zwrócony. Slot zwolniony. Status: {productStateToProcess.Status}");
                                            }
                                        }, linkedCtsForAttempt.Token);
                                        activeScrapingTasks.Add(task);
                                    }
                                    else { semaphore.Release(); }
                                }
                                if (!taskStartedThisIteration && pendingProductIds.Any() && activeScrapingTasks.Any(t => !t.IsCompleted) && !linkedCtsForAttempt.Token.IsCancellationRequested)
                                { await Task.Delay(TimeSpan.FromMilliseconds(200), linkedCtsForAttempt.Token); }
                            }
                            activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);

                            if (_currentCaptchaGlobalCts.IsCancellationRequested)
                            { captchaDetectedInCurrentAttempt = true; Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] CAPTCHA. Przerywam pętlę."); break; }
                            if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Globalne zatrzymanie. Przerywam pętlę."); break; }
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Główna pętla zakończona. Czekam na zadania...");
                        try { await Task.WhenAll(activeScrapingTasks.ToArray()); } catch (OperationCanceledException) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Task.WhenAll: Zadania anulowane."); }
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Wszystkie zadania dla tej próby zakończone.");
                    }
                    catch (OperationCanceledException)
                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Operacja ANULOWANA."); if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true; }
                    catch (Exception ex)
                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] BŁĄD KRYTYCZNY: {ex.Message}"); captchaDetectedInCurrentAttempt = false; finalMessage = $"Błąd krytyczny (sklep {storeId}): {ex.Message}"; break; }
                    finally
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Finally dla próby...");
                        if (captchaDetectedInCurrentAttempt && !_currentGlobalScrapingOperationCts.IsCancellationRequested) { _currentGlobalScrapingOperationCts.Cancel(); }

                        var remainingTasks = activeScrapingTasks.Where(t => !t.IsCompleted).ToArray();
                        if (remainingTasks.Any())
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Czekam na {remainingTasks.Length} zadań w finally próby...");
                            try { await Task.WhenAll(remainingTasks); } catch (OperationCanceledException) { }
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Końcowy zapis zmian...");
                        await BatchUpdateDatabaseAsync(true, CancellationToken.None);

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zamykanie {scraperInstancesForThisAttempt.Count} instancji scrapera dla tej próby...");
                        foreach (var sc in scraperInstancesForThisAttempt) { await sc.CloseBrowserAsync(); }
                        scraperInstancesForThisAttempt.Clear();

                        _currentGlobalScrapingOperationCts?.Dispose(); _currentCaptchaGlobalCts?.Dispose();
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zakończono finally dla próby.");
                    }

                    lastAttemptEndedDueToCaptcha = captchaDetectedInCurrentAttempt;

                    if (lastAttemptEndedDueToCaptcha)
                    {
                        restartAttempt++;
                        if (restartAttempt <= MAX_RESTARTS_ON_CAPTCHA)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CAPTCHA. Próba {restartAttempt - 1} nieudana. Reset sieci...");
                            bool networkResetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Reset sieci: {(networkResetSuccess ? "OK" : "FAIL")}.");
                            if (!networkResetSuccess) { finalMessage = $"Reset sieci FAIL po CAPTCHA. Stop po {restartAttempt - 1} próbach."; break; }
                        }
                        else { finalMessage = $"MAX ({MAX_RESTARTS_ON_CAPTCHA}) restartów po CAPTCHA. Stop."; break; }
                    }
                    else if (overallOperationSuccess) { finalMessage = $"Sklep {storeId} OK."; break; }
                    else if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { finalMessage = $"Sklep {storeId} STOP (sygnał)."; break; }
                } while (lastAttemptEndedDueToCaptcha && restartAttempt <= MAX_RESTARTS_ON_CAPTCHA);
            }
            finally
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] OSTATECZNE finally...");
                lock (_lockTimer)
                {
                    if (_batchSaveTimer != null) { _batchSaveTimer.Dispose(); _batchSaveTimer = null; Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Globalny timer Zatrzymany."); }
                }
                _isScrapingActive = false;
                _currentGlobalScrapingOperationCts?.Dispose(); _currentCaptchaGlobalCts?.Dispose();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Flaga _isScrapingActive=false. Gotowy na nowe żądanie.");
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ostateczny status: {finalMessage}");
            return Content(finalMessage);
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
          
            lockTaken = await _timerCallbackSemaphore.WaitAsync(TimeSpan.FromSeconds(1) );
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

    private void InitializeMasterProductListIfNeeded(int storeId, bool isRestartAfterCaptcha)
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
                    var productsFromDb = context.Set<ProductClass>().AsNoTracking()
                        .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url)).ToList();

              
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
                bool processThisProduct = false;

                lock (productState)
                {
                    if (productState.IsDirty)
                    {
                        processThisProduct = true;
                        currentStatusSnapshot = productState.Status;
                        currentGoogleUrlSnapshot = productState.GoogleUrl;
                        currentCidSnapshot = productState.Cid;
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
                            if (dbProduct.FoundOnGoogle != true || dbProduct.GoogleUrl != currentGoogleUrlSnapshot)
                            { dbProduct.FoundOnGoogle = true; dbProduct.GoogleUrl = currentGoogleUrlSnapshot; changedInDb = true; }
                        }
                        else if (currentStatusSnapshot == ProductStatus.NotFound)
                        {
                            if (dbProduct.FoundOnGoogle != false || dbProduct.GoogleUrl != null)
                            { dbProduct.FoundOnGoogle = false; dbProduct.GoogleUrl = null; changedInDb = true; }
                        }
                        else if (currentStatusSnapshot == ProductStatus.Error)
                        {
                            if (dbProduct.FoundOnGoogle != false)
                            { dbProduct.FoundOnGoogle = false; dbProduct.GoogleUrl = $"Error State @ {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss Z}"; changedInDb = true; }
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
     string namePrefix)
    {
        if (captchaCts.IsCancellationRequested)
        { lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); } return; }


        string searchTermBase;


        switch (termSource)
        {
            case SearchTermSource.ProductUrl:
                searchTermBase = productState.OriginalUrl; // Używamy oryginalnego URL
                                                           // Można też rozważyć productState.CleanedUrl, jeśli ma to sens
                if (string.IsNullOrWhiteSpace(searchTermBase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] OSTRZEŻENIE: Źródło terminu to URL, ale URL jest pusty dla ID: {productState.ProductId}. Używam nazwy produktu jako fallback.");
                    searchTermBase = productState.ProductNameInStoreForGoogle; // Fallback na nazwę produktu
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
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Przetwarzam produkt: {productState.ProductNameInStoreForGoogle} (ID: {productState.ProductId}), Szukam: '{searchTermBase}'");

        try
        {
            ScraperResult<List<string>> cidResult = await scraper.SearchInitialProductCIDsAsync(searchTermBase, maxCidsToSearch);
            if (cidResult.CaptchaEncountered)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] CAPTCHA (SearchInitialProductCIDsAsync) dla ID {productState.ProductId}. Sygnalizuję.");
                if (!captchaCts.IsCancellationRequested) captchaCts.Cancel();
                lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                return;
            }
            if (captchaCts.IsCancellationRequested) { lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); } return; }

            if (!cidResult.IsSuccess || !cidResult.Data.Any())
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Nie znaleziono CID/błąd dla '{searchTermBase}' (ID {productState.ProductId}). Msg: {cidResult.ErrorMessage}");
                lock (productState) { productState.UpdateStatus(ProductStatus.NotFound); }
            }
            else
            {
                List<string> initialCIDs = cidResult.Data;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Znaleziono {initialCIDs.Count} CID dla ID {productState.ProductId}. Sprawdzam oferty.");
                Dictionary<string, ProductProcessingState> localEligibleProductsMap;
                lock (_lockMasterListInit)
                {
                    localEligibleProductsMap = masterList.Values
                        .Where(p => (p.Status == ProductStatus.Pending || p.Status == ProductStatus.NotFound || p.Status == ProductStatus.Error || p.Status == ProductStatus.Processing || p.Status == ProductStatus.CaptchaHalt)
                               && !string.IsNullOrEmpty(p.CleanedUrl))
                        .GroupBy(p => p.CleanedUrl).ToDictionary(g => g.Key, g => g.First());
                }
                bool initiatingProductDirectlyMatchedInThisTask = false;
                lock (productState) { if (productState.Status == ProductStatus.Found) initiatingProductDirectlyMatchedInThisTask = true; }

                foreach (var cid in initialCIDs)
                {
                    if (captchaCts.IsCancellationRequested)
                        break;

                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] " +
                                      $"ID {productState.ProductId}: Przetwarzam CID: {cid}");

                    // 1) Paginacja + ekstrakcja wszystkich ofert
                    ScraperResult<List<string>> offersResult =
                        await scraper.NavigateToProductPageAndExpandOffersAsync(cid);

                    // 2) CAPTCHA?
                    if (offersResult.CaptchaEncountered)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] " +
                                          $"CAPTCHA (Navigate+Extract) CID {cid}, ID {productState.ProductId}. Sygnalizuję.");
                        if (!captchaCts.IsCancellationRequested)
                            captchaCts.Cancel();

                        lock (productState)
                        {
                            productState.UpdateStatus(ProductStatus.CaptchaHalt);
                        }
                        return;
                    }

                    if (captchaCts.IsCancellationRequested)
                        break;

                    // 3) Mamy oferty?
                    if (offersResult.IsSuccess && offersResult.Data.Any())
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] " +
                                          $"ID {productState.ProductId}, CID {cid}: Znaleziono {offersResult.Data.Count} ofert.");

                        foreach (var cleanedOfferUrl in offersResult.Data)
                        {
                            if (captchaCts.IsCancellationRequested)
                                break;

                            if (localEligibleProductsMap.TryGetValue(cleanedOfferUrl, out var matchedState))
                            {
                                lock (matchedState)
                                {
                                    string googleProductPageUrl = $"https://www.google.com/shopping/product/{cid}";
                                    matchedState.UpdateStatus(ProductStatus.Found, googleProductPageUrl, cid);
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] " +
                                                      $"✓ DOPASOWANO! {cleanedOfferUrl} → ID {matchedState.ProductId}. CID: {cid}");

                                    if (matchedState.ProductId == productState.ProductId)
                                        initiatingProductDirectlyMatchedInThisTask = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        // brak ofert lub błąd
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] " +
                                          $"ID {productState.ProductId}, CID {cid}: Brak ofert lub błąd. Msg: {offersResult.ErrorMessage}");
                    }

                    if (captchaCts.IsCancellationRequested)
                        break;

                    // krótka losowa przerwa przed kolejnym CID
                    await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(200, 400)), CancellationToken.None);
                }


                if (!captchaCts.IsCancellationRequested)
                {
                    lock (productState)
                    {
                        if ((productState.Status == ProductStatus.Processing || productState.Status == ProductStatus.Pending) && !initiatingProductDirectlyMatchedInThisTask)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] \u2717 Produkt inicjujący ID {productState.ProductId} NIE znaleziony. Status: NotFound.");
                            productState.UpdateStatus(ProductStatus.NotFound);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (captchaCts.IsCancellationRequested)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Przetwarzanie ID {productState.ProductId} anulowane przez CAPTCHA.");
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] BŁĄD OGÓLNY (ProcessSingle) dla ID {productState.ProductId}: {ex.GetType().Name} - {ex.Message}");
            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
        }
        finally
        {
            if (captchaCts.IsCancellationRequested)
            { lock (productState) { if (productState.Status != ProductStatus.Found && productState.Status != ProductStatus.Error) productState.Status = ProductStatus.CaptchaHalt; } }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] --- Koniec ProcessSingle dla ID {productState.ProductId}. Finalny Status: {productState.Status} ---");
        }
    }


// Nowa powolna

//[HttpPost]
//public async Task<IActionResult> StartScrapingForProducts(int storeId)
//{
//    var store = await _context.Stores.FindAsync(storeId);
//    if (store == null)
//    {
//        return NotFound();
//    }

//    var scraper = new GoogleScraper();
//    while (true)
//    {
//        var productsToProcessQuery = _context.Products
//            .Where(p => p.StoreId == storeId
//                         && p.OnGoogle
//                         && !string.IsNullOrEmpty(p.Url)
//                         && p.FoundOnGoogle == null);

//        var productsToProcessCandidates = await productsToProcessQuery.ToListAsync();

//        if (!productsToProcessCandidates.Any())
//        {
//            Console.WriteLine("Nie ma więcej produktów do przetworzenia dla tego sklepu (kryterium FoundOnGoogle == null).");
//            break;
//        }

//        Random random = new Random();
//        var productToProcess = productsToProcessCandidates[random.Next(productsToProcessCandidates.Count)];



//        // Opcja 1: Wyszukiwanie po nazwie produktu
//        //string searchTermBase = productToProcess.ProductNameInStoreForGoogle;

//        // Opcja 2: Wyszukiwanie po URL-u produktu (upewnij się, że URL jest sensowny jako termin wyszukiwania)
//        string searchTermBase = productToProcess.Url;

//        string storeSuffix = " k3design"; // Lub np. $" {store.Name}"; jeśli 'store.Name' istnieje
//        if (string.IsNullOrWhiteSpace(productToProcess.ProductNameInStoreForGoogle) && searchTermBase == productToProcess.ProductNameInStoreForGoogle)
//        {
//            Console.WriteLine($"OSTRZEŻENIE: Nazwa produktu (ProductNameInStoreForGoogle) dla produktu ID: {productToProcess.ProductId} jest pusta. Używam URL jako searchTermBase.");
//            searchTermBase = productToProcess.Url; // Awaryjnie URL, jeśli nazwa jest pusta
//            if (string.IsNullOrEmpty(searchTermBase))
//            {
//                Console.WriteLine($"BŁĄD KRYTYCZNY: Zarówno nazwa produktu, jak i URL są puste dla produktu ID: {productToProcess.ProductId}. Pomijam produkt.");
//                productToProcess.FoundOnGoogle = false; // Oznacz jako nieznaleziony/błąd
//                _context.Products.Update(productToProcess);
//                await _context.SaveChangesAsync();
//                continue; // Przejdź do następnego produktu
//            }
//        }

//        //NAZWA PLUS PREFIX
//        //string finalSearchTerm = $"{storeSuffix} {searchTermBase}";

//        // GOLY URL
//        string finalSearchTerm = $"{searchTermBase}";

//        Console.WriteLine($"\n--- Rozpoczynam wyszukiwanie dla produktu z DB: {productToProcess.ProductNameInStoreForGoogle} (ID: {productToProcess.ProductId}, URL: {productToProcess.Url}) ---");
//        Console.WriteLine($"Użyty searchTerm dla Google: '{finalSearchTerm}' ---");
//        // --- KONIEC MODYFIKACJI ---

//        string cidForCurrentGooglePage = null;
//        bool anEligibleStoreProductWasUpdatedInThisIteration = false;

//        try
//        {
//            // Użyj 'finalSearchTerm' zamiast 'productToProcess.ProductNameInStoreForGoogle'
//            cidForCurrentGooglePage = await scraper.SearchAndClickFirstProductAsync(finalSearchTerm);

//            if (!string.IsNullOrEmpty(cidForCurrentGooglePage))
//            {
//                var allStoreProductsEligibleForUpdate = await _context.Products
//                    .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.Url) && p.FoundOnGoogle == null)
//                    .Select(p => new { p.ProductId, RawUrl = p.Url, p.ProductNameInStoreForGoogle })
//                    .ToListAsync();

//                if (!allStoreProductsEligibleForUpdate.Any())
//                {
//                    Console.WriteLine("Brak produktów w sklepie kwalifikujących się do aktualizacji (FoundOnGoogle == null). Pomijam sprawdzanie ofert.");
//                }
//                else
//                {
//                    var cleanedUrlToProductInfoMap = allStoreProductsEligibleForUpdate
//                        .GroupBy(p => scraper.CleanUrlParameters(p.RawUrl))
//                        .ToDictionary(
//                            g => g.Key,
//                            g => g.First()
//                        );
//                    //var allCleanedStoreUrlsForMatching = cleanedUrlToProductInfoMap.Keys.ToList(); // Już niepotrzebne bezpośrednio

//                    Console.WriteLine($"Przygotowano {cleanedUrlToProductInfoMap.Count} unikalnych, oczyszczonych URL-i sklepu do porównania z ofertami.");

//                    Console.WriteLine($"Sprawdzanie ofert dla pierwszego boksu Google (CID: {cidForCurrentGooglePage})...");
//                    var offersFromCurrentGoogleBox = await scraper.ExtractStoreOffersAsync(scraper.CurrentPage);
//                    Console.WriteLine($"Znaleziono {offersFromCurrentGoogleBox.Count} oczyszczonych ofert dla CID {cidForCurrentGooglePage}.");

//                    foreach (var offerUrlFromGoogle in offersFromCurrentGoogleBox)
//                    {
//                        if (cleanedUrlToProductInfoMap.TryGetValue(offerUrlFromGoogle, out var matchedProductInfo))
//                        {
//                            var productToUpdateInDb = await _context.Products.FindAsync(matchedProductInfo.ProductId);
//                            if (productToUpdateInDb != null && productToUpdateInDb.FoundOnGoogle == null)
//                            {
//                                Console.WriteLine($"\u2713 Dopasowano w boksie Google (CID: {cidForCurrentGooglePage})! Oferta Google: {offerUrlFromGoogle}. " +
//                                                  $"Produkt z DB (ID: {productToUpdateInDb.ProductId}, Nazwa: {productToUpdateInDb.ProductNameInStoreForGoogle}, URL: {matchedProductInfo.RawUrl}) " +
//                                                  $"zostanie zaktualizowany.");
//                                productToUpdateInDb.FoundOnGoogle = true;
//                                productToUpdateInDb.GoogleUrl = $"https://www.google.com/shopping/product/{cidForCurrentGooglePage}";
//                                _context.Products.Update(productToUpdateInDb);
//                                anEligibleStoreProductWasUpdatedInThisIteration = true;
//                            }
//                            else if (productToUpdateInDb != null && productToUpdateInDb.FoundOnGoogle == true)
//                            {
//                                Console.WriteLine($"INFO: Oferta Google: {offerUrlFromGoogle} pasuje do produktu (ID: {productToUpdateInDb.ProductId}), który już został oznaczony jako znaleziony.");
//                            }
//                        }
//                    }

//                    if (!anEligibleStoreProductWasUpdatedInThisIteration)
//                    {
//                        Console.WriteLine($"\u2717 Żaden kwalifikujący się produkt sklepu nie został dopasowany w pierwszym boksie Google (CID: {cidForCurrentGooglePage}). Próba kolejnych boksów Google...");
//                        int maxGoogleBoxesToTry = 10;
//                        for (int googleBoxIndex = 1; googleBoxIndex < maxGoogleBoxesToTry; googleBoxIndex++)
//                        {
//                            Console.WriteLine($"\n--- Próba przetworzenia boksu produktu Google o indeksie {googleBoxIndex} na stronie wyników ---");
//                            var nextGoogleBoxResult = await scraper.ClickProductBoxByIndexAndExtractOffersAsync(googleBoxIndex);

//                            if (nextGoogleBoxResult.HasValue)
//                            {
//                                string currentGoogleBoxCid = nextGoogleBoxResult.Value.Cid;
//                                var offersFromNextGoogleBox = nextGoogleBoxResult.Value.Offers;
//                                Console.WriteLine($"Znaleziono {offersFromNextGoogleBox.Count} ofert w boksie Google {googleBoxIndex} (CID: {currentGoogleBoxCid}).");

//                                foreach (var offerUrlFromGoogle in offersFromNextGoogleBox)
//                                {
//                                    if (cleanedUrlToProductInfoMap.TryGetValue(offerUrlFromGoogle, out var matchedProductInfo))
//                                    {
//                                        var productToUpdateInDb = await _context.Products.FindAsync(matchedProductInfo.ProductId);
//                                        if (productToUpdateInDb != null && productToUpdateInDb.FoundOnGoogle == null)
//                                        {
//                                            Console.WriteLine($"\u2713 Dopasowano w boksie Google {googleBoxIndex} (CID: {currentGoogleBoxCid})! Oferta Google: {offerUrlFromGoogle}. " +
//                                                              $"Produkt z DB (ID: {productToUpdateInDb.ProductId}, Nazwa: {productToUpdateInDb.ProductNameInStoreForGoogle}, URL: {matchedProductInfo.RawUrl}) " +
//                                                              $"zostanie zaktualizowany.");
//                                            productToUpdateInDb.FoundOnGoogle = true;
//                                            productToUpdateInDb.GoogleUrl = $"https://www.google.com/shopping/product/{currentGoogleBoxCid}";
//                                            _context.Products.Update(productToUpdateInDb);
//                                            anEligibleStoreProductWasUpdatedInThisIteration = true;
//                                        }
//                                        else if (productToUpdateInDb != null && productToUpdateInDb.FoundOnGoogle == true)
//                                        {
//                                            Console.WriteLine($"INFO: Oferta Google: {offerUrlFromGoogle} pasuje do produktu (ID: {productToUpdateInDb.ProductId}), który już został oznaczony jako znaleziony.");
//                                        }
//                                    }
//                                }
//                            }
//                            else
//                            {
//                                Console.WriteLine($"Nie udało się przetworzyć boksu Google {googleBoxIndex} lub brak więcej boksów. Koniec pętli boksów Google.");
//                                break;
//                            }
//                            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(1, 3)));
//                        }
//                    }
//                }
//            }
//            else
//            {
//                Console.WriteLine($"Produkt (dla którego zainicjowano wyszukiwanie: {productToProcess.ProductNameInStoreForGoogle} używając searchTerm: '{finalSearchTerm}') nie został odnaleziony na stronie wyników Google (SearchAndClickFirstProductAsync zwrócił null).");
//                if (productToProcess.FoundOnGoogle == null)
//                {
//                    productToProcess.FoundOnGoogle = false;
//                    productToProcess.GoogleUrl = null;
//                    _context.Products.Update(productToProcess);
//                }
//            }

//            if (!anEligibleStoreProductWasUpdatedInThisIteration && productToProcess.FoundOnGoogle == null)
//            {
//                Console.WriteLine($"INFO: Produkt (inicjujący wyszukiwanie: {productToProcess.ProductNameInStoreForGoogle}) oznaczony jako NIEZNALEZIONY, " +
//                                  "ponieważ żadna oferta z przeglądanych boksów Google nie doprowadziła do jego aktualizacji ani aktualizacji innego produktu sklepu.");
//                productToProcess.FoundOnGoogle = false;
//                productToProcess.GoogleUrl = null;
//                _context.Products.Update(productToProcess);
//            }

//            await _context.SaveChangesAsync();
//            Console.WriteLine("Zapisano wszystkie zmiany w bazie danych dla tej iteracji produktu.");

//            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 10)));
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"KRYTYCZNY BŁĄD w kontrolerze podczas przetwarzania (produktu inicjującego: {productToProcess?.ProductNameInStoreForGoogle}): {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
//            if (productToProcess != null && productToProcess.FoundOnGoogle == null)
//            {
//                productToProcess.FoundOnGoogle = false;
//                productToProcess.GoogleUrl = null;
//                _context.Products.Update(productToProcess);
//                await _context.SaveChangesAsync();
//            }
//            Console.WriteLine("Próba zamknięcia i zresetowania przeglądarki scrapera po błędzie w kontrolerze...");
//            await scraper.CloseBrowserAsync();
//            scraper = new GoogleScraper();
//            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(15, 30)));
//        }
//        Console.WriteLine($"--- Zakończono iterację pętli while dla produktu (inicjującego: {productToProcess?.ProductNameInStoreForGoogle}) ---\n");
//    }

//    await scraper.CloseBrowserAsync();
//    Console.WriteLine("Zakończono scrapowanie dla wszystkich produktów lub nie ma więcej produktów do przetworzenia.");
//    return Content("Scraping completed for all products or no more products to process.");
//}




// Stara metoda 


//[HttpPost]
//public async Task<IActionResult> StartScrapingForProducts(int storeId)
//{
//    var store = await _context.Stores.FindAsync(storeId);
//    if (store == null)
//    {
//        return NotFound();
//    }

//    var googleMiG = store.GoogleMiG;
//    if (string.IsNullOrEmpty(googleMiG))
//    {
//        return BadRequest("GoogleMiG is not set for this store.");
//    }

//    var scraper = new GoogleScraper();
//    await scraper.InitializeBrowserAsync();

//    // Pętla, która będzie działać, dopóki są produkty do przetworzenia
//    while (true)
//    {
//        // Pobierz produkty, które są OnGoogle, mają niepusty URL i jeszcze nie były przetworzone (FoundOnGoogle == null)
//        var productsToProcess = await _context.Products
//            .Where(p => p.StoreId == storeId
//                     && p.OnGoogle
//                     && !string.IsNullOrEmpty(p.Url)
//                     && p.FoundOnGoogle == null)
//            .ToListAsync();

//        if (!productsToProcess.Any())
//        {
//            Console.WriteLine("No products left to process.");
//            break; // Kończymy pętlę, gdy nie ma już produktów do przetworzenia
//        }

//        // Wybieramy losowy produkt z aktualnej listy do przetwarzania
//        Random random = new Random();
//        var productToProcess = productsToProcess[random.Next(productsToProcess.Count)];

//        // Pobieramy pełną listę produktów (wszystkich URL-i) dla porównania
//        var allProducts = await _context.Products
//            .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url))
//            .ToListAsync();

//        // Tworzymy słownik dla szybkiego dostępu po URL
//        var productDict = allProducts.ToDictionary(p => p.Url, p => p);
//        var allProductUrls = productDict.Keys.ToList();

//        try
//        {
//            // Wyszukujemy produkt na Google używając nazwy produktu
//            await scraper.InitializeAndSearchAsync(productToProcess.ProductNameInStoreForGoogle, googleMiG);

//            // Pobieramy listę dopasowanych URL-i
//            var matchedUrls = await scraper.SearchForMatchingProductUrlsAsync(allProductUrls);

//            // Aktualizujemy produkty, które zostały znalezione
//            foreach (var (matchedStoreUrl, googleProductUrl) in matchedUrls)
//            {
//                if (productDict.TryGetValue(matchedStoreUrl, out var matchedProduct))
//                {
//                    // Bez względu na poprzedni status, ustawiamy FoundOnGoogle na true i zapisujemy URL
//                    matchedProduct.GoogleUrl = googleProductUrl;
//                    matchedProduct.FoundOnGoogle = true;
//                    Console.WriteLine($"Updated product: {matchedProduct.ProductName}, GoogleUrl: {matchedProduct.GoogleUrl}");

//                    _context.Products.Update(matchedProduct);
//                    await _context.SaveChangesAsync();
//                }
//            }

//            // Jeśli przetwarzany produkt nie został znaleziony, ustawiamy jego status na false
//            if (!matchedUrls.Any(m => m.storeUrl == productToProcess.Url))
//            {
//                productToProcess.FoundOnGoogle = false;
//                Console.WriteLine($"Product not found on Google: {productToProcess.ProductName}");
//                _context.Products.Update(productToProcess);
//                await _context.SaveChangesAsync();
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing product: {ex.Message}");
//        }
//    }

//    await scraper.CloseBrowserAsync();
//    return Content("Scraping completed for all products.");
//}






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
        // Pobranie produktów z prawidłowym statusem FoundOnGoogle i GoogleUrl
        var products = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl))
            .ToListAsync();

        foreach (var product in products)
        {
            // Sprawdzenie, czy GoogleUrl spełnia wymagany schemat (czy zawiera "shopping/product")
            if (!product.GoogleUrl.Contains("shopping/product"))
            {
                // Jeżeli URL jest nieprawidłowy, aktualizujemy produkt
                product.FoundOnGoogle = false;
                product.GoogleUrl = null;

                _context.Products.Update(product);
            }
        }

        // Zapisanie zmian w bazie danych
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId });
    }


  

    [HttpPost]
    public async Task<IActionResult> SetOnGoogleForAll()
    {
        // Pobieramy produkty, które mają wypełnione pole ProductNameInStoreForGoogle
        var products = await _context.Products
            .Where(p => !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle))
            .ToListAsync();

        foreach (var product in products)
        {
            // Jeśli pole Url nie jest puste, ustawiamy OnGoogle na true
            if (!string.IsNullOrEmpty(product.Url))
            {
                product.OnGoogle = true;
            }
            // Jeśli pole Url jest puste, ustawiamy OnGoogle na false
            else
            {
                product.OnGoogle = false;
            }

            // Aktualizujemy produkt w bazie danych
            _context.Products.Update(product);
        }

        // Zapisujemy zmiany
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = products.FirstOrDefault()?.StoreId });
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
            
            var jsonProducts = products.Select(p => new
            {
                p.ProductId,
                p.ProductNameInStoreForGoogle,
                p.Url,
                p.FoundOnGoogle,
                p.GoogleUrl
            }).ToList();

            return Json(jsonProducts);
        }

 
        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/GoogleProducts.cshtml", products);
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
        // Znajdź produkty, które mają FoundOnGoogle = true, mają GoogleUrl, ale ProductUrl jest null
        var productsToReset = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl) && string.IsNullOrEmpty(p.Url))
            .ToListAsync();

        foreach (var product in productsToReset)
        {
            // Ustawiamy FoundOnGoogle na null i usuwamy GoogleUrl
            product.FoundOnGoogle = null;
            product.GoogleUrl = null;
            _context.Products.Update(product);
        }

        // Zapisanie zmian
        await _context.SaveChangesAsync();

        return Ok(); // Możesz zwrócić inne odpowiedzi w zależności od tego, co chcesz
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
            product.FoundOnGoogle = null; // Opcjonalnie możesz zresetować także ten status
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok(); // Możesz zwrócić inną odpowiedź w zależności od potrzeb
    }
}