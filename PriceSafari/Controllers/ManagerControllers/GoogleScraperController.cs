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

        public string Ean { get; set; }
        public string ProducerCode { get; set; }

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
            Ean = product.Ean;
            ProducerCode = product.ProducerCode;
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

            return await StartScrapingForProducts_IntermediateMatchAsync(storeId, numberOfConcurrentScrapers, searchTermSource, maxCidsToProcessPerProduct);
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















    //public async Task<IActionResult> StartScrapingForProducts_StandardAsync(
    //    int storeId,
    //    int numberOfConcurrentScrapers = 5,
    //    int maxCidsToProcessPerProduct = 3,
    //    SearchTermSource searchTermSource = SearchTermSource.ProductName,
    //    string productNamePrefix = null,
    //     bool allowManualCaptchaSolving = false)

    //{
    //    if (_isScrapingActive)
    //    {
    //        return Conflict("Proces scrapowania jest już globalnie aktywny.");
    //    }
    //    _isScrapingActive = true;

    //    if (maxCidsToProcessPerProduct <= 0)
    //    {
    //        _isScrapingActive = false;
    //        return BadRequest("Liczba CIDów do przetworzenia musi być dodatnia.");
    //    }
    //    if (searchTermSource == SearchTermSource.ProductName && string.IsNullOrEmpty(productNamePrefix))
    //    {

    //    }

    //    int restartAttempt = 0;
    //    const int MAX_RESTARTS_ON_CAPTCHA = 100;
    //    bool overallOperationSuccess = false;
    //    string finalMessage = $"Proces scrapowania dla sklepu {storeId} zainicjowany.";
    //    bool lastAttemptEndedDueToCaptcha = false;

    //    lock (_lockTimer)
    //    {
    //        if (_batchSaveTimer == null)
    //        {
    //            _batchSaveTimer = new Timer(async _ => await TimerBatchUpdateCallback(CancellationToken.None), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    //            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer do zapisu wsadowego globalnie uruchomiony.");
    //        }
    //    }

    //    try
    //    {
    //        do
    //        {
    //            bool captchaDetectedInCurrentAttempt = false;
    //            _currentGlobalScrapingOperationCts = new CancellationTokenSource();
    //            _currentCaptchaGlobalCts = new CancellationTokenSource();
    //            var activeScrapingTasks = new List<Task>();
    //            overallOperationSuccess = false;

    //            List<GoogleScraper> scraperInstancesForThisAttempt = new List<GoogleScraper>();
    //            ConcurrentBag<GoogleScraper> availableScrapersPool = null;

    //            if (restartAttempt > 0)
    //            {
    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] === Próba restartu scrapowania nr {restartAttempt} po CAPTCHA dla sklepu {storeId} ===");
    //                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
    //            }

    //            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rozpoczynam próbę scrapowania nr {restartAttempt} dla sklepu ID: {storeId}.");

    //            try
    //            {
    //                using (var initialScope = _scopeFactory.CreateScope())
    //                {
    //                    var context = initialScope.ServiceProvider.GetRequiredService<PriceSafariContext>();
    //                    var store = await context.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId);
    //                    if (store == null) { finalMessage = "Sklep nie znaleziony."; _isScrapingActive = false; return NotFound(finalMessage); }
    //                }
    //                if (numberOfConcurrentScrapers <= 0) { finalMessage = "Liczba scraperów musi być dodatnia."; _isScrapingActive = false; return BadRequest(finalMessage); }

    //                var initTasks = new List<Task>();
    //                for (int i = 0; i < numberOfConcurrentScrapers; i++)
    //                {
    //                    var sc = new GoogleScraper();
    //                    scraperInstancesForThisAttempt.Add(sc);
    //                    initTasks.Add(sc.InitializeBrowserAsync());
    //                }
    //                await Task.WhenAll(initTasks);
    //                availableScrapersPool = new ConcurrentBag<GoogleScraper>(scraperInstancesForThisAttempt);
    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Utworzono i zainicjowano {scraperInstancesForThisAttempt.Count} instancji scrapera do puli.");

    //                InitializeMasterProductListIfNeeded(storeId, restartAttempt > 0);

    //                var semaphore = new SemaphoreSlim(numberOfConcurrentScrapers, numberOfConcurrentScrapers);
    //                var linkedCtsForAttempt = CancellationTokenSource.CreateLinkedTokenSource(_currentGlobalScrapingOperationCts.Token, _currentCaptchaGlobalCts.Token);

    //                while (!_currentGlobalScrapingOperationCts.IsCancellationRequested &&
    //                       !_currentCaptchaGlobalCts.IsCancellationRequested &&
    //                       !_masterProductStateList.Values.Any(p => p.Status == ProductStatus.CaptchaHalt)) // <-- DODAJ TEN WARUNEK
    //                {
    //                    List<int> pendingProductIds;
    //                    lock (_lockMasterListInit)
    //                    {
    //                        pendingProductIds = _masterProductStateList
    //                            .Where(kvp => kvp.Value.Status == ProductStatus.Pending && kvp.Value.ProcessingByTaskId == null)
    //                            .Select(kvp => kvp.Key).ToList();
    //                    }

    //                    if (!pendingProductIds.Any())
    //                    {
    //                        if (activeScrapingTasks.Any(t => !t.IsCompleted))
    //                        {
    //                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Brak produktów. Czekam na zadania...");
    //                            try { await Task.WhenAny(activeScrapingTasks.Where(t => !t.IsCompleted).ToArray()); } catch (OperationCanceledException) { }
    //                            activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);
    //                        }
    //                        else
    //                        {
    //                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Brak produktów i zadań. Kończę próbę.");
    //                            overallOperationSuccess = true;
    //                            break;
    //                        }
    //                    }
    //                    else
    //                    {
    //                        bool taskStartedThisIteration = false;
    //                        while (pendingProductIds.Any() && !linkedCtsForAttempt.Token.IsCancellationRequested &&
    //                               await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100), linkedCtsForAttempt.Token))
    //                        {
    //                            if (linkedCtsForAttempt.Token.IsCancellationRequested) { semaphore.Release(); break; }
    //                            int selectedProductId; ProductProcessingState productStateToProcess;
    //                            lock (_lockMasterListInit)
    //                            {
    //                                if (!pendingProductIds.Any()) { semaphore.Release(); break; }
    //                                int randomIndex = _random.Next(pendingProductIds.Count); selectedProductId = pendingProductIds[randomIndex];
    //                                pendingProductIds.RemoveAt(randomIndex);
    //                                if (!_masterProductStateList.TryGetValue(selectedProductId, out productStateToProcess)) { semaphore.Release(); continue; }
    //                            }
    //                            bool canProcess = false;
    //                            lock (productStateToProcess)
    //                            {
    //                                if (productStateToProcess.Status == ProductStatus.Pending && productStateToProcess.ProcessingByTaskId == null)
    //                                { productStateToProcess.Status = ProductStatus.Processing; productStateToProcess.ProcessingByTaskId = Task.CurrentId ?? Thread.CurrentThread.ManagedThreadId; canProcess = true; }
    //                            }

    //                            if (canProcess)
    //                            {
    //                                GoogleScraper assignedScraper = null;
    //                                if (!availableScrapersPool.TryTake(out assignedScraper))
    //                                {
    //                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] KRYTYCZNE: Brak wolnego scrapera w puli! Semafor pozwolił, ale pula pusta. Produkt ID: {selectedProductId}");
    //                                    lock (productStateToProcess) { productStateToProcess.Status = ProductStatus.Pending; productStateToProcess.ProcessingByTaskId = null; }
    //                                    semaphore.Release();
    //                                    continue;
    //                                }

    //                                taskStartedThisIteration = true;
    //                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Produkt ID {selectedProductId} ({productStateToProcess.ProductNameInStoreForGoogle}) wzięty do przetwarzania przez scrapera.");
    //                                var task = Task.Run(async () =>
    //                                {
    //                                    try
    //                                    {
    //                                        await ProcessSingleProductAsync(
    //                                           productStateToProcess,
    //                                           assignedScraper,
    //                                           storeId,
    //                                           _masterProductStateList,
    //                                           _currentCaptchaGlobalCts,
    //                                           maxCidsToProcessPerProduct,
    //                                           searchTermSource,
    //                                           productNamePrefix,
    //                                           allowManualCaptchaSolving
    //                                        );
    //                                    }
    //                                    catch (OperationCanceledException oce) when (oce.CancellationToken == _currentCaptchaGlobalCts.Token)
    //                                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} anulowane przez CAPTCHA signal."); }
    //                                    catch (OperationCanceledException)
    //                                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} anulowane (ogólnie)."); lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Pending); } }
    //                                    catch (Exception ex)
    //                                    { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] BŁĄD w zadaniu dla ID {productStateToProcess.ProductId}: {ex.Message}"); lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Error); } }
    //                                    finally
    //                                    {
    //                                        lock (productStateToProcess)
    //                                        { productStateToProcess.ProcessingByTaskId = null; if (productStateToProcess.Status == ProductStatus.Processing) { productStateToProcess.UpdateStatus(ProductStatus.Pending); } }

    //                                        availableScrapersPool.Add(assignedScraper);
    //                                        semaphore.Release();
    //                                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zadanie dla ID {productStateToProcess.ProductId} zakończone. Scraper zwrócony. Slot zwolniony. Status: {productStateToProcess.Status}");
    //                                    }
    //                                }, linkedCtsForAttempt.Token);
    //                                activeScrapingTasks.Add(task);
    //                            }
    //                            else { semaphore.Release(); }
    //                        }
    //                        if (!taskStartedThisIteration && pendingProductIds.Any() && activeScrapingTasks.Any(t => !t.IsCompleted) && !linkedCtsForAttempt.Token.IsCancellationRequested)
    //                        { await Task.Delay(TimeSpan.FromMilliseconds(200), linkedCtsForAttempt.Token); }
    //                    }
    //                    activeScrapingTasks.RemoveAll(t => t.IsCompleted || t.IsCanceled || t.IsFaulted);

    //                    if (_currentCaptchaGlobalCts.IsCancellationRequested)
    //                    { captchaDetectedInCurrentAttempt = true; Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] CAPTCHA. Przerywam pętlę."); break; }
    //                    if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Globalne zatrzymanie. Przerywam pętlę."); break; }
    //                }

    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Główna pętla zakończona. Czekam na zadania...");
    //                try { await Task.WhenAll(activeScrapingTasks.ToArray()); } catch (OperationCanceledException) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Task.WhenAll: Zadania anulowane."); }
    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Wszystkie zadania dla tej próby zakończone.");
    //            }
    //            catch (OperationCanceledException)
    //            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Operacja ANULOWANA."); if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true; }
    //            catch (Exception ex)
    //            { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] BŁĄD KRYTYCZNY: {ex.Message}"); captchaDetectedInCurrentAttempt = false; finalMessage = $"Błąd krytyczny (sklep {storeId}): {ex.Message}"; break; }
    //            finally
    //            {
    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Finally dla próby...");

    //                // ### ZMIANA NR 2: Modyfikacja sposobu wykrywania CAPTCHA ###
    //                // Sprawdzamy nie tylko czy token został anulowany, ale też czy jakikolwiek produkt
    //                // został oznaczony jako zatrzymany przez CAPTCHA. Jest to potrzebne dla trybu ręcznego.
    //                bool captchaSignaledByToken = _currentCaptchaGlobalCts.IsCancellationRequested;
    //                bool captchaSignaledByProductStatus = _masterProductStateList.Values.Any(p => p.Status == ProductStatus.CaptchaHalt);
    //                captchaDetectedInCurrentAttempt = captchaSignaledByToken || captchaSignaledByProductStatus;

    //                if (captchaDetectedInCurrentAttempt && !captchaSignaledByToken)
    //                {
    //                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Wykryto CAPTCHA w trybie ręcznym (status produktu). Anuluję pozostałe zadania tej próby.");
    //                }

    //                // Anulujemy operacje tej próby, jeśli wykryto CAPTCHA w jakikolwiek sposób, aby zakończyć pozostałe wątki.
    //                if (captchaDetectedInCurrentAttempt && !_currentGlobalScrapingOperationCts.IsCancellationRequested)
    //                {
    //                    _currentGlobalScrapingOperationCts.Cancel();
    //                }

    //                var remainingTasks = activeScrapingTasks.Where(t => !t.IsCompleted).ToArray();
    //                if (remainingTasks.Any())
    //                {
    //                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Czekam na {remainingTasks.Length} zadań w finally próby...");
    //                    try { await Task.WhenAll(remainingTasks); } catch (OperationCanceledException) { }
    //                }

    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Końcowy zapis zmian...");
    //                await BatchUpdateDatabaseAsync(true, CancellationToken.None);


    //                _currentGlobalScrapingOperationCts?.Dispose();
    //                _currentCaptchaGlobalCts?.Dispose();
    //                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zakończono finally dla próby (przeglądarki wciąż otwarte).");
    //            }


    //            lastAttemptEndedDueToCaptcha = captchaDetectedInCurrentAttempt;

    //            if (lastAttemptEndedDueToCaptcha)
    //            {
    //                restartAttempt++;
    //                if (restartAttempt <= MAX_RESTARTS_ON_CAPTCHA)
    //                {
    //                    if (allowManualCaptchaSolving)
    //                    {
    //                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] !!!!!!!!!!!! CAPTCHA W TRYBIE RĘCZNYM !!!!!!!!!!!!");
    //                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Użytkowniku, masz 30 sekund na rozwiązanie CAPTCHA w otwartym oknie przeglądarki.");
    //                        await Task.Delay(TimeSpan.FromSeconds(30));
    //                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Czas na rozwiązanie CAPTCHA minął. Kontynuuję scrapowanie.");
    //                    }
    //                    else
    //                    {
    //                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CAPTCHA. Próba {restartAttempt - 1} nieudana. Reset sieci...");
    //                        bool networkResetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
    //                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Reset sieci: {(networkResetSuccess ? "OK" : "FAIL")}.");
    //                        if (!networkResetSuccess) { finalMessage = $"Reset sieci FAIL po CAPTCHA. Stop po {restartAttempt - 1} próbach."; break; }
    //                    }
    //                }
    //                else { finalMessage = $"MAX ({MAX_RESTARTS_ON_CAPTCHA}) restartów po CAPTCHA. Stop."; break; }
    //            }
    //            else if (overallOperationSuccess) { finalMessage = $"Sklep {storeId} OK."; break; }
    //            else if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { finalMessage = $"Sklep {storeId} STOP (sygnał)."; break; }

    //            // ### KROK 2: DODAJEMY ZAMYKANIE PRZEGLĄDAREK TUTAJ ###
    //            // Ten kod wykona się po każdej próbie, niezależnie od jej wyniku (CAPTCHA, sukces, błąd),
    //            // ale PO obsłudze 30-sekundowego opóźnienia.
    //            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sprzątanie po próbie {restartAttempt - 1}: Zamykanie {scraperInstancesForThisAttempt.Count} instancji scrapera.");
    //            foreach (var sc in scraperInstancesForThisAttempt) { await sc.CloseBrowserAsync(); }
    //            scraperInstancesForThisAttempt.Clear();

    //        } while (lastAttemptEndedDueToCaptcha && restartAttempt <= MAX_RESTARTS_ON_CAPTCHA);
    //    }
    //    finally
    //    {
    //        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] OSTATECZNE finally...");
    //        lock (_lockTimer)
    //        {
    //            if (_batchSaveTimer != null) { _batchSaveTimer.Dispose(); _batchSaveTimer = null; Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Globalny timer Zatrzymany."); }
    //        }
    //        _isScrapingActive = false;
    //        _currentGlobalScrapingOperationCts?.Dispose(); _currentCaptchaGlobalCts?.Dispose();
    //        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Flaga _isScrapingActive=false. Gotowy na nowe żądanie.");
    //    }
    //    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ostateczny status: {finalMessage}");
    //    return Content(finalMessage);

    //}









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
                // =================================================================
                // #region TRYB RĘCZNY (jedno uruchomienie, bez restartów)
                // =================================================================

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
                            // Ponownie pobieramy listę, bo mogła się zmienić w trakcie oczekiwania na semafor
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
                // #endregion
            }
            else
            {
                // =================================================================
                // #region TRYB AUTOMATYCZNY (z pętlą do-while i restartami)
                // =================================================================

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
                // #endregion
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
            // Te tokeny mogły już zostać usunięte w pętli, ale `Dispose` jest idempotentne
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

        var cidResult = await scraper.SearchInitialProductCIDsAsync(searchTermBase, maxCIDsToExtract: 1);

        if (cidResult.CaptchaEncountered)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] CAPTCHA! Zatrzymuję operację.");
            if (!cts.IsCancellationRequested) cts.Cancel();
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
            return;
        }

        if (cidResult.IsSuccess && cidResult.Data.Any())
        {
            var firstCid = cidResult.Data.First();
            var googleUrl = $"https://www.google.com/shopping/product/{firstCid}";
            lock (productState)
            {
                productState.UpdateStatus(ProductStatus.Found, googleUrl, firstCid);
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Szybki] ✓ Znaleziono dla ID {productState.ProductId}. CID: {firstCid}");
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

    private async Task<IActionResult> StartScrapingForProducts_IntermediateMatchAsync(int storeId, int numberOfConcurrentScrapers, SearchTermSource searchTermSource, int maxCidsToProcess)
    {
        if (_isScrapingActive)
        {
            return Conflict("Proces scrapowania jest już globalnie aktywny.");
        }
        _isScrapingActive = true;

        int restartAttempt = 0;
        const int MAX_RESTARTS_ON_CAPTCHA = 100;
        bool overallOperationSuccess = false;
        string finalMessage = $"Proces scrapowania (Tryb Pośredni) dla sklepu {storeId} zainicjowany.";
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
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Utworzono i zainicjowano {scraperInstancesForThisAttempt.Count} instancji scrapera do puli.");

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
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Brak produktów i zadań. Kończę próbę.");
                                overallOperationSuccess = true;
                                break;
                            }
                        }
                        else
                        {
                            await semaphore.WaitAsync(linkedCtsForAttempt.Token);
                            if (linkedCtsForAttempt.Token.IsCancellationRequested) { semaphore.Release(); break; }

                            int selectedProductId;
                            lock (_lockMasterListInit)
                            {
                                if (!pendingProductIds.Any()) { semaphore.Release(); continue; }
                                int randomIndex = _random.Next(pendingProductIds.Count);
                                selectedProductId = pendingProductIds[randomIndex];
                            }

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
                                                maxCidsToProcess
                                            );
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BŁĄD w zadaniu dla ID {productStateToProcess.ProductId}: {ex.Message}");
                                            lock (productStateToProcess) { productStateToProcess.UpdateStatus(ProductStatus.Error); }
                                        }
                                        finally
                                        {
                                            lock (productStateToProcess)
                                            {
                                                productStateToProcess.ProcessingByTaskId = null;
                                                if (productStateToProcess.Status == ProductStatus.Processing) { productStateToProcess.UpdateStatus(ProductStatus.Pending); }
                                            }
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

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Główna pętla zakończona. Czekam na {activeScrapingTasks.Count(t => !t.IsCompleted)} zadań...");
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
                    captchaDetectedInCurrentAttempt = false;
                    finalMessage = $"Błąd krytyczny (sklep {storeId}): {ex.Message}";
                    break;
                }
                finally
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Finally dla próby...");
                    if (_currentCaptchaGlobalCts.IsCancellationRequested) captchaDetectedInCurrentAttempt = true;

                    if (captchaDetectedInCurrentAttempt && !_currentGlobalScrapingOperationCts.IsCancellationRequested)
                    {
                        _currentGlobalScrapingOperationCts.Cancel();
                    }

                    await BatchUpdateDatabaseAsync(true, CancellationToken.None);

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Próba {restartAttempt}] Zamykanie {scraperInstancesForThisAttempt.Count} instancji scrapera dla tej próby...");
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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CAPTCHA. Próba {restartAttempt - 1} nieudana. Reset sieci...");
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
                else if (_currentGlobalScrapingOperationCts.IsCancellationRequested) { finalMessage = $"Sklep {storeId} (Tryb Pośredni) zatrzymany przez użytkownika."; break; }

            } while (lastAttemptEndedDueToCaptcha && restartAttempt <= MAX_RESTARTS_ON_CAPTCHA);
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Flaga _isScrapingActive ustawiona na false. Gotowy na nowe żądanie.");
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ostateczny status (Tryb Pośredni): {finalMessage}");
        return Content(finalMessage);
    }

    private async Task ProcessSingleProduct_IntermediateMatchAsync(ProductProcessingState productState, GoogleScraper scraper, SearchTermSource termSource, CancellationTokenSource cts, int maxCidsToExtract)
    {
        if (cts.IsCancellationRequested) return;

        string searchTermForGoogle;
        string comparisonTerm = productState.ProducerCode;

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
            lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Brak terminu do wyszukania (np. nazwy lub kodu)"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✗ Błąd dla ID {productState.ProductId}: Brak terminu do wyszukania.");
            return;
        }

        if (string.IsNullOrWhiteSpace(comparisonTerm))
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.Error, "Brak kodu producenta do weryfikacji tytułu"); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✗ Błąd dla ID {productState.ProductId}: Brak kodu producenta do weryfikacji.");
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] Przetwarzam ID: {productState.ProductId}. Szukam w Google: '{searchTermForGoogle}', Weryfikuję w tytule: '{comparisonTerm}'");

        var cidsResult = await scraper.SearchInitialProductCIDsAsync(searchTermForGoogle, maxCIDsToExtract: maxCidsToExtract);

        if (cts.IsCancellationRequested || cidsResult.CaptchaEncountered)
        {
            if (cidsResult.CaptchaEncountered && !cts.IsCancellationRequested) cts.Cancel();
            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
            return;
        }

        if (!cidsResult.IsSuccess || !cidsResult.Data.Any())
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.NotFound); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✗ Nie znaleziono żadnych wyników dla '{searchTermForGoogle}'.");
            return;
        }

        string foundCid = null;
        string cleanedComparisonTerm = comparisonTerm.Replace(" ", "").Trim();

        foreach (var cid in cidsResult.Data)
        {
            if (cts.IsCancellationRequested) break;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] Weryfikuję CID: {cid} dla produktu ID: {productState.ProductId}");
            var titleResult = await scraper.GetTitleFromProductPageAsync(cid);

            if (titleResult.CaptchaEncountered)
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
                lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                return;
            }

            if (titleResult.IsSuccess && !string.IsNullOrEmpty(titleResult.Data))
            {
                string cleanedTitle = titleResult.Data.Replace(" ", "").Trim();

                if (cleanedTitle.Contains(cleanedComparisonTerm, StringComparison.OrdinalIgnoreCase))
                {
                    foundCid = cid;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✓ ZNALEZIONO DOPASOWANIE. Tytuł: '{titleResult.Data}' zawiera kod '{comparisonTerm}'. CID: {foundCid}");
                    break;
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] - Brak dopasowania. Tytuł '{titleResult.Data}' nie zawiera kodu '{comparisonTerm}'.");
                }
            }
        }

        if (foundCid != null)
        {
            var googleUrl = $"https://www.google.com/shopping/product/{foundCid}";
            lock (productState)
            {
                productState.UpdateStatus(ProductStatus.Found, googleUrl, foundCid);
            }
        }
        else
        {
            lock (productState) { productState.UpdateStatus(ProductStatus.NotFound); }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Tryb Pośredni] ✗ Nie znaleziono dopasowania w żadnym z {cidsResult.Data.Count} sprawdzonych CID-ów.");
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
    string namePrefix,
    bool allowManualCaptchaSolving)
    {
        // W trybie ręcznym ignorujemy początkowe anulowanie, bo zadania mogą startować z opóźnieniem
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
        // Etykieta, do której wracamy, jeśli CAPTCHA pojawi się w trakcie przetwarzania ofert
        RestartProductProcessing:

            // Pętla do ponawiania operacji po ręcznym rozwiązaniu CAPTCHA
            bool cidSearchCompleted = false;
            ScraperResult<List<string>> cidResult = null;

            while (!cidSearchCompleted)
            {
                // W trybie automatycznym sprawdzamy token na początku każdej próby
                if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving)
                {
                    lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                    return;
                }

                cidResult = await scraper.SearchInitialProductCIDsAsync(searchTermBase, maxCidsToSearch);

                if (cidResult.CaptchaEncountered)
                {
                    if (allowManualCaptchaSolving)
                    {
                        bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                        if (solved)
                        {
                            // CAPTCHA rozwiązana, ponów próbę wyszukania CIDów
                            continue;
                        }
                        else
                        {
                            // Timeout, oznacz produkt jako błąd i zakończ
                            lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
                            return;
                        }
                    }
                    else // Tryb automatyczny
                    {
                        if (!captchaCts.IsCancellationRequested) captchaCts.Cancel();
                        lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                        return;
                    }
                }

                // Jeśli doszliśmy tutaj, wyszukiwanie CID zakończyło się (sukcesem lub porażką, ale bez CAPTCHA)
                cidSearchCompleted = true;
            }


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
                        .Where(p => (p.Status != ProductStatus.Found) && !string.IsNullOrEmpty(p.CleanedUrl))
                        .GroupBy(p => p.CleanedUrl).ToDictionary(g => g.Key, g => g.First());
                }

                bool initiatingProductDirectlyMatchedInThisTask = false;
                lock (productState) { if (productState.Status == ProductStatus.Found) initiatingProductDirectlyMatchedInThisTask = true; }

                foreach (var cid in initialCIDs)
                {
                    if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving) break;

                    Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}: Przetwarzam CID: {cid}");

                    ScraperResult<List<string>> offersResult = await scraper.NavigateToProductPageAndExpandOffersAsync(cid);

                    if (offersResult.CaptchaEncountered)
                    {
                        if (allowManualCaptchaSolving)
                        {
                            bool solved = await HandleManualCaptchaAsync(scraper, TimeSpan.FromMinutes(5));
                            if (solved)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] CAPTCHA rozwiązana podczas sprawdzania ofert. Restartuję przetwarzanie dla produktu ID: {productState.ProductId}");
                                // Użycie goto do wyjścia z zagnieżdżonych pętli i rozpoczęcia całego procesu od nowa dla tego produktu
                                goto RestartProductProcessing;
                            }
                            else
                            {
                                lock (productState) { productState.UpdateStatus(ProductStatus.Error); }
                                return;
                            }
                        }
                        else // Tryb automatyczny
                        {
                            if (!captchaCts.IsCancellationRequested) captchaCts.Cancel();
                            lock (productState) { productState.UpdateStatus(ProductStatus.CaptchaHalt); }
                            return;
                        }
                    }

                    if (offersResult.IsSuccess && offersResult.Data.Any())
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}, CID {cid}: Znaleziono {offersResult.Data.Count} ofert.");
                        foreach (var cleanedOfferUrl in offersResult.Data)
                        {
                            if (localEligibleProductsMap.TryGetValue(cleanedOfferUrl, out var matchedState))
                            {
                                lock (matchedState)
                                {
                                    string googleProductPageUrl = $"https://www.google.com/shopping/product/{cid}";
                                    matchedState.UpdateStatus(ProductStatus.Found, googleProductPageUrl, cid);
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ✓ DOPASOWANO! {cleanedOfferUrl} → ID {matchedState.ProductId}. CID: {cid}");

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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] ID {productState.ProductId}, CID {cid}: Brak ofert lub błąd. Msg: {offersResult.ErrorMessage}");
                    }

                    if (captchaCts.IsCancellationRequested && !allowManualCaptchaSolving) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(200, 400)), CancellationToken.None);
                }

                // Oznacz produkt inicjujący jako "Nie znaleziony" tylko jeśli nie został dopasowany bezpośrednio
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
            // W bloku finally upewniamy się, że produkt nie utknie w stanie "Processing"
            lock (productState)
            {
                if (productState.Status == ProductStatus.Processing)
                {
                    // Jeśli jakimś cudem nie został ustawiony żaden inny status, wracamy do Pending
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
            // Sprawdzamy, czy strona nadal jest stroną CAPTCHA
            if (scraper.CurrentPage != null && !scraper.CurrentPage.Url.Contains("/sorry/") && !scraper.CurrentPage.Url.Contains("/captcha"))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Wygląda na to, że CAPTCHA została rozwiązana. Wznawiam pracę wątku.");
                return true; // Sukces, CAPTCHA rozwiązana
            }

            try
            {
                // Czekamy 10 sekund przed kolejnym sprawdzeniem
                await Task.Delay(TimeSpan.FromSeconds(10), Cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Przerwanie pętli z powodu timeoutu
                break;
            }
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{Thread.CurrentThread.ManagedThreadId}] Minął czas na rozwiązanie CAPTCHA. Wątek nie będzie kontynuowany.");
        return false; // Porażka, timeout
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

            var jsonProducts = products.Select(p => new
            {
                p.ProductId,
                p.ProductNameInStoreForGoogle,
                p.Url,
                p.FoundOnGoogle,
                p.GoogleUrl,
                p.Ean,
                p.ProducerCode
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

