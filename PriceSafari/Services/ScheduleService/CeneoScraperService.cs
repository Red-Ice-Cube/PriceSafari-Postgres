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

        private bool _captchaDetected = false;

        private int _captchaResolutions = 0;

        private readonly List<CaptchaScraper> _activeScrapers = new();

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

        public enum CeneoScrapingResult
        {
            Success,
            SettingsNotFound,
            NoUrlsFound,
            Error
        }

        public record CeneoScrapingDto(
             CeneoScrapingResult Result,
             int ScrapedCount,
             int RejectedCount,
             int TotalUrlsToScrape,
             string? Message
         );

        public async Task<CeneoScrapingDto> StartScrapingWithCaptchaHandlingAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _context.Settings.FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                Console.WriteLine("Settings not found.");

                return new CeneoScrapingDto(CeneoScrapingResult.SettingsNotFound, 0, 0, 0, "Settings not found in DB.");
            }

            var coOfrs = await _context.CoOfrs
                .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
                .ToListAsync(cancellationToken);

            int totalUrls = coOfrs.Count;

            if (!coOfrs.Any())
            {
                Console.WriteLine("No URLs found to scrape.");

                return new CeneoScrapingDto(CeneoScrapingResult.NoUrlsFound, 0, 0, totalUrls, "No URLs found to scrape.");
            }

            int totalScraped = 0;
            int totalRejected = 0;
            _captchaDetected = false;
            _captchaResolutions = 0;

            for (int attempt = 1; attempt <= 5; attempt++)
            {

                _captchaResolutions++;

                var resolveCaptchaScraper = new ResolveCaptchaScraper();
                await resolveCaptchaScraper.InitializeNormalBrowserAsync();

                await resolveCaptchaScraper.NavigateToCaptchaAsync();

                if (settings.ControlXY)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveControlXYCountdown", 10);
                    await Task.Delay(TimeSpan.FromSeconds(9), cancellationToken);
                    _controlXYService.StartControlXY();
                }

                await resolveCaptchaScraper.WaitAndNavigateToCeneoAsync();

                var captchaSessionData = await resolveCaptchaScraper.GetSessionDataAsync();

                await resolveCaptchaScraper.CloseBrowserAsync();

                CloseAllBrowsers();

                _captchaDetected = false;
                var (scraped, rejected) = await ScrapeAllCoOfrsWithSemaphoreAsync(coOfrs, captchaSessionData, settings, cancellationToken);

                totalScraped += scraped;
                totalRejected += rejected;

                if (!_captchaDetected)
                {
                    break;
                }
                else
                {
                    Console.WriteLine($"Captcha detected in attempt {attempt}. Will retry...");
                }
            }

            if (_captchaDetected)
            {

                return new CeneoScrapingDto(
                    CeneoScrapingResult.Error,
                    totalScraped,
                    totalRejected,
                    totalUrls,
                    $"Too many captcha attempts after {_captchaResolutions} tries."
                );
            }
            else
            {
                var msg = $"Scraping completed. Captcha solved/attempted {_captchaResolutions} time(s).";

                return new CeneoScrapingDto(CeneoScrapingResult.Success, totalScraped, totalRejected, totalUrls, msg);
            }
        }

        private async Task<(int scrapedCount, int rejectedCount)> ScrapeAllCoOfrsWithSemaphoreAsync(
            List<CoOfrClass> coOfrs,
            CaptchaSessionData captchaSessionData,
            Settings settings,
            CancellationToken cancellationToken
        )
        {

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

                            lock (_activeScrapers)
                                _activeScrapers.Add(captchaScraper);

                            await captchaScraper.InitializeBrowserAsync(settings);

                            await captchaScraper.Page.SetCookieAsync(captchaSessionData.Cookies);

                            while (true)
                            {
                                if (_captchaDetected || cancellationToken.IsCancellationRequested)
                                {

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

                                    var coOfr = coOfrs.First(co => co.OfferUrl == url);

                                    var (prices, log, rejectedProducts) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
                                        url,
                                        settings.GetCeneoName,
                                        coOfr.StoreNames,
                                        coOfr.StoreProfiles
                                    );

                                    Console.WriteLine(log);

                                    var currentUrl = captchaScraper.Page.Url ?? "";
                                    if (currentUrl.Contains("/Captcha/Add", StringComparison.OrdinalIgnoreCase))
                                    {

                                        _captchaDetected = true;
                                        break;
                                    }

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

                    }
                }
                _activeScrapers.Clear();
            }
        }
    }
}