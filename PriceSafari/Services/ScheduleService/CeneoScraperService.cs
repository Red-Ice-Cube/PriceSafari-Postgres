using PriceSafari.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
using PriceSafari.Services.ControlXY;

namespace PriceSafari.Services.ScheduleService
{
    public class CeneoScraperService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ControlXYService _controlXYService;

        // ====================
        // Pola do obsługi Captchy:
        // ====================
        // flaga globalna – czy w trakcie scrapowania pojawiła się Captcha
        private bool _captchaDetected = false;

        // liczymy, ile razy (ile podejść) udało się/ musieliśmy rozwiązać Captchę
        private int _captchaResolutions = 0;

        // Trzymamy listę aktywnych scraperów, by móc je zamknąć
        private readonly List<CaptchaScraper> _activeScrapers = new();

        // Konstruktor:
        public CeneoScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ControlXYService controlXYService
        )
        {
            _context = context;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _controlXYService = controlXYService;
        }

        // Enum i DTO
        public enum CeneoScrapingResult
        {
            Success,
            SettingsNotFound,
            NoUrlsFound,
            Error
        }

        public record CeneoScrapingDto(CeneoScrapingResult Result, int ScrapedCount, int RejectedCount, string? Message);

        // ============================
        // Metoda "główna" StartScrapingWithCaptchaHandlingAsync
        // ============================
        public async Task<CeneoScrapingDto> StartScrapingWithCaptchaHandlingAsync(CancellationToken cancellationToken = default)
        {
            // 1) Pobieramy Settings z bazy
            var settings = await _context.Settings.FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                Console.WriteLine("Settings not found.");
                return new CeneoScrapingDto(CeneoScrapingResult.SettingsNotFound, 0, 0, "Settings not found in DB.");
            }

            // 2) Pobieramy CoOfrs (URL-e do scrapowania)
            var coOfrs = await _context.CoOfrs
                .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
                .ToListAsync(cancellationToken);
            if (!coOfrs.Any())
            {
                Console.WriteLine("No URLs found to scrape.");
                return new CeneoScrapingDto(CeneoScrapingResult.NoUrlsFound, 0, 0, "No URLs found to scrape.");
            }

            // Przygotowujemy liczniki
            int totalScraped = 0;
            int totalRejected = 0;
            _captchaDetected = false;
            _captchaResolutions = 0;

            // Narzucamy np. max 5 prób, żeby nie wpaść w pętlę nieskończoną
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                // Za każdym razem „rozwiązujemy” captchę – lub przynajmniej próbujemy
                _captchaResolutions++;

                // (A) Odpalamy ResolveCaptchaScraper (normalna przeglądarka)
                var resolveCaptchaScraper = new ResolveCaptchaScraper();
                await resolveCaptchaScraper.InitializeNormalBrowserAsync();

                // Przechodzimy stronę Captcha
                await resolveCaptchaScraper.NavigateToCaptchaAsync();

                // Jeśli ControlXY włączone, czekamy i uruchamiamy
                if (settings.ControlXY)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveControlXYCountdown", 10);
                    await Task.Delay(TimeSpan.FromSeconds(9), cancellationToken);
                    _controlXYService.StartControlXY();
                }

                // Dopiero teraz przechodzimy do ceneo
                await resolveCaptchaScraper.WaitAndNavigateToCeneoAsync();

                // Pobierz ciasteczka sesji "po captcha"
                var captchaSessionData = await resolveCaptchaScraper.GetSessionDataAsync();

                // Zamknij przeglądarkę testową
                await resolveCaptchaScraper.CloseBrowserAsync();

                // Upewnij się, że poprzednie scrapery są zamknięte, żeby nie zostały np. z poprzedniej próby
                CloseAllBrowsers();

                // (B) Właściwe scrapowanie w semaforze
                _captchaDetected = false; // flaga reset
                var (scraped, rejected) = await ScrapeAllCoOfrsWithSemaphoreAsync(coOfrs, captchaSessionData, settings, cancellationToken);

                // Zsumuj do globalnych
                totalScraped += scraped;
                totalRejected += rejected;

                // Jeśli w trakcie scrapowania nie wykryto captchy => koniec pętli
                if (!_captchaDetected)
                {
                    break;
                }
                else
                {
                    Console.WriteLine($"Captcha detected in attempt {attempt}. Will retry...");
                }
            }

            // Po pętli, jeśli _captchaDetected dalej jest true – to przekroczyliśmy liczbę prób
            if (_captchaDetected)
            {
                return new CeneoScrapingDto(
                    CeneoScrapingResult.Error,
                    totalScraped,
                    totalRejected,
                    $"Too many captcha attempts after {_captchaResolutions} tries."
                );
            }
            else
            {
                // Udało się
                var msg = $"Scraping completed. Captcha solved/attempted {_captchaResolutions} time(s).";
                return new CeneoScrapingDto(CeneoScrapingResult.Success, totalScraped, totalRejected, msg);
            }
        }

        // ============================
        // 2) Metoda do właściwego scrapowania w semaforze
        // ============================
        private async Task<(int scrapedCount, int rejectedCount)> ScrapeAllCoOfrsWithSemaphoreAsync(
            List<CoOfrClass> coOfrs,
            CaptchaSessionData captchaSessionData,
            Settings settings,
            CancellationToken cancellationToken
        )
        {
            // Tworzymy kolejkę URL-i
            var urls = coOfrs.Select(co => co.OfferUrl!).ToList();
            var urlQueue = new Queue<string>(urls);

            int scrapedCount = 0;
            int rejectedCount = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var tasks = new List<Task>();

            using (var semaphore = new SemaphoreSlim(settings.Semophore))
            {
                for (int i = 0; i < settings.Semophore; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        CaptchaScraper? captchaScraper = null;
                        try
                        {
                            var httpClient = _httpClientFactory.CreateClient();
                            captchaScraper = new CaptchaScraper(httpClient);

                            // Dodajemy do listy aktywnych scraperów
                            lock (_activeScrapers)
                                _activeScrapers.Add(captchaScraper);

                            // Uruchamiamy przeglądarkę
                            await captchaScraper.InitializeBrowserAsync(settings);

                            // Ustawiamy cookies
                            await captchaScraper.Page.SetCookieAsync(captchaSessionData.Cookies);

                            while (true)
                            {
                                if (_captchaDetected || cancellationToken.IsCancellationRequested)
                                {
                                    // Ktoś już wykrył captchę albo jest cancel
                                    break;
                                }

                                string url;
                                lock (urlQueue)
                                {
                                    if (urlQueue.Count == 0) break;
                                    url = urlQueue.Dequeue();
                                }

                                try
                                {
                                    // Znajdujemy CoOfr
                                    var coOfr = coOfrs.First(co => co.OfferUrl == url);

                                    // Właściwe scrapowanie:
                                    var (prices, log, rejectedProducts) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
                                        url,
                                        settings.GetCeneoName,
                                        coOfr.StoreNames,
                                        coOfr.StoreProfiles
                                    );

                                    Console.WriteLine(log);

                                    // Sprawdzamy, czy Puppeteer w międzyczasie nie jest na /Captcha/Add
                                    var currentUrl = captchaScraper.Page.Url ?? "";
                                    if (currentUrl.Contains("/Captcha/Add", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Wykryliśmy captchę w tym wątku => globalny sygnał
                                        _captchaDetected = true;
                                        break;
                                    }

                                    // Zapis do bazy:
                                    using var scope = _scopeFactory.CreateScope();
                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                    if (prices.Count > 0)
                                    {
                                        var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
                                        {
                                            CoOfrClassId = coOfr.Id,
                                            StoreName = priceData.storeName,
                                            Price = priceData.price,
                                            ShippingCostNum = priceData.shippingCostNum,
                                            AvailabilityNum = priceData.availabilityNum,
                                            IsBidding = priceData.isBidding,
                                            Position = priceData.position,
                                            ExportedName = priceData.ceneoName
                                        }).ToList();

                                        await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories, cancellationToken);

                                        coOfr.IsScraped = true;
                                        coOfr.PricesCount = priceHistories.Count;
                                        coOfr.IsRejected = false;
                                        scopedContext.CoOfrs.Update(coOfr);

                                        await scopedContext.SaveChangesAsync(cancellationToken);
                                    }
                                    else
                                    {
                                        coOfr.IsScraped = true;
                                        coOfr.IsRejected = true;
                                        coOfr.PricesCount = 0;
                                        scopedContext.CoOfrs.Update(coOfr);
                                        await scopedContext.SaveChangesAsync(cancellationToken);

                                        Console.WriteLine($"No prices found for URL: {url}. Marked as rejected.");
                                        Interlocked.Increment(ref rejectedCount);
                                    }

                                    Interlocked.Increment(ref scrapedCount);

                                    // Wysyłamy info do SignalR
                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.PricesCount);
                                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error scraping URL: {url}. Exception: {ex.Message}");
                                }
                            }
                        }
                        finally
                        {
                            // Zamykamy przeglądarkę w tym wątku
                            if (captchaScraper != null)
                            {
                                await captchaScraper.CloseBrowserAsync();
                                lock (_activeScrapers)
                                    _activeScrapers.Remove(captchaScraper);
                            }
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Scraping was canceled (operation canceled).");
                }
            }

            stopwatch.Stop();
            return (scrapedCount, rejectedCount);
        }

        // ============================
        // 3) Metoda zamykająca wszystkie przeglądarki
        // ============================
        private void CloseAllBrowsers()
        {
            lock (_activeScrapers)
            {
                foreach (var scraper in _activeScrapers)
                {
                    try
                    {
                        scraper.CloseBrowserAsync().Wait();
                    }
                    catch
                    {
                        // ignoruj błędy zamykania
                    }
                }
                _activeScrapers.Clear();
            }
        }
    }
}




//using PriceSafari.Data;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.AspNetCore.SignalR;
//using System.Diagnostics;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Scrapers;
//using PriceSafari.Services.ControlXY;

//namespace PriceSafari.Services.ScheduleService
//{

//    public class CeneoScraperService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly IHttpClientFactory _httpClientFactory;
//        private readonly ControlXYService _controlXYService; // jeśli używasz

//        // Konstruktor z wstrzykiwaniem potrzebnych zależności:
//        public CeneoScraperService(
//            PriceSafariContext context,
//            IHubContext<ScrapingHub> hubContext,
//            IServiceScopeFactory scopeFactory,
//            IHttpClientFactory httpClientFactory,
//            ControlXYService controlXYService // jeśli go używasz
//        )
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _scopeFactory = scopeFactory;
//            _httpClientFactory = httpClientFactory;
//            _controlXYService = controlXYService;
//        }

//        // Przykładowy typ wyliczeniowy - opisuje możliwe wyniki operacji
//        public enum CeneoScrapingResult
//        {
//            Success,
//            SettingsNotFound,
//            NoUrlsFound,
//            Error
//        }

//        // Opcjonalnie: klasa/rekord zwracająca dodatkowe informacje (np. liczba odrzuconych, liczba pobranych cen)
//        public record CeneoScrapingDto(CeneoScrapingResult Result, int ScrapedCount, int RejectedCount, string? Message);


//        public async Task<CeneoScrapingDto> StartScrapingWithCaptchaHandlingAsync(CancellationToken cancellationToken = default)
//        {
//            // 1) Pobranie settings z bazy
//            var settings = await _context.Settings.FirstOrDefaultAsync(cancellationToken);
//            if (settings == null)
//            {
//                Console.WriteLine("Settings not found.");
//                return new CeneoScrapingDto(
//                    CeneoScrapingResult.SettingsNotFound,
//                    0,
//                    0,
//                    "Settings not found in DB."
//                );
//            }

//            // 2) Przykład: rozwiązywanie captchy
//            var resolveCaptchaScraper = new ResolveCaptchaScraper();
//            await resolveCaptchaScraper.InitializeNormalBrowserAsync();

//            // Przejście przez stronę captcha
//            await resolveCaptchaScraper.NavigateToCaptchaAsync();

//            // Jeśli ControlXY włączone, czekamy i uruchamiamy
//            if (settings.ControlXY)
//            {
//                // Komunikat do SignalR (odliczanie)
//                await _hubContext.Clients.All.SendAsync("ReceiveControlXYCountdown", 10);
//                await Task.Delay(TimeSpan.FromSeconds(9), cancellationToken);

//                _controlXYService.StartControlXY();
//            }

//            await resolveCaptchaScraper.WaitAndNavigateToCeneoAsync();

//            // Pobierz ciasteczka sesji "po captcha"
//            var captchaSessionData = await resolveCaptchaScraper.GetSessionDataAsync();

//            // Zamknij przeglądarkę testową od captcha
//            await resolveCaptchaScraper.CloseBrowserAsync();

//            // 3) Pobierz oferty CoOfrs, które nie są jeszcze IsScraped
//            var coOfrs = await _context.CoOfrs
//                .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
//                .ToListAsync(cancellationToken);

//            if (!coOfrs.Any())
//            {
//                Console.WriteLine("No URLs found to scrape.");
//                return new CeneoScrapingDto(
//                    CeneoScrapingResult.NoUrlsFound,
//                    0,
//                    0,
//                    "No URLs found to scrape."
//                );
//            }

//            var urls = coOfrs.Select(co => co.OfferUrl).ToList();
//            var urlQueue = new Queue<string>(urls);

//            // 4) Przygotowanie do scrapowania
//            int captchaSpeed = settings.Semophore;
//            bool getCeneoName = settings.GetCeneoName;
//            int totalPrices = 0;
//            int scrapedCount = 0;
//            int rejectedCount = 0;
//            var stopwatch = new Stopwatch();
//            stopwatch.Start();

//            var tasks = new List<Task>();

//            // 5) Używamy semafora do ograniczenia liczby równoczesnych wątków
//            using (var semaphore = new SemaphoreSlim(captchaSpeed))
//            {
//                for (int i = 0; i < captchaSpeed; i++)
//                {
//                    tasks.Add(Task.Run(async () =>
//                    {
//                        await semaphore.WaitAsync(cancellationToken);
//                        try
//                        {
//                            var httpClient = _httpClientFactory.CreateClient();
//                            var captchaScraper = new CaptchaScraper(httpClient);

//                            await captchaScraper.InitializeBrowserAsync(settings);

//                            // Ustawiamy cookies uzyskane po captcha
//                            await captchaScraper.Page.SetCookieAsync(captchaSessionData.Cookies);

//                            while (true)
//                            {
//                                if (cancellationToken.IsCancellationRequested)
//                                {
//                                    Console.WriteLine("Scraping canceled (CenScraper).");
//                                    break;
//                                }

//                                string url;
//                                lock (urlQueue)
//                                {
//                                    if (urlQueue.Count == 0) break;
//                                    url = urlQueue.Dequeue();
//                                }

//                                try
//                                {
//                                    // Szukamy pasującego CoOfr
//                                    var coOfr = coOfrs.First(co => co.OfferUrl == url);

//                                    // Nowy scope EF
//                                    using var scope = _scopeFactory.CreateScope();
//                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                    var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
//                                        url, getCeneoName, coOfr.StoreNames, coOfr.StoreProfiles
//                                    );

//                                    Console.WriteLine(log);

//                                    if (prices.Count > 0)
//                                    {
//                                        var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
//                                        {
//                                            CoOfrClassId = coOfr.Id,
//                                            StoreName = priceData.storeName,
//                                            Price = priceData.price,
//                                            ShippingCostNum = priceData.shippingCostNum,
//                                            AvailabilityNum = priceData.availabilityNum,
//                                            IsBidding = priceData.isBidding,
//                                            Position = priceData.position,
//                                            ExportedName = priceData.ceneoName
//                                        }).ToList();

//                                        // Zapis ofert
//                                        await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories, cancellationToken);

//                                        // Aktualizacja coOfr
//                                        coOfr.IsScraped = true;
//                                        coOfr.PricesCount = priceHistories.Count;
//                                        coOfr.IsRejected = false;
//                                        scopedContext.CoOfrs.Update(coOfr);

//                                        await scopedContext.SaveChangesAsync(cancellationToken);

//                                        Interlocked.Add(ref totalPrices, priceHistories.Count);
//                                    }
//                                    else
//                                    {
//                                        // Brak ofert => odrzucone
//                                        coOfr.IsScraped = true;
//                                        coOfr.IsRejected = true;
//                                        coOfr.PricesCount = 0;
//                                        scopedContext.CoOfrs.Update(coOfr);
//                                        await scopedContext.SaveChangesAsync(cancellationToken);

//                                        Console.WriteLine($"No prices found for URL: {url}. Marked as rejected.");
//                                        Interlocked.Increment(ref rejectedCount);
//                                    }

//                                    // Wysyłamy info przez SignalR
//                                    Interlocked.Increment(ref scrapedCount);
//                                    await _hubContext.Clients.All.SendAsync(
//                                        "ReceiveScrapingUpdate",
//                                        coOfr.OfferUrl,
//                                        coOfr.IsScraped,
//                                        coOfr.IsRejected,
//                                        coOfr.PricesCount
//                                    );

//                                    await _hubContext.Clients.All.SendAsync(
//                                        "ReceiveProgressUpdate",
//                                        scrapedCount,
//                                        coOfrs.Count,
//                                        stopwatch.Elapsed.TotalSeconds,
//                                        rejectedCount
//                                    );
//                                }
//                                catch (Exception ex)
//                                {
//                                    var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                                    Console.WriteLine(log);
//                                }
//                            }

//                            await captchaScraper.CloseBrowserAsync();
//                        }
//                        finally
//                        {
//                            semaphore.Release();
//                        }
//                    }, cancellationToken));
//                }

//                try
//                {
//                    await Task.WhenAll(tasks);
//                }
//                catch (OperationCanceledException)
//                {
//                    Console.WriteLine("Scraping was canceled (operation canceled).");
//                }
//            }

//            stopwatch.Stop();

//            // 6) Zwracamy wynik do kodu wywołującego (bez Ok(), NotFound(), itp.)
//            return new CeneoScrapingDto(
//                CeneoScrapingResult.Success,
//                scrapedCount,
//                rejectedCount,
//                "Scraping completed."
//            );
//        }
//    }
//}
