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
        private readonly ControlXYService _controlXYService; // jeśli używasz

        // Konstruktor z wstrzykiwaniem potrzebnych zależności:
        public CeneoScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ControlXYService controlXYService // jeśli go używasz
        )
        {
            _context = context;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _controlXYService = controlXYService;
        }

        // Przykładowy typ wyliczeniowy - opisuje możliwe wyniki operacji
        public enum CeneoScrapingResult
        {
            Success,
            SettingsNotFound,
            NoUrlsFound,
            Error
        }

        // Opcjonalnie: klasa/rekord zwracająca dodatkowe informacje (np. liczba odrzuconych, liczba pobranych cen)
        public record CeneoScrapingDto(CeneoScrapingResult Result, int ScrapedCount, int RejectedCount, string? Message);

   
        public async Task<CeneoScrapingDto> StartScrapingWithCaptchaHandlingAsync(CancellationToken cancellationToken = default)
        {
            // 1) Pobranie settings z bazy
            var settings = await _context.Settings.FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                Console.WriteLine("Settings not found.");
                return new CeneoScrapingDto(
                    CeneoScrapingResult.SettingsNotFound,
                    0,
                    0,
                    "Settings not found in DB."
                );
            }

            // 2) Przykład: rozwiązywanie captchy
            var resolveCaptchaScraper = new ResolveCaptchaScraper();
            await resolveCaptchaScraper.InitializeNormalBrowserAsync();

            // Przejście przez stronę captcha
            await resolveCaptchaScraper.NavigateToCaptchaAsync();

            // Jeśli ControlXY włączone, czekamy i uruchamiamy
            if (settings.ControlXY)
            {
                // Komunikat do SignalR (odliczanie)
                await _hubContext.Clients.All.SendAsync("ReceiveControlXYCountdown", 10);
                await Task.Delay(TimeSpan.FromSeconds(9), cancellationToken);

                _controlXYService.StartControlXY();
            }

            await resolveCaptchaScraper.WaitAndNavigateToCeneoAsync();

            // Pobierz ciasteczka sesji "po captcha"
            var captchaSessionData = await resolveCaptchaScraper.GetSessionDataAsync();

            // Zamknij przeglądarkę testową od captcha
            await resolveCaptchaScraper.CloseBrowserAsync();

            // 3) Pobierz oferty CoOfrs, które nie są jeszcze IsScraped
            var coOfrs = await _context.CoOfrs
                .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
                .ToListAsync(cancellationToken);

            if (!coOfrs.Any())
            {
                Console.WriteLine("No URLs found to scrape.");
                return new CeneoScrapingDto(
                    CeneoScrapingResult.NoUrlsFound,
                    0,
                    0,
                    "No URLs found to scrape."
                );
            }

            var urls = coOfrs.Select(co => co.OfferUrl).ToList();
            var urlQueue = new Queue<string>(urls);

            // 4) Przygotowanie do scrapowania
            int captchaSpeed = settings.Semophore;
            bool getCeneoName = settings.GetCeneoName;
            int totalPrices = 0;
            int scrapedCount = 0;
            int rejectedCount = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var tasks = new List<Task>();

            // 5) Używamy semafora do ograniczenia liczby równoczesnych wątków
            using (var semaphore = new SemaphoreSlim(captchaSpeed))
            {
                for (int i = 0; i < captchaSpeed; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            var httpClient = _httpClientFactory.CreateClient();
                            var captchaScraper = new CaptchaScraper(httpClient);

                            await captchaScraper.InitializeBrowserAsync(settings);

                            // Ustawiamy cookies uzyskane po captcha
                            await captchaScraper.Page.SetCookieAsync(captchaSessionData.Cookies);

                            while (true)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Console.WriteLine("Scraping canceled (CenScraper).");
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
                                    // Szukamy pasującego CoOfr
                                    var coOfr = coOfrs.First(co => co.OfferUrl == url);

                                    // Nowy scope EF
                                    using var scope = _scopeFactory.CreateScope();
                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                    var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
                                        url, getCeneoName, coOfr.StoreNames, coOfr.StoreProfiles
                                    );

                                    Console.WriteLine(log);

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

                                        // Zapis ofert
                                        await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories, cancellationToken);

                                        // Aktualizacja coOfr
                                        coOfr.IsScraped = true;
                                        coOfr.PricesCount = priceHistories.Count;
                                        coOfr.IsRejected = false;
                                        scopedContext.CoOfrs.Update(coOfr);

                                        await scopedContext.SaveChangesAsync(cancellationToken);

                                        Interlocked.Add(ref totalPrices, priceHistories.Count);
                                    }
                                    else
                                    {
                                        // Brak ofert => odrzucone
                                        coOfr.IsScraped = true;
                                        coOfr.IsRejected = true;
                                        coOfr.PricesCount = 0;
                                        scopedContext.CoOfrs.Update(coOfr);
                                        await scopedContext.SaveChangesAsync(cancellationToken);

                                        Console.WriteLine($"No prices found for URL: {url}. Marked as rejected.");
                                        Interlocked.Increment(ref rejectedCount);
                                    }

                                    // Wysyłamy info przez SignalR
                                    Interlocked.Increment(ref scrapedCount);
                                    await _hubContext.Clients.All.SendAsync(
                                        "ReceiveScrapingUpdate",
                                        coOfr.OfferUrl,
                                        coOfr.IsScraped,
                                        coOfr.IsRejected,
                                        coOfr.PricesCount
                                    );

                                    await _hubContext.Clients.All.SendAsync(
                                        "ReceiveProgressUpdate",
                                        scrapedCount,
                                        coOfrs.Count,
                                        stopwatch.Elapsed.TotalSeconds,
                                        rejectedCount
                                    );
                                }
                                catch (Exception ex)
                                {
                                    var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                                    Console.WriteLine(log);
                                }
                            }

                            await captchaScraper.CloseBrowserAsync();
                        }
                        finally
                        {
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

            // 6) Zwracamy wynik do kodu wywołującego (bez Ok(), NotFound(), itp.)
            return new CeneoScrapingDto(
                CeneoScrapingResult.Success,
                scrapedCount,
                rejectedCount,
                "Scraping completed."
            );
        }
    }
}
