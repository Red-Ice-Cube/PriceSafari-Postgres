



using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using PriceSafari.Models;
using Microsoft.Extensions.DependencyInjection;
using PriceSafari.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PriceSafari.Services
{
    public static class GlobalCookieWarehouse
    {

        private const int MAX_COOKIES_IN_QUEUE = 200;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

        private static readonly BlockingCollection<CookieContainer> _cookieQueue = new(MAX_COOKIES_IN_QUEUE);

        private static CancellationTokenSource _generatorCts = new();
        private static bool _isStarted = false;
        private static readonly object _lock = new();

        private static bool _useHeadlessMode = true;

        public static int AvailableCookies => _cookieQueue.Count;

        public static void StartGenerators(int generatorCount, bool headless)
        {
            lock (_lock)
            {
                if (_isStarted) return;

                if (generatorCount < 1) generatorCount = 1;

                _useHeadlessMode = headless;

                _generatorCts = new CancellationTokenSource();
                _isStarted = true;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[WAREHOUSE] Uruchamiam {generatorCount} generatorów (Headless: {_useHeadlessMode})...");
                Console.ResetColor();

                for (int i = 0; i < generatorCount; i++)
                {
                    int botId = i + 1;
                    Task.Run(() => RunGeneratorLoop(botId, _generatorCts.Token));
                }
            }
        }

        public static void StopAndClear()
        {
            lock (_lock)
            {
                if (!_isStarted) return;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[WAREHOUSE] Zatrzymywanie generatorów i czyszczenie magazynu...");
                Console.ResetColor();

                _generatorCts.Cancel();

                while (_cookieQueue.TryTake(out _)) { }

                _isStarted = false;
            }
        }

        // <summary>

        // Metoda dla Scrapera HTTP - pobiera ciastko. Blokuje wątek, jeśli magazyn jest pusty.

        // </summary>

        public static CookieContainer TakeCookie(CancellationToken token)
        {
            try
            {

                return _cookieQueue.Take(token);
            }
            catch (OperationCanceledException)
            {
                throw;

            }
            catch (InvalidOperationException)
            {

                throw new Exception("Magazyn ciastek został zamknięty.");
            }
        }

        private static void RunGeneratorLoop(int id, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {

                    if (_cookieQueue.Count >= MAX_COOKIES_IN_QUEUE - 5)
                    {
                        Thread.Sleep(5000);
                        continue;
                    }

                    var container = GenerateSingleSession(id);

                    if (container != null && !token.IsCancellationRequested)
                    {
                        _cookieQueue.Add(container, token);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[GEN-{id}] +1 Sesja. Magazyn: {_cookieQueue.Count}");
                        Console.ResetColor();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GEN-{id} ERROR] {ex.Message}. Restartuję generator...");
                    Thread.Sleep(5000);
                }
            }
        }

        private static CookieContainer? GenerateSingleSession(int botId)
        {
            var options = new ChromeOptions();

            if (_useHeadlessMode)
            {
                options.AddArgument("--headless=new");
            }

            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument($"user-agent={UserAgent}");
            options.AddArgument("--log-level=3");

            var searchQueries = new[]
            {
                "iphone 15 pro", "samsung galaxy s24", "laptop dell", "karta graficzna rtx 4060",
                "buty nike air max", "adidas ultraboost", "kurtka the north face", "plecak vans",
                "ekspres do kawy delonghi", "odkurzacz dyson v15", "robot sprzątający roborock",
                "klocki lego star wars", "konsola ps5 slim", "pad xbox series x", "nintendo switch",
                "wiertarka wkrętarka makita", "zestaw kluczy yato", "kosiarka spalinowa",
                "rower górski kross", "namiot 4 osobowy", "buty trekkingowe salomon"
            };

            var randomQuery = searchQueries[new Random().Next(searchQueries.Length)].Replace(" ", "+");

            try
            {
                using (var driver = new ChromeDriver(options))
                {

                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(40);

                    string targetUrl = $"https://www.google.com/search?q={randomQuery}&tbm=shop";
                    driver.Navigate().GoToUrl(targetUrl);

                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                    try
                    {
                        var consentButton = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'QS5gu') and (contains(., 'Zaakceptuj') or contains(., 'Odrzuć'))]")));
                        Thread.Sleep(new Random().Next(200, 500));
                        consentButton.Click();
                    }
                    catch { }

                    Thread.Sleep(500);

                    try
                    {
                        var productCard = wait.Until(d => d.FindElement(By.CssSelector("div.njFjte[role='button']")));
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", productCard);
                        Thread.Sleep(500);
                        productCard.Click();
                        Thread.Sleep(2500);
                    }
                    catch { }

                    var cookies = driver.Manage().Cookies.AllCookies;
                    var cookieContainer = new CookieContainer();
                    foreach (var c in cookies)
                    {
                        try { cookieContainer.Add(new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain)); } catch { }
                    }
                    return cookieContainer;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public static class ResultBatchProcessor
    {
        private static readonly ConcurrentQueue<CoOfrPriceHistoryClass> _resultsQueue = new();
        private static readonly CancellationTokenSource _cts = new();
        private static Task? _processorTask;
        private static IServiceScopeFactory _scopeFactory;

        private const int FLUSH_INTERVAL_MS = 3000;

        public static void Initialize(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            if (_processorTask != null) return;

            _processorTask = Task.Run(async () =>
            {
                Console.WriteLine("[BATCH] Startuje asynchroniczny magazynier wyników...");
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(FLUSH_INTERVAL_MS, _cts.Token);
                        await FlushQueueAsync();
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BATCH ERROR] Main Loop: {ex.Message}");
                    }
                }
            });
        }

        public static void Enqueue(IEnumerable<CoOfrPriceHistoryClass> items)
        {
            foreach (var item in items) _resultsQueue.Enqueue(item);
        }

        public static async Task StopAndFlushAsync()
        {
            _cts.Cancel();
            await FlushQueueAsync();

            Console.WriteLine("[BATCH] Zatrzymano procesor.");
        }

        private static async Task FlushQueueAsync()
        {
            if (_resultsQueue.IsEmpty) return;

            var batch = new List<CoOfrPriceHistoryClass>();

            while (_resultsQueue.TryDequeue(out var item) && batch.Count < 500)
            {
                batch.Add(item);
            }

            if (batch.Any())
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                        await db.CoOfrPriceHistories.AddRangeAsync(batch);
                        await db.SaveChangesAsync();
                        Console.WriteLine($"[BATCH] Zapisano paczkę: {batch.Count} rekordów.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BATCH CRITICAL ERROR] Nie udało się zapisać {batch.Count} rekordów: {ex.Message}");
                }
            }
        }
    }

    public record TempOffer(
        string Seller,
        string Price,
        string Url,
        string? Delivery,
        bool IsInStock,
        string? Badge,
        int OriginalIndex,
        string Method = "OLD",
        string? RatingScore = null,
        string? RatingCount = null,
        string Condition = "NOWY",
        string Currency = "PLN"
    );

    public class GoogleMainPriceScraper
    {
        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

        private HttpClient _httpClient;
        private int _requestsOnCurrentIdentity = 0;

        public GoogleMainPriceScraper()
        {
            LoadNewIdentityFromWarehouse();
        }

        private void LoadNewIdentityFromWarehouse()
        {
            try
            {
                Console.WriteLine("[SCRAPER] Pobieram nowe ciastka z globalnego magazynu...");

                var cookieContainer = GlobalCookieWarehouse.TakeCookie(CancellationToken.None);

                _requestsOnCurrentIdentity = 0;
                var handler = new HttpClientHandler
                {
                    CookieContainer = cookieContainer,
                    UseCookies = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
                _httpClient = new HttpClient(handler);
                _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not A(Brand\";v=\"99\", \"Google Chrome\";v=\"121\", \"Chromium\";v=\"121\"");
                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SCRAPER] Błąd podczas pobierania ciastek: {ex.Message}");

                throw;
            }
        }

        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
        {
            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
            var allFoundOffers = new List<TempOffer>();

            Console.WriteLine($"\n[INFO] Start scrapowania dla ID: {coOfr.Id}...");

            string urlTemplate;
            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
            {
                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
            }
            else
            {
                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);
                if (string.IsNullOrEmpty(catalogId))
                {
                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
                    return finalPriceHistory;
                }
                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
                else
                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
            }

            string? firstPageRawResponse = null;
            int startIndex = 0;
            const int pageSize = 10;
            int lastFetchCount;
            const int maxRetries = 3;

            do
            {
                string currentUrl = string.Format(urlTemplate, startIndex);
                List<TempOffer> newOffers = new List<TempOffer>();
                string rawResponse = "";

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"\n[DEBUG HTTP] GET OAPV (Start: {startIndex}, Magazyn: {GlobalCookieWarehouse.AvailableCookies}):");
                        Console.ResetColor();

                        _requestsOnCurrentIdentity++;

                        rawResponse = await _httpClient.GetStringAsync(currentUrl);

                        if (rawResponse.Length < 100 && rawResponse.Contains("ProductDetailsResult\":[]"))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[BLOKADA] Pusta odpowiedź! Wymieniam tożsamość...");
                            Console.ResetColor();

                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine($"[STATS] Ciastko spalone po {_requestsOnCurrentIdentity} requestach.");
                            Console.ResetColor();

                            LoadNewIdentityFromWarehouse();
                            continue;

                        }

                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);
                        if (newOffers.Count > 0) break;

                        if (attempt < maxRetries) await Task.Delay(1500);
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"[ERROR HTTP] {ex.Message}. Biorę nowe ciastka.");
                        LoadNewIdentityFromWarehouse();
                        if (attempt < maxRetries) await Task.Delay(2000);
                    }
                }

                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse)) firstPageRawResponse = rawResponse;
                lastFetchCount = newOffers.Count;
                Console.WriteLine($"   [INFO] Znaleziono ofert na stronie: {lastFetchCount}");

                foreach (var offer in newOffers)
                {
                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url)) allFoundOffers.Add(offerWithIndex);
                }

                startIndex += pageSize;
                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(5, 15));

            } while (lastFetchCount == pageSize);

            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse))
            {
                decimal baselinePrice = 0;
                if (allFoundOffers.Any())
                {
                    var prices = allFoundOffers.Where(o => o.Currency == "PLN").Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
                    if (prices.Any()) baselinePrice = prices.OrderBy(p => p).ToList()[prices.Count / 2];
                }

                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);
                if (!string.IsNullOrEmpty(productTitle))
                {
                    string encodedQ = Uri.EscapeDataString(productTitle);
                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

                    try
                    {
                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
                        if (wrgaResponse.Length > 100)
                        {
                            var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);
                            foreach (var off in wrgaOffers)
                            {
                                if (baselinePrice > 0 && off.Currency == "PLN")
                                {
                                    decimal wrgaPrice = ParsePrice(off.Price);
                                    decimal diff = wrgaPrice - baselinePrice;
                                    decimal percentageDiff = diff / baselinePrice;
                                    if (percentageDiff < -0.8m || percentageDiff > 2.0m) continue;
                                }
                                if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
                                {
                                    allFoundOffers.Add(off with { OriginalIndex = allFoundOffers.Count + 1 });
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
            var debugMainList = new List<dynamic>();
            var debugAdditionalList = new List<dynamic>();

            foreach (var group in groupedBySeller)
            {
                var bestValidOffer = group.Where(o => o.Condition == "NOWY" && o.Currency == "PLN").OrderBy(o => ParsePrice(o.Price)).FirstOrDefault();
                var sortedStoreOffers = group.OrderBy(o => ParsePrice(o.Price)).ToList();

                foreach (var offer in sortedStoreOffers)
                {
                    bool isBest = (bestValidOffer != null && offer == bestValidOffer);
                    if (isBest)
                    {
                        string? isBiddingValue = null;
                        if (!string.IsNullOrEmpty(offer.Badge))
                        {
                            string b = offer.Badge.ToLower();
                            if (b.Contains("cena")) isBiddingValue = "bpg";
                            else if (b.Contains("popularn") || b.Contains("wybór")) isBiddingValue = "hpg";
                        }

                        finalPriceHistory.Add(new CoOfrPriceHistoryClass
                        {
                            CoOfrClassId = coOfr.Id,
                            GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl),
                            GoogleStoreName = offer.Seller,
                            GooglePrice = ParsePrice(offer.Price),
                            GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
                            GooglePosition = offer.OriginalIndex.ToString(),
                            IsBidding = isBiddingValue,
                            GoogleInStock = offer.IsInStock,
                            GoogleOfferPerStoreCount = group.Count()
                        });
                    }

                    var debugItem = new
                    {
                        Pos = isBest ? offer.OriginalIndex.ToString() : "-",
                        GPos = offer.OriginalIndex,
                        Stock = offer.IsInStock ? "OK" : "BRAK",
                        Cond = offer.Condition,
                        Curr = offer.Currency,
                        Info = offer.Badge ?? "-",
                        Method = offer.Method,
                        Seller = offer.Seller,
                        Price = ParsePrice(offer.Price),
                        Del = ParseDeliveryPrice(offer.Delivery),
                        Total = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
                        Url = offer.Url,
                        IsMain = isBest
                    };
                    if (isBest) debugMainList.Add(debugItem); else debugAdditionalList.Add(debugItem);
                }
            }

            finalPriceHistory = finalPriceHistory.OrderBy(x => x.GooglePrice).ToList();
            debugMainList = debugMainList.OrderBy(x => x.Price).ToList();

            for (int i = 0; i < debugMainList.Count; i++)
            {
                var old = debugMainList[i];
                debugMainList[i] = new { old.Pos, old.GPos, old.Stock, old.Cond, old.Curr, old.Info, old.Method, old.Seller, old.Price, old.Del, old.Total, old.Url, old.IsMain, ListPos = (i + 1).ToString() };
            }
            debugAdditionalList = debugAdditionalList.OrderBy(x => x.Seller).ThenBy(x => x.Price).ToList();

            Console.WriteLine("\n===============================================================================================================================================================================");
            Console.WriteLine($" TABELA GŁÓWNA (ID: {coOfr.Id}) - Najlepsze: {debugMainList.Count}");
            Console.WriteLine("===============================================================================================================================================================================");
            Console.WriteLine($"{"Poz.",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
            foreach (var item in debugMainList) PrintDebugRow(item, item.ListPos);

            if (debugAdditionalList.Any())
            {
                Console.WriteLine("\n--- DODATKOWE ---");
                foreach (var item in debugAdditionalList) PrintDebugRow(item, "-");
            }

            return finalPriceHistory;
        }

        #region Helper Methods
        private void PrintDebugRow(dynamic item, string posLabel)
        {
            string infoCode = item.Info;
            if (infoCode.Length > 15) infoCode = infoCode.Substring(0, 12) + "...";
            string seller = item.Seller;
            if (seller.Length > 20) seller = seller.Substring(0, 17) + "...";

            Console.Write($"{posLabel,-4} | {item.GPos,-5} | {item.Stock,-4} | ");

            if (item.Cond.Contains("UŻYW") || item.Cond.Contains("OUTLET")) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{item.Cond,-6}");
            Console.ResetColor();
            Console.Write(" | ");

            if (item.Curr != "PLN") Console.ForegroundColor = ConsoleColor.Magenta;
            else Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"{item.Curr,-6}");
            Console.ResetColor();

            Console.WriteLine($" | {infoCode,-15} | {item.Method,-6} | {seller,-20} | {item.Price,-10} | {item.Del,-9} | {item.Total,-10} | {item.Url}");
        }

        private bool AreUrlsEqual(string url1, string url2)
        {
            if (string.IsNullOrEmpty(url1) || string.IsNullOrEmpty(url2)) return false;
            string u1 = url1.Contains("?") ? url1.Split('?')[0] : url1;
            string u2 = url2.Contains("?") ? url2.Split('?')[0] : url2;
            string norm1 = u1.ToLower().Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
            string norm2 = u2.ToLower().Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
            return norm1 == norm2;
        }

        private string? ExtractProductId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = Regex.Match(url, @"product/(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private decimal ParsePrice(string priceText)
        {
            if (string.IsNullOrWhiteSpace(priceText)) return 0;
            var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".");
            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result)) return result;
            return 0;
        }

        private decimal ParseDeliveryPrice(string? deliveryText)
        {
            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa")) return 0;
            return ParsePrice(deliveryText);
        }
        #endregion
    }


    public static class GoogleShoppingApiParser
    {
        public static List<TempOffer> ParseWrga(string rawResponse)
        {
            var offers = new List<TempOffer>();
            if (string.IsNullOrWhiteSpace(rawResponse)) return offers;
            try
            {
                string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
                string decodedContent = Regex.Unescape(cleaned);
                decodedContent = WebUtility.HtmlDecode(decodedContent);
                var blockRegex = new Regex(@"<div[^>]*class=""[^""]*tF2Cxc[^""]*""[^>]*>([\s\S]*?)(?=<div[^>]*class=""[^""]*tF2Cxc|$)", RegexOptions.IgnoreCase);
                var blockMatches = blockRegex.Matches(decodedContent);
                int offerIndex = 0;
                foreach (Match blockMatch in blockMatches)
                {
                    offerIndex++;
                    string block = blockMatch.Groups[1].Value;
                    var urlMatch = Regex.Match(block, @"href=""(https?://[^""]+)""");
                    if (!urlMatch.Success) continue;
                    string url = urlMatch.Groups[1].Value;
                    if (IsGoogleLink(url)) continue;
                    string seller = GetDomainName(url);
                    decimal priceVal = 0;
                    decimal deliveryVal = 0;
                    string badge = "ORGANIC";
                    string condition = "NOWY";
                    string blockLower = block.ToLower();
                    var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
                    if (usedKeywords.Any(k => blockLower.Contains(k))) condition = "UŻYWANY";
                    string currency = "PLN";
                    if ((blockLower.Contains("eur") || blockLower.Contains("€")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "EUR";
                    else if ((blockLower.Contains("usd") || blockLower.Contains("$")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "USD";
                    priceVal = ExtractRichSnippetPrice(block);
                    if (priceVal > 0) { deliveryVal = ExtractDeliveryFromRichSnippet(block); badge = "RICH_SNIPPET"; }
                    else { string cleanText = StripHtml(block); var analysisResult = AnalyzePricesInBlock(cleanText); priceVal = analysisResult.Price; deliveryVal = analysisResult.Delivery; badge = "TEXT_ANALYSIS"; }
                    if (priceVal > 0)
                    {
                        offers.Add(new TempOffer(seller, priceVal.ToString("F2"), url, deliveryVal > 0 ? deliveryVal.ToString("F2") : "0", true, badge, offerIndex, "WRGA", null, null, condition, currency));
                    }
                }
            }
            catch { }
            return offers;
        }

        public static List<TempOffer> ParseOapv(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse)) return new List<TempOffer>();
            try
            {
                string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
                using JsonDocument doc = JsonDocument.Parse(cleanedJson);
                JsonElement root = doc.RootElement.Clone();
                var allOffers = new List<TempOffer>();
                FindAndParseAllOffers(root, root, allOffers);
                return allOffers;
            }
            catch (JsonException) { return new List<TempOffer>(); }
        }

        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                if (node.EnumerateArray().Any(IsPotentialSingleOffer))
                {
                    foreach (JsonElement potentialOffer in node.EnumerateArray())
                    {
                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url)) allOffers.Add(offer);
                    }
                }
            }
            if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) FindAndParseAllOffers(root, element, allOffers); }
            else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) FindAndParseAllOffers(root, property.Value, allOffers); }
        }

        private static bool IsPotentialSingleOffer(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Array) return false;
            int arrayChildren = 0; int primitiveChildren = 0;
            foreach (var child in node.EnumerateArray()) { if (child.ValueKind == JsonValueKind.Array) arrayChildren++; else if (child.ValueKind == JsonValueKind.String || child.ValueKind == JsonValueKind.Number || child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False) primitiveChildren++; }
            if (arrayChildren > 1 && primitiveChildren == 0) return false;
            JsonElement offerData = node;
            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array) offerData = node[0];
            if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;
            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google") && !s.Contains("gstatic"))) return true;
            return false;
        }

        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
        {
            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
            if (offerData.ValueKind != JsonValueKind.Array) return null;
            try
            {
                var allNodes = Flatten(offerData).ToList();
                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
                string? seller = null;
                if (!string.IsNullOrEmpty(url)) seller = GetDomainName(url);
                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
                {
                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };
                    foreach (var s in flatStrings) { if (!s.StartsWith("http") && s.Length > 2 && !blacklist.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) && !Regex.IsMatch(s, @"\d")) { seller = s; break; } }
                }
                if (seller == null && url != null) seller = GetDomainName(url);
                string condition = "NOWY";
                var usedKeywords = new[] { "pre-owned", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
                foreach (var text in flatStrings)
                {
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3) continue;
                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    string lowerText = text.ToLower();
                    if (lowerText.Contains("nie używany") || lowerText.Contains("nieużywany")) continue;
                    if (lowerText.Contains("nowy") && !lowerText.Contains("jak nowy")) continue;
                    foreach (var keyword in usedKeywords) { if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) { if (keyword == "używany" && (lowerText.Contains("fabrycznie nowy") || lowerText.Contains("produkt nowy"))) continue; condition = "UŻYWANY"; goto ConditionFound; } }
                }
            ConditionFound:;
                bool isInStock = true;
                bool hasPositiveStockText = flatStrings.Any(s => s.Contains("W magazynie", StringComparison.OrdinalIgnoreCase) || s.Contains("Dostępny", StringComparison.OrdinalIgnoreCase) || s.Equals("In stock", StringComparison.OrdinalIgnoreCase));
                if (hasPositiveStockText) isInStock = true;
                else { var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "wyprzedany", "chwilowy brak" }; if (flatStrings.Any(text => text.Length < 50 && outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false; }
                List<(decimal Amount, string Currency)> structuralPrices = new();
                for (int i = 1; i < allNodes.Count; i++)
                {
                    var current = allNodes[i];
                    if (current.ValueKind == JsonValueKind.String)
                    {
                        string currCode = current.GetString()?.ToUpper() ?? "";
                        if (currCode == "PLN" || currCode == "EUR" || currCode == "USD" || currCode == "GBP")
                        {
                            long micros = 0; bool foundPrice = false;
                            var prev = allNodes[i - 1];
                            if (prev.ValueKind == JsonValueKind.Number) { micros = prev.GetInt64(); foundPrice = true; }
                            else if (i >= 2) { var prevPrev = allNodes[i - 2]; if (prevPrev.ValueKind == JsonValueKind.Number) { micros = prevPrev.GetInt64(); foundPrice = true; } }
                            if (foundPrice && micros >= 1000000) { structuralPrices.Add((micros / 1000000m, currCode)); if (currCode == "PLN") break; }
                        }
                    }
                }
                bool hasTextualForeignEvidence = false; string foreignTextCurrency = "";
                var foreignRegex = new Regex(@"[\(\s](€|EUR|\$|USD)\s*(\d+[.,]?\d*)", RegexOptions.IgnoreCase);
                foreach (var s in flatStrings) { Match m = foreignRegex.Match(s); if (m.Success) { hasTextualForeignEvidence = true; string symbol = m.Groups[1].Value.ToUpper(); foreignTextCurrency = (symbol.Contains("EUR") || symbol.Contains("€")) ? "EUR" : "USD"; break; } }
                string? finalPrice = null; string finalCurrency = "PLN";
                var plnNode = structuralPrices.FirstOrDefault(x => x.Currency == "PLN");
                if (plnNode != default) { finalPrice = plnNode.Amount.ToString("F2", CultureInfo.InvariantCulture); if (hasTextualForeignEvidence) finalCurrency = foreignTextCurrency; else finalCurrency = "PLN"; }
                else if (structuralPrices.Any(x => x.Currency != "PLN")) { var foreign = structuralPrices.First(x => x.Currency != "PLN"); finalPrice = foreign.Amount.ToString("F2", CultureInfo.InvariantCulture); finalCurrency = foreign.Currency; }
                else return null;
                string? delivery = null;
                if (flatStrings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) || s.Contains("Bezpłatnie", StringComparison.OrdinalIgnoreCase) || s.Contains("Free delivery", StringComparison.OrdinalIgnoreCase))) delivery = "Bezpłatna";
                else
                {
                    var plusRegex = new Regex(@"^\+\s*(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
                    foreach (var s in flatStrings) { var match = plusRegex.Match(s.Trim()); if (match.Success) { delivery = ParsePriceDecimal(match.Groups[1].Value).ToString("F2"); break; } }
                    if (delivery == null)
                    {
                        var deliveryTextRegex = new Regex(@"(?:dostawa|wysyłka|delivery|shipping)(?:[^0-9]{0,30})(\d+[.,]\d{2})\s*(?:PLN|zł)|za\s+(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
                        foreach (var s in flatStrings)
                        {
                            if (!s.ToLower().Contains("dostaw") && !s.ToLower().Contains("wysyłk") && !s.ToLower().Contains("delivery") && !s.ToLower().Contains(" za ")) continue;
                            var match = deliveryTextRegex.Match(s);
                            if (match.Success) { string priceStr = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value; decimal delPrice = ParsePriceDecimal(priceStr); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } }
                        }
                    }
                    if (delivery == null) { for (int i = 0; i < allNodes.Count - 1; i++) { var node = allNodes[i]; if (node.ValueKind == JsonValueKind.Number) { try { long val = node.GetInt64(); if (val == 110720 && i > 0) { var prevNode = allNodes[i - 1]; if (prevNode.ValueKind == JsonValueKind.String) { string delText = prevNode.GetString()!; var priceMatch = Regex.Match(delText, @"(\d+[.,]\d{2})"); if (priceMatch.Success) { decimal delPrice = ParsePriceDecimal(priceMatch.Groups[1].Value); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } } } } } catch { } } } }
                }
                string? badge = ExtractBadgeStrict(offerData);
                if (string.IsNullOrEmpty(badge)) { string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }; badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase))); }
                if (!string.IsNullOrWhiteSpace(seller) && finalPrice != null && url != null) return new TempOffer(seller, finalPrice, url, delivery, isInStock, badge, 0, "OAPV", null, null, condition, finalCurrency);
            }
            catch { }
            return null;
        }

        public static string? ExtractProductTitle(string rawResponse)
        {
            try { string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse; using JsonDocument doc = JsonDocument.Parse(cleaned); if (doc.RootElement.TryGetProperty("ProductDetailsResult", out JsonElement pd) && pd.GetArrayLength() > 0) return pd[0].GetString(); } catch { }
            return null;
        }

        private static string GetDomainName(string url) { try { var host = new Uri(url).Host.ToLower().Replace("www.", ""); return char.ToUpper(host[0]) + host.Substring(1); } catch { return "Nieznany"; } }
        private static bool IsGoogleLink(string url) { return url.Contains(".google.") || url.Contains("gstatic.") || url.Contains("/search?") || url.Contains("youtube.") || url.Contains("googleusercontent") || url.Contains("translate.google"); }
        private static string? ExtractBadgeStrict(JsonElement offerData) { try { foreach (var element in offerData.EnumerateArray()) { if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0) { var inner = element[0]; if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0) { var potentialBadgeNode = inner[0]; if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1) { if (potentialBadgeNode[0].ValueKind == JsonValueKind.String) { string text = potentialBadgeNode[0].GetString()!; if (new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))) return text; } } } } } } catch { } return null; }
        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0) { if (currentDepth > maxDepth) yield break; yield return node; if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) { foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child; } } else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) { foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child; } } }
        private static decimal ExtractRichSnippetPrice(string htmlBlock) { var richPriceRegex = new Regex(@"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>", RegexOptions.IgnoreCase); var match = richPriceRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock) { var deliveryRegex = new Regex(@"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)", RegexOptions.IgnoreCase); var match = deliveryRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text) { var priceRegex = new Regex(@"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?", RegexOptions.IgnoreCase); var matches = priceRegex.Matches(text); decimal bestPrice = 0; decimal bestDelivery = 0; foreach (Match m in matches) { if (!decimal.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue; if (val < 1.0m) continue; int contextStart = Math.Max(0, m.Index - 30); int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20); string snippetForLog = text.Substring(contextStart, contextLen).ToLower(); if (Regex.IsMatch(snippetForLog, @"(dostawa|wysyłka|delivery|\+)")) { if (bestDelivery == 0) bestDelivery = val; } else if (bestPrice == 0) { bestPrice = val; } } return (bestPrice, bestDelivery); }
        private static string StripHtml(string html) { if (string.IsNullOrEmpty(html)) return ""; string s = html.Replace("<br>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " "); s = Regex.Replace(s, "<.*?>", " "); s = WebUtility.HtmlDecode(s); s = Regex.Replace(s, @"\s+", " ").Trim(); return s; }
        private static decimal ParsePriceDecimal(string priceStr) { if (string.IsNullOrEmpty(priceStr)) return 0; string clean = Regex.Replace(priceStr, @"[^\d,.]", ""); if (clean.Contains(",") && clean.Contains(".")) { if (clean.LastIndexOf(',') > clean.LastIndexOf('.')) clean = clean.Replace(".", "").Replace(",", "."); else clean = clean.Replace(",", ""); } else if (clean.Contains(",")) { clean = clean.Replace(",", "."); } else if (clean.Count(c => c == '.') > 1) { int lastDot = clean.LastIndexOf('.'); clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot); } if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res; return 0; }
    }
}