using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// ZMIANA: Dodano pole 'Source' na końcu, aby identyfikować źródło (OAPV vs WRGA)
public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock, string Source);

public class GoogleGlobalScraper
{
    // === KONFIGURACJA WRGA ===
    private const bool ENABLE_WRGA_MODE = false;
    private const decimal WRGA_LOWER_LIMIT = 0.8m; // Odrzuć jeśli tańsze o 80% od średniej
    private const decimal WRGA_UPPER_LIMIT = 2.0m; // Odrzuć jeśli droższe o 200% od średniej

    private static readonly HttpClient _httpClient;

    static GoogleGlobalScraper()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
    {
        var finalPriceData = new List<PriceData>();
        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

        // LOG START
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine($" START SCRAPERA: Region [{countryCode.ToUpper()}] | CID: {catalogId ?? "BRAK"}");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        if (string.IsNullOrEmpty(catalogId))
        {
            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
            return finalPriceData;
        }

        string urlTemplate =
            $"https://www.google.com/async/oapv?udm=3&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";

        var allFoundOffers = new List<TempOfferGlobal>();
        string? firstPageRawResponse = null;

        int startIndex = 0;
        const int pageSize = 10;
        int lastFetchCount;
        const int maxRetries = 3;

        // ==============================================================================
        // 1. GŁÓWNA PĘTLA OAPV (JSON)
        // ==============================================================================
        do
        {
            string currentUrl = string.Format(urlTemplate, startIndex);
            List<TempOfferGlobal> newOffers = new();
            string rawResponse = "";
            bool requestSucceeded = false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.Write($"\r[OAPV] Pobieranie strony {startIndex / pageSize + 1} (próba {attempt})...");

                    rawResponse = await _httpClient.GetStringAsync(currentUrl);

                    // Parser teraz zwraca oferty z ustawionym Source="OAPV"
                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

                    if (newOffers.Any() || rawResponse.Length < 100)
                    {
                        requestSucceeded = true;
                        break;
                    }
                    if (attempt < maxRetries) await Task.Delay(600);
                }
                catch (Exception)
                {
                    if (attempt < maxRetries) await Task.Delay(700);
                }
            }
            // Czyścimy linię po statusie
            Console.Write("\r" + new string(' ', 60) + "\r");

            if (!requestSucceeded) break;

            if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse))
            {
                firstPageRawResponse = rawResponse;
            }

            lastFetchCount = newOffers.Count;

            foreach (var offer in newOffers)
            {
                if (!allFoundOffers.Any(o => o.Url == offer.Url))
                {
                    allFoundOffers.Add(offer);
                }
            }

            startIndex += pageSize;
            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

        } while (lastFetchCount == pageSize);

        Console.WriteLine($"[INFO] OAPV zakończone. Pobrano stron: {startIndex / pageSize}. Znaleziono ofert: {allFoundOffers.Count}");

        // --- Wyświetlenie tabeli z wynikami OAPV ---
        PrintDetailTable(allFoundOffers, "WYNIKI PO ETAPIE OAPV (JSON)");


        // ==============================================================================
        // 2. WRGA MODE (Smart Q - HTML)
        // ==============================================================================
        if (ENABLE_WRGA_MODE && !string.IsNullOrEmpty(firstPageRawResponse))
        {
            decimal baselinePrice = 0;
            if (allFoundOffers.Any())
            {
                var prices = allFoundOffers.Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
                if (prices.Any()) baselinePrice = prices.Average();
            }

            string? productTitle = GoogleShoppingApiParserGlobal.ExtractProductTitle(firstPageRawResponse);

            if (!string.IsNullOrEmpty(productTitle))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n--------------------------------------------------------------------------------");
                Console.WriteLine($"[WRGA START] Wykryty tytuł: '{productTitle}'");
                Console.WriteLine($"[WRGA INFO]  Średnia cena OAPV (Baseline): {baselinePrice:F2}");
                Console.WriteLine("--------------------------------------------------------------------------------");
                Console.ResetColor();

                string encodedQ = Uri.EscapeDataString(productTitle);
                string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&gl={countryCode}&async=_fmt:prog";

                Console.WriteLine($"[WRGA URL] {wrgaUrl}");

                try
                {
                    string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
                    // Parser teraz zwraca oferty z ustawionym Source="WRGA"
                    var wrgaOffers = GoogleShoppingApiParserGlobal.ParseWrga(wrgaResponse);

                    Console.WriteLine($"[WRGA] Pobranych surowych ofert HTML: {wrgaOffers.Count}\n");

                    // Nagłówek logowania dla WRGA
                    Console.WriteLine($"{"STATUS",-10} | {"Cena",-10} | {"Sprzedawca",-20} | Info");
                    Console.WriteLine(new string('-', 60));

                    int addedCount = 0;

                    foreach (var off in wrgaOffers)
                    {
                        decimal wrgaPrice = ParsePrice(off.Price);
                        bool isAccepted = true;
                        string statusMsg = "OK";
                        ConsoleColor statusColor = ConsoleColor.Green;

                        // 1. Sprawdzenie duplikatów (czy URL już jest w bazie)
                        if (allFoundOffers.Any(existing => existing.Url == off.Url))
                        {
                            isAccepted = false;
                            statusMsg = "DUPLIKAT";
                            statusColor = ConsoleColor.DarkGray;
                        }
                        // 2. Limiter cenowy WRGA
                        else if (baselinePrice > 0 && wrgaPrice > 0)
                        {
                            decimal diff = wrgaPrice - baselinePrice;
                            decimal percentageDiff = diff / baselinePrice;

                            if (percentageDiff < -WRGA_LOWER_LIMIT)
                            {
                                isAccepted = false;
                                statusMsg = "ZA TANIO"; // -80%
                                statusColor = ConsoleColor.Red;
                            }
                            else if (percentageDiff > WRGA_UPPER_LIMIT)
                            {
                                isAccepted = false;
                                statusMsg = "ZA DROGO"; // +200%
                                statusColor = ConsoleColor.Red;
                            }
                        }

                        // Wypisanie logu wiersza
                        Console.ForegroundColor = statusColor;
                        Console.WriteLine($"{statusMsg,-10} | {off.Price,-10} | {Truncate(off.Seller, 20),-20} | {(isAccepted ? "Dodano" : "Odrzucono")}");
                        Console.ResetColor();

                        if (isAccepted)
                        {
                            allFoundOffers.Add(off);
                            addedCount++;
                        }
                    }
                    Console.WriteLine($"\n[WRGA SUMA] Dodano unikalnych ofert: {addedCount}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WRGA ERROR] {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[WRGA SKIP] Nie udało się ustalić tytułu produktu.");
            }
        }

        // ==============================================================================
        // 3. PRZETWARZANIE KOŃCOWE (Grupowanie i wybór najlepszej)
        // ==============================================================================

        // Lista pomocnicza do wyświetlenia w tabeli końcowej
        var displayList = new List<(string Seller, decimal Price, decimal Total, string Source, string Url)>();

        foreach (var group in allFoundOffers.GroupBy(o => o.Seller))
        {
            var cheapestOffer = group
                .Select(o =>
                {
                    var basePrice = ParsePrice(o.Price);
                    var del = ParseDeliveryPrice(o.Delivery);
                    return new
                    {
                        Offer = o,
                        Base = basePrice,
                        Ship = del,
                        Total = basePrice + del
                    };
                })
                .OrderBy(x => x.Total)
                .ThenBy(x => x.Base)
                .First();

            finalPriceData.Add(new PriceData
            {
                StoreName = cheapestOffer.Offer.Seller,
                Price = cheapestOffer.Base,
                PriceWithDelivery = cheapestOffer.Total,
                OfferUrl = UnwrapGoogleRedirectUrl(cheapestOffer.Offer.Url) ?? cheapestOffer.Offer.Url,
                ScrapingProductId = scrapingProduct.ScrapingProductId,
                RegionId = scrapingProduct.RegionId
            });

            displayList.Add((
                cheapestOffer.Offer.Seller,
                cheapestOffer.Base,
                cheapestOffer.Total,
                cheapestOffer.Offer.Source,
                cheapestOffer.Offer.Url
            ));
        }

        // --- 4. WYŚWIETLANIE TABELI KOŃCOWEJ ---
        PrintFinalSummaryTable(displayList, countryCode);

        return finalPriceData;
    }

    // === METODY POMOCNICZE DRUKOWANIA (TABELE) ===

    private void PrintDetailTable(List<TempOfferGlobal> offers, string title)
    {
        if (!offers.Any()) return;

        var sorted = offers.OrderBy(o => ParsePrice(o.Price)).ToList();

        Console.WriteLine("\n");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("==========================================================================================================");
        Console.WriteLine($" {title} - Liczba: {sorted.Count}");
        Console.WriteLine("==========================================================================================================");
        Console.ResetColor();

        Console.WriteLine($"{"Lp.",-4} | {"Źródło",-6} | {"Sprzedawca",-25} | {"Cena",-10} | {"Dostawa",-10} | URL");
        Console.WriteLine("----------------------------------------------------------------------------------------------------------");

        int idx = 1;
        foreach (var item in sorted)
        {
            string src = item.Source;
            string seller = Truncate(item.Seller, 25);
            string price = item.Price;
            string del = item.Delivery ?? "-";
            string url = Truncate(item.Url, 30);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{idx,-4} | {src,-6} | {seller,-25} | {price,-10} | {del,-10} | {url}");
            Console.ResetColor();
            idx++;
        }
        Console.WriteLine("----------------------------------------------------------------------------------------------------------");
    }

    private void PrintFinalSummaryTable(List<(string Seller, decimal Price, decimal Total, string Source, string Url)> data, string region)
    {
        if (!data.Any())
        {
            Console.WriteLine("\n[INFO] Brak ofert końcowych do wyświetlenia.");
            return;
        }

        var sorted = data.OrderBy(x => x.Total).ToList();

        Console.WriteLine("\n");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("==========================================================================================================");
        Console.WriteLine($" TABELA KOŃCOWA (OAPV + WRGA) | REGION: {region.ToUpper()} | Unikalni sprzedawcy: {sorted.Count}");
        Console.WriteLine("==========================================================================================================");
        Console.ResetColor();

        Console.WriteLine($"{"Lp.",-4} | {"Metoda",-6} | {"Sprzedawca",-25} | {"Cena",-10} | {"Razem",-10} | URL");
        Console.WriteLine("----------------------------------------------------------------------------------------------------------");

        int index = 1;
        foreach (var item in sorted)
        {
            string lp = index.ToString() + ".";
            string src = item.Source;
            string seller = Truncate(item.Seller, 25);
            string price = item.Price.ToString("F2");
            string total = item.Total.ToString("F2");
            string url = Truncate(item.Url, 40);

            Console.WriteLine($"{lp,-4} | {src,-6} | {seller,-25} | {price,-10} | {total,-10} | {url}");
            index++;
        }
        Console.WriteLine("----------------------------------------------------------------------------------------------------------\n");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
    }

    #region Helper Methods (Extraction, Parsing)

    private string? ExtractProductId(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var m1 = Regex.Match(url, @"(?:/|-)product/(\d+)", RegexOptions.IgnoreCase);
        if (m1.Success) return m1.Groups[1].Value;

        var m2 = Regex.Match(url, @"[?&]cid=(\d+)", RegexOptions.IgnoreCase);
        if (m2.Success) return m2.Groups[1].Value;

        var m3 = Regex.Match(url, @"cid:(\d+)", RegexOptions.IgnoreCase);
        if (m3.Success) return m3.Groups[1].Value;

        var m4 = Regex.Match(url, @"shopping/(?:product|offers)/(\d+)", RegexOptions.IgnoreCase);
        if (m4.Success) return m4.Groups[1].Value;

        return null;
    }

    private static string? UnwrapGoogleRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');

            var matchQ = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
            if (matchQ.Success)
            {
                var val = Uri.UnescapeDataString(matchQ.Groups[1].Value);

                try { val = Uri.UnescapeDataString(val); } catch { }
                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
            }

            return url;
        }
        catch
        {
            return url;
        }
    }

    private static decimal ParsePrice(string priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return 0;

        var cleanedText = Regex.Replace(priceText, @"[^\d,.\-]", "");

        bool hasComma = cleanedText.Contains(',');
        bool hasDot = cleanedText.Contains('.');

        if (hasDot && hasComma)
        {
            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
            {
                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
            }
            else
            {
                cleanedText = cleanedText.Replace(",", "");
            }
        }
        else if (hasComma)
        {
            if (cleanedText.Count(c => c == ',') > 1)
                cleanedText = cleanedText.Replace(",", "");
            else
                cleanedText = cleanedText.Replace(",", ".");
        }
        else if (hasDot)
        {
            if (cleanedText.Count(c => c == '.') > 1)
                cleanedText = cleanedText.Replace(".", "");
        }

        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;

        return 0;
    }

    private static decimal ParseDeliveryPrice(string? deliveryText)
    {
        if (string.IsNullOrWhiteSpace(deliveryText)) return 0;

        var freeWords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
        if (freeWords.Any(w => deliveryText.Contains(w, StringComparison.OrdinalIgnoreCase))) return 0;

        var m = GoogleShoppingApiParserGlobal.PricePattern.Match(deliveryText);
        if (m.Success) return ParsePrice(m.Value);

        return ParsePrice(deliveryText);
    }

    #endregion
}

public static class GoogleShoppingApiParserGlobal
{
    public static readonly Regex PricePattern = new(
        @"^(\+)?\s*([\d][\d\s,\.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
        + "|" +
        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,\.]*[\d])\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DeliveryKeywords = { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
    private static readonly string[] FreeKeywords = { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
    private static readonly string[] UsedKeywords = { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
    private static readonly string[] OosKeywords = { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };

    public static string? ExtractProductTitle(string rawResponse)
    {
        try
        {
            string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
            using JsonDocument doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.TryGetProperty("ProductDetailsResult", out JsonElement pd) && pd.GetArrayLength() > 0) return pd[0].GetString();
        }
        catch { }
        return null;
    }

    // ZMIANA: Ustawiamy source na "WRGA"
    public static List<TempOfferGlobal> ParseWrga(string rawResponse)
    {
        var offers = new List<TempOfferGlobal>();
        if (string.IsNullOrWhiteSpace(rawResponse)) return offers;

        try
        {
            string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
            string decodedContent = Regex.Unescape(cleaned);
            decodedContent = WebUtility.HtmlDecode(decodedContent);

            var blockRegex = new Regex(
                @"<div[^>]*class=""[^""]*tF2Cxc[^""]*""[^>]*>([\s\S]*?)(?=<div[^>]*class=""[^""]*tF2Cxc|$)",
                RegexOptions.IgnoreCase);

            var blockMatches = blockRegex.Matches(decodedContent);

            foreach (Match blockMatch in blockMatches)
            {
                string block = blockMatch.Groups[1].Value;

                var urlMatch = Regex.Match(block, @"href=""(https?://[^""]+)""");
                if (!urlMatch.Success) continue;

                string url = urlMatch.Groups[1].Value;
                if (IsGoogleLink(url)) continue;

                string seller = GetDomainName(url);
                decimal priceVal = 0;
                decimal deliveryVal = 0;

                priceVal = ExtractRichSnippetPrice(block);

                if (priceVal > 0)
                {
                    deliveryVal = ExtractDeliveryFromRichSnippet(block);
                }
                else
                {
                    string cleanText = StripHtml(block);
                    var analysisResult = AnalyzePricesInBlock(cleanText);
                    priceVal = analysisResult.Price;
                    deliveryVal = analysisResult.Delivery;
                }

                if (priceVal > 0)
                {
                    string finalPrice = priceVal.ToString("F2", CultureInfo.InvariantCulture);
                    string deliveryStr = deliveryVal > 0 ? deliveryVal.ToString("F2", CultureInfo.InvariantCulture) : null;
                    bool isInStock = true;

                    // Source = "WRGA"
                    offers.Add(new TempOfferGlobal(
                        Seller: seller,
                        Price: finalPrice,
                        Url: url,
                        Delivery: deliveryStr,
                        IsInStock: isInStock,
                        Source: "WRGA"
                    ));
                }
            }
        }
        catch { }
        return offers;
    }

    private static decimal ExtractRichSnippetPrice(string htmlBlock)
    {
        var richPriceRegex = new Regex(
            @"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>",
            RegexOptions.IgnoreCase);
        var match = richPriceRegex.Match(htmlBlock);
        if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
        return 0;
    }

    private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock)
    {
        var deliveryRegex = new Regex(
            @"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)",
            RegexOptions.IgnoreCase);
        var match = deliveryRegex.Match(htmlBlock);
        if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
        return 0;
    }

    private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text)
    {
        var priceRegex = new Regex(
            @"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?",
            RegexOptions.IgnoreCase);

        var matches = priceRegex.Matches(text);
        decimal bestPrice = 0;
        decimal bestDelivery = 0;

        var candidates = new List<(decimal val, int index, int score)>();

        foreach (Match m in matches)
        {
            string numStr = m.Groups[1].Value;
            if (!decimal.TryParse(numStr.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue;

            bool hasCurrency = m.Value.Contains("zł") || m.Value.Contains("PLN");
            bool hasPriceKeyword = m.Value.ToLower().Contains("cena") || m.Value.ToLower().Contains("za");

            if (val < 1.0m && !hasCurrency && !hasPriceKeyword) continue;

            int contextStart = Math.Max(0, m.Index - 30);
            int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20);
            string fullMatchContext = text.Substring(contextStart, contextLen).ToLower();

            if (Regex.IsMatch(fullMatchContext, @"(dostawa|wysyłka|delivery|\+)"))
            {
                if (bestDelivery == 0) bestDelivery = val;
                continue;
            }

            int score = 0;
            if (hasPriceKeyword) score += 50;
            if (hasCurrency) score += 10;
            if (fullMatchContext.Contains("brutto")) score += 5;
            if (fullMatchContext.Contains("netto")) score += 2;
            if (val > 1900 && val < 2100 && val % 1 == 0 && !hasCurrency) continue;

            candidates.Add((val, m.Index, score));
        }

        var winner = candidates.OrderByDescending(x => x.score).ThenBy(x => x.index).FirstOrDefault();
        if (winner.val > 0) bestPrice = winner.val;

        return (bestPrice, bestDelivery);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string s = html.Replace("<br>", " ").Replace("<br/>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " ").Replace("</em>", " ").Replace("</a>", " ");
        s = Regex.Replace(s, "<.*?>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static decimal ParsePriceDecimal(string priceStr)
    {
        if (string.IsNullOrEmpty(priceStr)) return 0;
        string clean = Regex.Replace(priceStr, @"[^\d,.]", "");

        if (clean.Contains(",") && clean.Contains("."))
        {
            if (clean.LastIndexOf(',') > clean.LastIndexOf('.')) clean = clean.Replace(".", "").Replace(",", ".");
            else clean = clean.Replace(",", "");
        }
        else if (clean.Contains(",")) clean = clean.Replace(",", ".");

        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res;
        return 0;
    }

    private static bool IsGoogleLink(string url)
    {
        return url.Contains(".google.") || url.Contains("gstatic.") ||
               url.Contains("/search?") || url.Contains("youtube.") ||
               url.Contains("googleusercontent") || url.Contains("translate.google");
    }

    private static string GetDomainName(string url)
    {
        try
        {
            var host = new Uri(url).Host.ToLower().Replace("www.", "");
            return char.ToUpper(host[0]) + host.Substring(1);
        }
        catch { return "Sklep"; }
    }

    // ZMIANA: Ustawiamy Source na "OAPV"
    public static List<TempOfferGlobal> Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return new List<TempOfferGlobal>();

        try
        {
            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
            using var doc = JsonDocument.Parse(cleanedJson, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement.Clone();
            var allOffers = new List<TempOfferGlobal>();
            FindAndParseAllOffers(root, root, allOffers);
            return allOffers;
        }
        catch (JsonException) { return new List<TempOfferGlobal>(); }
    }

    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
            {
                foreach (var potentialOffer in node.EnumerateArray())
                {
                    var offer = ParseSingleOffer(root, potentialOffer);
                    if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
                    {
                        allOffers.Add(offer);
                    }
                }
            }
        }

        if (node.ValueKind == JsonValueKind.Array)
            foreach (var element in node.EnumerateArray()) FindAndParseAllOffers(root, element, allOffers);
        else if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject()) FindAndParseAllOffers(root, property.Value, allOffers);
    }

    private static bool IsPotentialSingleOffer(JsonElement node)
    {
        JsonElement offerData = node;
        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array) offerData = node[0];
        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;
        var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        bool hasUrl = flatStrings.Any(IsExternalUrl);
        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));
        return hasUrl && hasPrice;
    }

    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
    {
        try
        {
            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
            if (offerData.ValueKind != JsonValueKind.Array) return null;
            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            if (flatStrings.Any(text => UsedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) return null;
            bool isInStock = !flatStrings.Any(text => OosKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)));
            string? url = flatStrings.FirstOrDefault(IsExternalUrl);
            if (!string.IsNullOrEmpty(url)) { var unwrapped = UnwrapGoogleRedirectUrl(url); if (!string.IsNullOrEmpty(unwrapped)) url = unwrapped; }
            var priceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
            string? itemPrice = priceStrings.FirstOrDefault(s => !s.TrimStart().StartsWith("+"));
            string? deliveryRaw = priceStrings.FirstOrDefault(s => s.TrimStart().StartsWith("+"));
            string? seller = !string.IsNullOrWhiteSpace(url) ? SellerFromUrl(url) : null;
            string? delivery = null;
            if (!string.IsNullOrEmpty(deliveryRaw)) delivery = deliveryRaw.Trim();
            else
            {
                var deliveryLine = flatStrings.FirstOrDefault(s => DeliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrEmpty(deliveryLine)) { if (FreeKeywords.Any(w => deliveryLine.Contains(w, StringComparison.OrdinalIgnoreCase))) delivery = "Bezpłatna"; else { var m = PricePattern.Match(deliveryLine); if (m.Success) delivery = m.Value.Trim(); } }
                else if (flatStrings.Any(s => FreeKeywords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)))) delivery = "Bezpłatna";
            }
            if (!string.IsNullOrWhiteSpace(seller) && itemPrice != null && url != null)
            {
                // Source = "OAPV"
                return new TempOfferGlobal(seller, itemPrice, url, delivery, isInStock, "OAPV");
            }
            return null;
        }
        catch { return null; }
    }

    private static string SellerFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;
            host = Regex.Replace(host, @"^(www|m|amp|click|l)\.", "", RegexOptions.IgnoreCase);
            var registrable = GetRegistrableDomain(host);
            return ToSellerCase(registrable);
        }
        catch { return "Sklep"; }
    }

    private static string GetRegistrableDomain(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return host;
        int n = parts.Length;
        var sldMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "co", "com", "org", "net", "gov", "edu" };
        if (parts[n - 1].Length == 2 && sldMarkers.Contains(parts[n - 2]) && n >= 3) return $"{parts[n - 3]}.{parts[n - 2]}.{parts[n - 1]}";
        return $"{parts[n - 2]}.{parts[n - 1]}";
    }

    private static string ToSellerCase(string domain)
    {
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return domain;
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var first = textInfo.ToTitleCase(parts[0].ToLowerInvariant());
        var rest = parts.Skip(1).Select(p => p.ToLowerInvariant());
        return string.Join(".", (new[] { first }).Concat(rest));
    }

    private static bool IsExternalUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Contains("google.", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string? UnwrapGoogleRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try { var uri = new Uri(url); var query = uri.Query.TrimStart('?'); var m = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase); if (m.Success) { var val = Uri.UnescapeDataString(m.Groups[1].Value); try { val = Uri.UnescapeDataString(val); } catch { } if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val; } return url; } catch { return url; }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node)
    {
        var stack = new Stack<JsonElement>(); stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Array) foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
            else if (current.ValueKind == JsonValueKind.Object) foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
            else yield return current;
        }
    }
}