

//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//// 1. Rekord pomocniczy
//public record TempOffer(string Seller, string Price, string Url, string? Delivery, bool IsInStock, string? Badge, int OriginalIndex);

//public class GoogleMainPriceScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleMainPriceScraper()
//    {
//        _httpClient = new HttpClient();
//        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
//    }

//    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//    {
//        var finalPriceHistory = new List<CoOfrPriceHistoryClass>();

//        // Krok 1: Wyodrębnij ID katalogu (CID)
//        string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
//            return finalPriceHistory;
//        }



//        //string urlTemplate;
//        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        //{
//        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
//        //    // Dodano gl:4, usunięto fs oraz isp
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        //}
//        //else
//        //{
//        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//        //    // Dodano gl:4, usunięto fs oraz isp
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        //}

//        //nowy z catalogid

//        //string urlTemplate;
//        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        //{
//        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
//        //    // Dodano gl:4, usunięto fs oraz isp
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        //}
//        //else
//        //{
//        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//        //    // Dodano gl:4, usunięto fs oraz isp
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        //}







//        //stary niepelny schemat

//        //string urlTemplate;
//        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        //{
//        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
//        //}
//        //else
//        //{
//        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//        //    urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
//        //}



//        //z isp

//        string urlTemplate;
//        if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        {
//            Console.WriteLine($"metoda bez gid dla CID: {catalogId}");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,gl:4,pvt:hg,_fmt:jspb";
//        }
//        else
//        {
//            Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,gl:4,pvt:hg,_fmt:jspb";
//        }

//        var allFoundOffers = new List<TempOffer>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOffer> newOffers = new List<TempOffer>();

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);
//                    newOffers = GoogleShoppingApiParser.Parse(rawResponse);

//                    if (newOffers.Any() || rawResponse.Length < 100) break;
//                    if (attempt < maxRetries) await Task.Delay(2000);
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        await Task.Delay(2500);
//                    }
//                }
//            }
//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;
//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(500, 800));
//        } while (lastFetchCount == pageSize);

//        // Krok 3: Nadanie oryginalnych indeksów
//        var indexedOffers = allFoundOffers.Select((offer, index) => offer with { OriginalIndex = index + 1 }).ToList();

//        var groupedBySeller = indexedOffers.GroupBy(o => o.Seller);
//        var finalOffersToProcess = new List<(TempOffer offer, int count)>();

//        foreach (var group in groupedBySeller)
//        {
//            int storeOfferCount = group.Count();
//            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
//            finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
//        }

//        // Krok 4: Finalne mapowanie
//        foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
//        {
//            // =================================================================
//            // LOGIKA ROZRÓŻNIANIA ODZNAK (BPG vs HPG)
//            // =================================================================
//            string? isBiddingValue = null;

//            if (!string.IsNullOrEmpty(item.offer.Badge))
//            {
//                string badgeLower = item.offer.Badge.ToLower();

//                if (badgeLower.Contains("cena")) // Najlepsza cena, Niska cena
//                {
//                    isBiddingValue = "bpg"; // Best Price Google
//                }
//                else if (badgeLower.Contains("popularn")) // Najpopularniejsze
//                {
//                    isBiddingValue = "hpg"; // High Popularity Google (lub inne rozwinięcie skrótu)
//                }
//            }
//            // =================================================================

//            finalPriceHistory.Add(new CoOfrPriceHistoryClass
//            {
//                GoogleStoreName = item.offer.Seller,
//                GooglePrice = ParsePrice(item.offer.Price),
//                GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),

//                // Oryginalna pozycja z Google
//                GooglePosition = item.offer.OriginalIndex.ToString(),

//                // Kod odznaki (bpg / hpg / null)
//                IsBidding = isBiddingValue,

//                GoogleInStock = item.offer.IsInStock,
//                GoogleOfferPerStoreCount = item.count
//            });
//        }

//        return finalPriceHistory;
//    }

//    #region Helper Methods
//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;
//        var match = Regex.Match(url, @"product/(\d+)");
//        return match.Success ? match.Groups[1].Value : null;
//    }

//    private decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;
//        var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".");
//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//        {
//            return result;
//        }
//        return 0;
//    }

//    private decimal ParseDeliveryPrice(string? deliveryText)
//    {
//        if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa"))
//        {
//            return 0;
//        }
//        return ParsePrice(deliveryText);
//    }
//    #endregion
//}





//public static class GoogleShoppingApiParser
//{
//    private static readonly Regex PricePattern = new(@"\d[\d\s,.]*\s*(?:PLN|zł)", RegexOptions.Compiled);

//    public static List<TempOffer> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse)) return new List<TempOffer>();

//        try
//        {
//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using JsonDocument doc = JsonDocument.Parse(cleanedJson);
//            JsonElement root = doc.RootElement.Clone();

//            var allOffers = new List<TempOffer>();
//            FindAndParseAllOffers(root, root, allOffers);
//            return allOffers;
//        }
//        catch (JsonException)
//        {
//            return new List<TempOffer>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//    {
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//            {
//                foreach (JsonElement potentialOffer in node.EnumerateArray())
//                {
//                    TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                    if (offer != null)
//                    {
//                        if (!allOffers.Any(o => o.Url == offer.Url))
//                        {
//                            allOffers.Add(offer);
//                        }
//                    }
//                }
//            }
//        }

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//            {
//                FindAndParseAllOffers(root, element, allOffers);
//            }
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//            {
//                FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        JsonElement offerData = node;
//        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//        {
//            offerData = node[0];
//        }

//        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//        var flatStrings = Flatten(offerData)
//            .Where(e => e.ValueKind == JsonValueKind.String)
//            .Select(e => e.GetString()!)
//            .ToList();

//        if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google"))) return true;

//        return false;
//    }

//    private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//    {
//        JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array)
//                                        ? offerContainer[0]
//                                        : offerContainer;

//        if (offerData.ValueKind != JsonValueKind.Array) return null;

//        try
//        {
//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            // 1. Filtrowanie używanych
//            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony" };
//            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                return null;
//            }

//            // 2. Dostępność
//            bool isInStock = true;
//            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
//            if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                isInStock = false;
//            }

//            // 3. Podstawowe dane
//            string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//            string? price = flatStrings.FirstOrDefault(s => PricePattern.IsMatch(s) && !s.Trim().StartsWith("+"));

//            // 4. Sprzedawca
//            string? seller = null;
//            var offerElements = offerData.EnumerateArray().ToList();
//            for (int i = 0; i < offerElements.Count - 1; i++)
//            {
//                if (offerElements[i].ValueKind == JsonValueKind.Number &&
//                    offerElements[i + 1].ValueKind == JsonValueKind.String)
//                {
//                    string potentialSeller = offerElements[i + 1].GetString()!;
//                    if (!potentialSeller.StartsWith("http") && !PricePattern.IsMatch(potentialSeller) && potentialSeller.Length > 2)
//                    {
//                        seller = potentialSeller;
//                        break;
//                    }
//                }
//            }

//            if (seller == null)
//            {
//                var sellerNode = offerData.EnumerateArray()
//                    .FirstOrDefault(item => item.ValueKind == JsonValueKind.Array
//                                            && item.GetArrayLength() > 1
//                                            && item[0].ValueKind == JsonValueKind.String
//                                            && item[1].ValueKind == JsonValueKind.String
//                                            && item[1].GetString()!.All(char.IsDigit));
//                if (sellerNode.ValueKind != JsonValueKind.Undefined)
//                {
//                    var potentialSeller = sellerNode[0].GetString()!;
//                    if (!int.TryParse(potentialSeller, out _)) seller = potentialSeller;
//                }
//            }

//            if (seller == null && url != null)
//            {
//                var docIdMatch = Regex.Match(url, @"shopping_docid(?:%253D|=)(\d+)|docid(?:%3D|=)(\d+)");
//                if (docIdMatch.Success)
//                {
//                    string offerId = docIdMatch.Groups[1].Success ? docIdMatch.Groups[1].Value : docIdMatch.Groups[2].Value;
//                    var sellerInfoNodes = FindNodesById(root, offerId);
//                    foreach (var sellerInfoNode in sellerInfoNodes)
//                    {
//                        if (sellerInfoNode.ValueKind == JsonValueKind.Array && sellerInfoNode.GetArrayLength() > 1 && sellerInfoNode[1].ValueKind == JsonValueKind.Array)
//                        {
//                            var potentialSellerName = sellerInfoNode[1].EnumerateArray()
//                                .FirstOrDefault(e => e.ValueKind == JsonValueKind.String);

//                            if (potentialSellerName.ValueKind == JsonValueKind.String)
//                            {
//                                seller = potentialSellerName.GetString();
//                                break;
//                            }
//                        }
//                    }
//                }
//            }

//            // 5. Dostawa
//            string? delivery = null;
//            string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && PricePattern.IsMatch(s))
//                                      ?? flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) || s.Contains("delivery", StringComparison.OrdinalIgnoreCase));

//            if (rawDeliveryText != null)
//            {
//                if (rawDeliveryText.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Free", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Darmowa", StringComparison.OrdinalIgnoreCase))
//                    delivery = "Bezpłatna";
//                else
//                {
//                    Match priceMatch = PricePattern.Match(rawDeliveryText);
//                    if (priceMatch.Success) delivery = priceMatch.Value.Trim();
//                }
//            }

//            // 6. Odznaka (Badge)
//            string? badge = ExtractBadge(offerData);

//            if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
//            {
//                // Przekazujemy 0 jako tymczasowy OriginalIndex
//                return new TempOffer(seller, price, url, delivery, isInStock, badge, 0);
//            }
//        }
//        catch { }

//        return null;
//    }

//    private static string? ExtractBadge(JsonElement offerData)
//    {
//        try
//        {
//            foreach (var element in offerData.EnumerateArray())
//            {
//                if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
//                {
//                    var inner = element[0];
//                    if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0)
//                    {
//                        var potentialBadgeNode = inner[0];
//                        if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1)
//                        {
//                            if (potentialBadgeNode[0].ValueKind == JsonValueKind.String)
//                            {
//                                string text = potentialBadgeNode[0].GetString()!;

//                                // STRICT MATCH: Tylko dokładne frazy (ignorując wielkość liter)
//                                bool isValid =
//                                    string.Equals(text, "Najlepsza cena", StringComparison.OrdinalIgnoreCase) ||
//                                    string.Equals(text, "Niska cena", StringComparison.OrdinalIgnoreCase) ||
//                                    string.Equals(text, "Najpopularniejsze", StringComparison.OrdinalIgnoreCase);

//                                if (isValid)
//                                {
//                                    return text;
//                                }
//                            }
//                        }
//                    }
//                }
//            }
//        }
//        catch { }
//        return null;
//    }

//    private static List<JsonElement> FindNodesById(JsonElement node, string id)
//    {
//        var results = new List<JsonElement>();
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);

//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                if (current.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == id))
//                {
//                    results.Add(current);
//                }
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//        }
//        return results;
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node)
//    {
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);
//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//            else
//            {
//                yield return current;
//            }
//        }
//    }
//}





























//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOffer(string Seller, string Price, string Url, string? Delivery, bool IsInStock, string? Badge, int OriginalIndex);

//public class GoogleMainPriceScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleMainPriceScraper()
//    {
//        _httpClient = new HttpClient();

//        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
//    }

//    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//    {
//        var finalPriceHistory = new List<CoOfrPriceHistoryClass>();

//        string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
//            return finalPriceHistory;
//        }

//        string urlTemplate;
//        if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        {
//            Console.WriteLine($"metoda bez gid dla CID: {catalogId}");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        }
//        else
//        {
//            Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
//        }

//        var allFoundOffers = new List<TempOffer>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOffer> newOffers = new List<TempOffer>();

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);
//                    newOffers = GoogleShoppingApiParser.Parse(rawResponse);

//                    if (newOffers.Count == 0)
//                    {
//                        Console.ForegroundColor = ConsoleColor.Red;
//                        Console.WriteLine($"[PRÓBA {attempt}/{maxRetries}] Znaleziono 0 produktow. URL: {currentUrl}");

//                        string preview = rawResponse.Length > 500 ? rawResponse.Substring(0, 500) : rawResponse;

//                        preview = preview.Replace("\n", " ").Replace("\r", "");
//                        Console.WriteLine($"[DEBUG TREŚCI]: {preview}...");

//                        Console.ResetColor();
//                    }

//                    if (newOffers.Count > 0)
//                    {
//                        break;
//                    }

//                    if (attempt < maxRetries)
//                    {
//                        Console.ForegroundColor = ConsoleColor.Yellow;
//                        Console.WriteLine($"Ponawiam probe za 2 sekundy... (Próba {attempt} z {maxRetries})");
//                        Console.ResetColor();

//                        await Task.Delay(2000);
//                    }
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        Console.ForegroundColor = ConsoleColor.Yellow;
//                        Console.WriteLine($"Błąd HTTP. Ponawiam probe za 4 sekundy... (Próba {attempt} z {maxRetries})");
//                        Console.ResetColor();
//                        await Task.Delay(4000);
//                    }
//                }
//            }

//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;

//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(500, 800));

//        } while (lastFetchCount == pageSize);

//        var indexedOffers = allFoundOffers.Select((offer, index) => offer with { OriginalIndex = index + 1 }).ToList();

//        var groupedBySeller = indexedOffers.GroupBy(o => o.Seller);
//        var finalOffersToProcess = new List<(TempOffer offer, int count)>();

//        foreach (var group in groupedBySeller)
//        {
//            int storeOfferCount = group.Count();
//            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
//            finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
//        }

//        foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
//        {
//            string? isBiddingValue = null;

//            if (!string.IsNullOrEmpty(item.offer.Badge))
//            {
//                string badgeLower = item.offer.Badge.ToLower();

//                if (badgeLower.Contains("cena"))
//                {
//                    isBiddingValue = "bpg";
//                }
//                else if (badgeLower.Contains("popularn") || badgeLower.Contains("wybór"))
//                {
//                    isBiddingValue = "hpg";
//                }
//            }

//            finalPriceHistory.Add(new CoOfrPriceHistoryClass
//            {
//                GoogleStoreName = item.offer.Seller,
//                GooglePrice = ParsePrice(item.offer.Price),
//                GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),
//                GooglePosition = item.offer.OriginalIndex.ToString(),
//                IsBidding = isBiddingValue,
//                GoogleInStock = item.offer.IsInStock,
//                GoogleOfferPerStoreCount = item.count
//            });
//        }

//        return finalPriceHistory;
//    }

//    #region Helper Methods
//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;
//        var match = Regex.Match(url, @"product/(\d+)");
//        return match.Success ? match.Groups[1].Value : null;
//    }

//    private decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;
//        var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".");
//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//        {
//            return result;
//        }
//        return 0;
//    }

//    private decimal ParseDeliveryPrice(string? deliveryText)
//    {
//        if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa"))
//        {
//            return 0;
//        }
//        return ParsePrice(deliveryText);
//    }
//    #endregion
//}
//public static class GoogleShoppingApiParser
//{
//    private static readonly Regex PricePattern = new(@"^\s*\d[\d\s,.]*\s*(?:PLN|zł|EUR|USD)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

//    // Ta lista nie jest już krytyczna przy nowej metodzie, ale zostawiamy dla porządku
//    private static readonly HashSet<string> InvalidSellerNames = new(StringComparer.OrdinalIgnoreCase)
//    {
//        "PLN", "EUR", "USD", "GBP", "zł", "Google", "Shopping", "null", "true", "false", "0", "1",
//        "Więcej opcji", "Porównaj oferty", "Dostawa", "Opinie", "Ocena"
//    };

//    public static List<TempOffer> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse)) return new List<TempOffer>();

//        try
//        {
//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using JsonDocument doc = JsonDocument.Parse(cleanedJson);
//            JsonElement root = doc.RootElement.Clone();

//            var allOffers = new List<TempOffer>();
//            FindAndParseAllOffers(root, root, allOffers);
//            return allOffers;
//        }
//        catch (JsonException)
//        {
//            return new List<TempOffer>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//    {
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (IsPotentialSingleOffer(node))
//            {
//                TempOffer? offer = ParseSingleOffer(root, node);

//                if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                {
//                    allOffers.Add(offer);
//                }
//            }

//            foreach (var element in node.EnumerateArray())
//            {
//                FindAndParseAllOffers(root, element, allOffers);
//            }
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//            {
//                FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() < 3) return false;

//        int checks = 0;
//        foreach (var element in node.EnumerateArray())
//        {
//            if (checks++ > 15) break;

//            if (element.ValueKind == JsonValueKind.String)
//            {
//                string s = element.GetString()!;
//                if (s.StartsWith("http") && !s.Contains("google.com/search") && !s.Contains("google.com/url"))
//                    return true;
//            }
//            else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
//            {
//                if (element[0].ValueKind == JsonValueKind.String)
//                {
//                    string s = element[0].GetString()!;
//                    if (s.StartsWith("http") && !s.Contains("google.com/search")) return true;
//                }
//            }
//        }
//        return false;
//    }

//    private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerData)
//    {
//        try
//        {
//            var flatElements = Flatten(offerData, 20).ToList();

//            var strings = flatElements
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//            if (strings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) return null;

//            bool isInStock = true;
//            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
//            if (strings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false;

//            // 1. Pobieramy URL
//            string? url = strings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com/search") && !s.Contains("gstatic.com"));
//            if (url == null) return null;

//            // 2. [ZMIANA] Sprzedawca zawsze z domeny (usuwamy starą pętlę foreach)
//            string seller = GetDomainName(url);

//            // 3. Cena
//            string? price = null;
//            price = strings.FirstOrDefault(s => PricePattern.IsMatch(s) && !s.Trim().StartsWith("+"));

//            if (price == null)
//            {
//                foreach (var el in flatElements)
//                {
//                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long val))
//                    {
//                        if (val > 1000000 && val < 50000000000)
//                        {
//                            decimal decimalPrice = (decimal)val / 1000000m;

//                            int index = flatElements.IndexOf(el);
//                            bool hasCurrencyNearby = false;
//                            for (int k = 1; k <= 6 && (index + k) < flatElements.Count; k++)
//                            {
//                                var neighbor = flatElements[index + k];
//                                if (neighbor.ValueKind == JsonValueKind.String)
//                                {
//                                    string ns = neighbor.GetString()!;
//                                    if (ns == "PLN" || ns == "zł") { hasCurrencyNearby = true; break; }
//                                }
//                            }

//                            if (hasCurrencyNearby)
//                            {
//                                price = $"{decimalPrice:F2} zł";
//                                break;
//                            }
//                        }
//                    }
//                }
//            }

//            if (price == null) return null;

//            // 4. Dostawa
//            string? delivery = null;
//            string? rawDeliveryText = strings.FirstOrDefault(s => s.Trim().StartsWith("+") && PricePattern.IsMatch(s))
//                                      ?? strings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) && PricePattern.IsMatch(s));

//            if (rawDeliveryText == null && strings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase)))
//            {
//                delivery = "Bezpłatna";
//            }
//            else if (rawDeliveryText != null)
//            {
//                Match priceMatch = Regex.Match(rawDeliveryText, @"\d[\d\s,.]*");
//                if (priceMatch.Success) delivery = priceMatch.Value.Trim() + " zł";
//            }

//            var badge = ExtractBadge(offerData);

//            return new TempOffer(seller, price, url, delivery, isInStock, badge, 0);
//        }
//        catch
//        {
//            return null;
//        }
//    }

//    // [NOWA METODA] Wyciąganie nazwy z domeny
//    private static string GetDomainName(string url)
//    {
//        try
//        {
//            var uri = new Uri(url);
//            string host = uri.Host.ToLower();

//            // Usuwamy prefiks www.
//            if (host.StartsWith("www."))
//                host = host.Substring(4);

//            // Opcjonalnie: Wielka litera na początku (np. loombard.pl -> Loombard.pl)
//            if (host.Length > 0)
//                return char.ToUpper(host[0]) + host.Substring(1);

//            return host;
//        }
//        catch
//        {
//            return "Nieznany";
//        }
//    }

//    private static string? ExtractBadge(JsonElement offerData)
//    {
//        var flat = Flatten(offerData, 5).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//        var validBadges = new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" };
//        return flat.FirstOrDefault(s => validBadges.Contains(s));
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth, int currentDepth = 0)
//    {
//        if (currentDepth > maxDepth) yield break;

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//            {
//                yield return element;
//                foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child;
//            }
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//            {
//                yield return property.Value;
//                foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child;
//            }
//        }
//        else
//        {
//            yield return node;
//        }
//    }
//}





// bez dodatkowych katalogow


//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace PriceSafari.Services
//{
//    // Rekord pomocniczy
//    public record TempOffer(
//        string Seller,
//        string Price,
//        string Url,
//        string? Delivery,
//        bool IsInStock,
//        string? Badge,
//        int OriginalIndex,
//        string Method = "OLD",
//        string? RatingScore = null,
//        string? RatingCount = null
//    );

//    public class GoogleMainPriceScraper
//    {
//        // USUNIĘTO: private const bool USE_SMART_Q_MODE = true; <- Już niepotrzebne jako stała

//        // LIMITER: Próg odchylenia ceny dla WRGA (0.8 = 80%)
//        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;

//        private static readonly HttpClient _httpClient;

//        static GoogleMainPriceScraper()
//        {
//            _httpClient = new HttpClient();
//            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
//            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//        }

//        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//        {
//            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();

//            string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

//            if (string.IsNullOrEmpty(catalogId))
//            {
//                Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
//                return finalPriceHistory;
//            }

//            string urlTemplate;

//            // === ZMIANA 1: Logika użycia GPID ===
//            // Warunek: Mamy GID w bazie ORAZ flaga UseGPID jest ustawiona na true
//            if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//            {
//                Console.WriteLine($"[INFO] Używam metody z GPCID dla CID: {catalogId} (UseGPID = true)");
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
//            }
//            else
//            {
//                // Jeśli nie ma GID lub UseGPID jest false -> lecimy po samym CatalogID
//                Console.WriteLine($"[INFO] Metoda standardowa (bez gpcid) dla CID: {catalogId}.");
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
//            }

//            var allFoundOffers = new List<TempOffer>();
//            string? firstPageRawResponse = null;

//            int startIndex = 0;
//            const int pageSize = 10;
//            int lastFetchCount;
//            const int maxRetries = 3;

//            // --- 1. GŁÓWNA PĘTLA POBIERANIA (OAPV - PEWNE DANE) ---
//            do
//            {
//                string currentUrl = string.Format(urlTemplate, startIndex);
//                List<TempOffer> newOffers = new List<TempOffer>();
//                string rawResponse = "";

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {
//                    try
//                    {
//                        rawResponse = await _httpClient.GetStringAsync(currentUrl);
//                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);

//                        // Logowanie i retry logic
//                        if (newOffers.Count == 0)
//                        {
//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[PRÓBA {attempt}/{maxRetries}] Znaleziono 0 produktow. URL: {currentUrl}");

//                            string preview = rawResponse.Length > 500 ? rawResponse.Substring(0, 500) : rawResponse;
//                            preview = preview.Replace("\n", " ").Replace("\r", "");
//                            Console.WriteLine($"[DEBUG TREŚCI]: {preview}...");

//                            Console.ResetColor();
//                        }

//                        if (newOffers.Count > 0)
//                        {
//                            break;
//                        }

//                        if (attempt < maxRetries)
//                        {
//                            Console.ForegroundColor = ConsoleColor.Yellow;
//                            Console.WriteLine($"Ponawiam probe za 2 sekundy... (Próba {attempt} z {maxRetries})");
//                            Console.ResetColor();

//                            await Task.Delay(2000);
//                        }
//                    }
//                    catch (HttpRequestException ex)
//                    {
//                        if (attempt == maxRetries)
//                        {
//                            Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert po {maxRetries} próbach. Błąd: {ex.Message}");
//                        }
//                        else
//                        {
//                            Console.ForegroundColor = ConsoleColor.Yellow;
//                            Console.WriteLine($"Błąd HTTP. Ponawiam probe za 4 sekundy... (Próba {attempt} z {maxRetries})");
//                            Console.ResetColor();
//                            await Task.Delay(4000);
//                        }
//                    }
//                }

//                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse))
//                    firstPageRawResponse = rawResponse;

//                lastFetchCount = newOffers.Count;

//                foreach (var offer in newOffers)
//                {
//                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
//                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url))
//                    {
//                        allFoundOffers.Add(offerWithIndex);
//                    }
//                }

//                startIndex += pageSize;

//                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(500, 800));

//            } while (lastFetchCount == pageSize);

//            // --- 2. TRYB SMART Q (WRGA) Z LIMITEREM CENOWYM ---
//            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse))
//            {
//                Console.WriteLine($"[INFO] Uruchamiam tryb WRGA (Smart Q) dla produktu: {catalogId}");

//                // Obliczamy średnią cenę z OAPV jako punkt odniesienia (Baseline)
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers.Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
//                    if (prices.Any())
//                    {
//                        baselinePrice = prices.Average();
//                    }
//                }

//                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);

//                if (!string.IsNullOrEmpty(productTitle))
//                {
//                    string encodedQ = Uri.EscapeDataString(productTitle);
//                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

//                    try
//                    {
//                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
//                        var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);

//                        foreach (var off in wrgaOffers)
//                        {
//                            if (baselinePrice > 0)
//                            {
//                                decimal wrgaPrice = ParsePrice(off.Price);
//                                decimal diff = wrgaPrice - baselinePrice;
//                                decimal percentageDiff = diff / baselinePrice;

//                                if (percentageDiff < -0.8m || percentageDiff > 2.0m)
//                                {
//                                    Console.ForegroundColor = ConsoleColor.DarkGray;
//                                    Console.WriteLine($"[LIMITER] Odrzucono ofertę WRGA ({off.Seller}). Cena: {wrgaPrice} vs Średnia OAPV: {baselinePrice:F2} (Różnica: {percentageDiff:P0})");
//                                    Console.ResetColor();
//                                    continue;
//                                }
//                            }

//                            if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
//                            {
//                                var organicOffer = off with { OriginalIndex = allFoundOffers.Count + 1 };
//                                allFoundOffers.Add(organicOffer);
//                            }
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"[WARNING] Błąd w trybie Smart Q: {ex.Message}");
//                    }
//                }
//            }


//            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
//            var finalOffersToProcess = new List<(TempOffer offer, int count)>();

//            foreach (var group in groupedBySeller)
//            {
//                int storeOfferCount = group.Count();
//                var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
//                finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
//            }

//            foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
//            {
//                string? isBiddingValue = null;

//                if (!string.IsNullOrEmpty(item.offer.Badge))
//                {
//                    string badgeLower = item.offer.Badge.ToLower();
//                    if (badgeLower.Contains("cena")) isBiddingValue = "bpg";
//                    else if (badgeLower.Contains("popularn") || badgeLower.Contains("wybór")) isBiddingValue = "hpg";
//                }

//                finalPriceHistory.Add(new CoOfrPriceHistoryClass
//                {
//                    GoogleStoreName = item.offer.Seller,
//                    GooglePrice = ParsePrice(item.offer.Price),
//                    GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),
//                    GooglePosition = item.offer.OriginalIndex.ToString(),
//                    IsBidding = isBiddingValue,
//                    GoogleInStock = item.offer.IsInStock,
//                    GoogleOfferPerStoreCount = item.count
//                });
//            }

//            return finalPriceHistory;
//        }

//        #region Helper Methods
//        private bool AreUrlsEqual(string url1, string url2)
//        {
//            if (string.IsNullOrEmpty(url1) || string.IsNullOrEmpty(url2)) return false;

//            string u1 = url1.Contains("?") ? url1.Split('?')[0] : url1;
//            string u2 = url2.Contains("?") ? url2.Split('?')[0] : url2;

//            string norm1 = u1.ToLower().Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
//            string norm2 = u2.ToLower().Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');

//            return norm1 == norm2;
//        }

//        private string? ExtractProductId(string url)
//        {
//            if (string.IsNullOrEmpty(url)) return null;
//            var match = Regex.Match(url, @"product/(\d+)");
//            return match.Success ? match.Groups[1].Value : null;
//        }

//        private decimal ParsePrice(string priceText)
//        {
//            if (string.IsNullOrWhiteSpace(priceText)) return 0;
//            var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".");
//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//            {
//                return result;
//            }
//            return 0;
//        }

//        private decimal ParseDeliveryPrice(string? deliveryText)
//        {
//            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa") || deliveryText.ToLower().Contains("bezpłatnie"))
//            {
//                return 0;
//            }
//            return ParsePrice(deliveryText);
//        }
//        #endregion
//    }

//    public static class GoogleShoppingApiParser
//    {
//        // === PARSER WRGA (HTML) ===
//        public static List<TempOffer> ParseWrga(string rawResponse)
//        {
//            var offers = new List<TempOffer>();
//            if (string.IsNullOrWhiteSpace(rawResponse)) return offers;

//            try
//            {
//                string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//                string decodedContent = Regex.Unescape(cleaned);
//                decodedContent = WebUtility.HtmlDecode(decodedContent);

//                var blockRegex = new Regex(
//                    @"<div[^>]*class=""[^""]*tF2Cxc[^""]*""[^>]*>([\s\S]*?)(?=<div[^>]*class=""[^""]*tF2Cxc|$)",
//                    RegexOptions.IgnoreCase);

//                var blockMatches = blockRegex.Matches(decodedContent);
//                int offerIndex = 0;

//                foreach (Match blockMatch in blockMatches)
//                {
//                    offerIndex++;
//                    string block = blockMatch.Groups[1].Value;

//                    var urlMatch = Regex.Match(block, @"href=""(https?://[^""]+)""");
//                    if (!urlMatch.Success) continue;

//                    string url = urlMatch.Groups[1].Value;
//                    if (IsGoogleLink(url)) continue;

//                    string seller = GetDomainName(url);
//                    decimal priceVal = 0;
//                    decimal deliveryVal = 0;
//                    string badge = "ORGANIC";

//                    priceVal = ExtractRichSnippetPrice(block);

//                    if (priceVal > 0)
//                    {
//                        deliveryVal = ExtractDeliveryFromRichSnippet(block);
//                        badge = "RICH_SNIPPET";
//                    }
//                    else
//                    {
//                        string cleanText = StripHtml(block);
//                        var analysisResult = AnalyzePricesInBlock(cleanText);
//                        priceVal = analysisResult.Price;
//                        deliveryVal = analysisResult.Delivery;
//                        badge = "TEXT_ANALYSIS";
//                    }

//                    if (priceVal > 0)
//                    {
//                        string finalPrice = priceVal.ToString("F2");
//                        string deliveryStr = deliveryVal > 0 ? deliveryVal.ToString("F2") : "0";

//                        offers.Add(new TempOffer(
//                            seller,
//                            finalPrice,
//                            url,
//                            deliveryStr,
//                            true,
//                            badge,
//                            offerIndex,
//                            "WRGA"
//                        ));
//                    }
//                }
//            }
//            catch { }
//            return offers;
//        }

//        // === PARSER OAPV (JSON) ===
//        public static List<TempOffer> ParseOapv(string rawResponse)
//        {
//            if (string.IsNullOrWhiteSpace(rawResponse)) return new List<TempOffer>();

//            try
//            {
//                string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//                using JsonDocument doc = JsonDocument.Parse(cleanedJson);
//                JsonElement root = doc.RootElement.Clone();

//                var allOffers = new List<TempOffer>();
//                FindAndParseAllOffers(root, root, allOffers);
//                return allOffers;
//            }
//            catch (JsonException)
//            {
//                return new List<TempOffer>();
//            }
//        }

//        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//        {
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                if (IsPotentialSingleOffer(node))
//                {
//                    foreach (JsonElement potentialOffer in node.EnumerateArray())
//                    {
//                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                        {
//                            allOffers.Add(offer);
//                        }
//                    }
//                }

//                foreach (var element in node.EnumerateArray())
//                {
//                    FindAndParseAllOffers(root, element, allOffers);
//                }
//            }
//            else if (node.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in node.EnumerateObject())
//                {
//                    FindAndParseAllOffers(root, property.Value, allOffers);
//                }
//            }
//        }

//        private static bool IsPotentialSingleOffer(JsonElement node)
//        {
//            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0)
//            {
//                var flatStrings = Flatten(node, 3).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//                if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google"))) return true;
//            }
//            return false;
//        }

//        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//        {
//            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
//            if (offerData.ValueKind != JsonValueKind.Array) return null;

//            try
//            {
//                var flatStrings = Flatten(offerData, 20).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//                var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) return null;

//                bool isInStock = true;
//                var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
//                if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false;

//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));

//                var oapvRegex = new Regex(@"\d[\d\s,.]*\s*(?:PLN|zł|EUR|USD)", RegexOptions.IgnoreCase);
//                string? price = flatStrings.FirstOrDefault(s => oapvRegex.IsMatch(s) && !s.Trim().StartsWith("+"));

//                string? seller = null;
//                var offerElements = offerData.EnumerateArray().ToList();
//                for (int i = 0; i < offerElements.Count - 1; i++)
//                {
//                    if (offerElements[i].ValueKind == JsonValueKind.Number && offerElements[i + 1].ValueKind == JsonValueKind.String)
//                    {
//                        string potentialSeller = offerElements[i + 1].GetString()!;
//                        if (!potentialSeller.StartsWith("http") && !oapvRegex.IsMatch(potentialSeller) && potentialSeller.Length > 2) { seller = potentialSeller; break; }
//                    }
//                }
//                if (seller == null && url != null) seller = GetDomainName(url);

//                string? delivery = null;
//                if (flatStrings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase)))
//                    delivery = "Bezpłatna";
//                else
//                {
//                    string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && oapvRegex.IsMatch(s));
//                    if (rawDeliveryText != null) { Match m = oapvRegex.Match(rawDeliveryText); if (m.Success) delivery = m.Value.Trim(); }
//                    else
//                    {
//                        string? alt = flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) && oapvRegex.IsMatch(s));
//                        if (alt != null) { Match m = oapvRegex.Match(alt); if (m.Success) delivery = m.Value.Trim(); }
//                    }
//                }

//                string? badge = ExtractBadgeStrict(offerData);
//                if (string.IsNullOrEmpty(badge))
//                {
//                    string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" };
//                    badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase)));
//                }

//                var ratingData = ExtractStoreRating(offerData);

//                if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
//                {
//                    return new TempOffer(seller, price, url, delivery, isInStock, badge, 0, "OAPV", ratingData.Score, ratingData.Count);
//                }
//            }
//            catch { }
//            return null;
//        }

//        public static string? ExtractProductTitle(string rawResponse)
//        {
//            try
//            {
//                string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//                using JsonDocument doc = JsonDocument.Parse(cleaned);
//                if (doc.RootElement.TryGetProperty("ProductDetailsResult", out JsonElement pd) && pd.GetArrayLength() > 0) return pd[0].GetString();
//            }
//            catch { }
//            return null;
//        }

//        private static string GetDomainName(string url)
//        {
//            try
//            {
//                var host = new Uri(url).Host.ToLower().Replace("www.", "");
//                return char.ToUpper(host[0]) + host.Substring(1);
//            }
//            catch { return "Nieznany"; }
//        }

//        private static bool IsGoogleLink(string url)
//        {
//            return url.Contains(".google.") || url.Contains("gstatic.") ||
//                  url.Contains("/search?") || url.Contains("youtube.") ||
//                  url.Contains("googleusercontent") || url.Contains("translate.google");
//        }

//        private static string? ExtractBadgeStrict(JsonElement offerData)
//        {
//            try
//            {
//                foreach (var element in offerData.EnumerateArray())
//                {
//                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
//                    {
//                        var inner = element[0];
//                        if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0)
//                        {
//                            var potentialBadgeNode = inner[0];
//                            if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1)
//                            {
//                                if (potentialBadgeNode[0].ValueKind == JsonValueKind.String)
//                                {
//                                    string text = potentialBadgeNode[0].GetString()!;
//                                    if (new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))) return text;
//                                }
//                            }
//                        }
//                    }
//                }
//            }
//            catch { }
//            return null;
//        }

//        private static (string? Score, string? Count) ExtractStoreRating(JsonElement offerData)
//        {
//            try
//            {
//                var stack = new Stack<JsonElement>(); stack.Push(offerData);
//                while (stack.Count > 0)
//                {
//                    var current = stack.Pop();
//                    if (current.ValueKind == JsonValueKind.Array)
//                    {
//                        if (current.GetArrayLength() >= 2 && current[0].ValueKind == JsonValueKind.String && current[1].ValueKind == JsonValueKind.String)
//                        {
//                            string s1 = current[0].GetString()!;
//                            if (s1.Contains("/5") && s1.Length < 10) return (s1, current[1].GetString()!);
//                        }
//                        foreach (var element in current.EnumerateArray()) if (element.ValueKind == JsonValueKind.Array) stack.Push(element);
//                    }
//                }
//            }
//            catch { }
//            return (null, null);
//        }

//        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0)
//        {
//            if (currentDepth > maxDepth) yield break;
//            var stack = new Stack<JsonElement>(); stack.Push(node);
//            while (stack.Count > 0)
//            {
//                var current = stack.Pop();
//                if (current.ValueKind == JsonValueKind.Array) foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//                else if (current.ValueKind == JsonValueKind.Object) foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//                else yield return current;
//            }
//        }

//        private static decimal ExtractRichSnippetPrice(string htmlBlock)
//        {
//            var richPriceRegex = new Regex(
//                @"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>",
//                RegexOptions.IgnoreCase);
//            var match = richPriceRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock)
//        {
//            var deliveryRegex = new Regex(
//                @"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)",
//                RegexOptions.IgnoreCase);
//            var match = deliveryRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text)
//        {
//            var priceRegex = new Regex(
//                @"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?",
//                RegexOptions.IgnoreCase);

//            var matches = priceRegex.Matches(text);
//            decimal bestPrice = 0;
//            decimal bestDelivery = 0;

//            var candidates = new List<(decimal val, int index, int score)>();

//            foreach (Match m in matches)
//            {
//                string numStr = m.Groups[1].Value;
//                if (!decimal.TryParse(numStr.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue;

//                bool hasCurrency = m.Value.Contains("zł") || m.Value.Contains("PLN");
//                bool hasPriceKeyword = m.Value.ToLower().Contains("cena") || m.Value.ToLower().Contains("za");

//                if (val < 1.0m && !hasCurrency && !hasPriceKeyword) continue;

//                int contextStart = Math.Max(0, m.Index - 30);
//                int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20);
//                string fullMatchContext = text.Substring(contextStart, contextLen).ToLower();

//                if (Regex.IsMatch(fullMatchContext, @"(dostawa|wysyłka|delivery|\+)"))
//                {
//                    if (bestDelivery == 0) bestDelivery = val;
//                    continue;
//                }

//                int score = 0;
//                if (hasPriceKeyword) score += 50;
//                if (hasCurrency) score += 10;
//                if (fullMatchContext.Contains("brutto")) score += 5;
//                if (fullMatchContext.Contains("netto")) score += 2;
//                if (val > 1900 && val < 2100 && val % 1 == 0 && !hasCurrency) continue;

//                candidates.Add((val, m.Index, score));
//            }

//            var winner = candidates.OrderByDescending(x => x.score).ThenBy(x => x.index).FirstOrDefault();
//            if (winner.val > 0) bestPrice = winner.val;

//            return (bestPrice, bestDelivery);
//        }

//        private static string StripHtml(string html)
//        {
//            if (string.IsNullOrEmpty(html)) return "";
//            string s = html.Replace("<br>", " ").Replace("<br/>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " ").Replace("</em>", " ").Replace("</a>", " ");
//            s = Regex.Replace(s, "<.*?>", " ");
//            s = WebUtility.HtmlDecode(s);
//            s = Regex.Replace(s, @"\s+", " ").Trim();
//            return s;
//        }

//        private static decimal ParsePriceDecimal(string priceStr)
//        {
//            if (string.IsNullOrEmpty(priceStr)) return 0;
//            string clean = Regex.Replace(priceStr, @"[^\d,.]", "");

//            if (clean.Contains(",") && clean.Contains("."))
//            {
//                if (clean.LastIndexOf(',') > clean.LastIndexOf('.')) clean = clean.Replace(".", "").Replace(",", ".");
//                else clean = clean.Replace(",", "");
//            }
//            else if (clean.Contains(",")) clean = clean.Replace(",", ".");

//            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res;
//            return 0;
//        }
//    }
//}










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

namespace PriceSafari.Services
{
    // Rekord pomocniczy
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
        string? RatingCount = null
    );

    public class GoogleMainPriceScraper
    {
        // USUNIĘTO: private const bool USE_SMART_Q_MODE = true; <- Już niepotrzebne jako stała

        // LIMITER: Próg odchylenia ceny dla WRGA (0.8 = 80%)
        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;

        private static readonly HttpClient _httpClient;

        static GoogleMainPriceScraper()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
        {
            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
            string urlTemplate;

            // --- NOWA LOGIKA BUDOWANIA URL (Obsługa HID / CID) ---
            // Najpierw sprawdzamy czy zadanie wymusza tryb HID
            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
            {
                Console.WriteLine($"[INFO] Używam metody HID dla GID: {coOfr.GoogleGid} | HID: {coOfr.GoogleHid} (UseGoogleHidOffer = true)");
                // W trybie HID zawsze wymagane jest gpcid oraz headlineOfferDocid zamiast catalogid
                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
            }
            else
            {
                // Tryb standardowy (Katalogowy) - dotychczasowa logika
                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);

                if (string.IsNullOrEmpty(catalogId))
                {
                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
                    return finalPriceHistory;
                }

                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
                {
                    Console.WriteLine($"[INFO] Używam metody z GPCID dla CID: {catalogId} (UseGPID = true)");
                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
                }
                else
                {
                    Console.WriteLine($"[INFO] Metoda standardowa (bez gpcid) dla CID: {catalogId}.");
                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
                }
            }
            // --- KONIEC ZMIAN W BUDOWANIU URL ---


            // --- RESZTA LOGIKI BEZ ZMIAN (1:1) ---
            var allFoundOffers = new List<TempOffer>();
            string? firstPageRawResponse = null;

            int startIndex = 0;
            const int pageSize = 10;
            int lastFetchCount;
            const int maxRetries = 3;

            // --- 1. GŁÓWNA PĘTLA POBIERANIA (OAPV - PEWNE DANE) ---
            do
            {
                string currentUrl = string.Format(urlTemplate, startIndex);
                List<TempOffer> newOffers = new List<TempOffer>();
                string rawResponse = "";

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        rawResponse = await _httpClient.GetStringAsync(currentUrl);
                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);

                        // Logowanie i retry logic
                        if (newOffers.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[PRÓBA {attempt}/{maxRetries}] Znaleziono 0 produktow. URL: {currentUrl}");

                            string preview = rawResponse.Length > 500 ? rawResponse.Substring(0, 500) : rawResponse;
                            preview = preview.Replace("\n", " ").Replace("\r", "");
                            Console.WriteLine($"[DEBUG TREŚCI]: {preview}...");

                            Console.ResetColor();
                        }

                        if (newOffers.Count > 0)
                        {
                            break;
                        }

                        if (attempt < maxRetries)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Ponawiam probe za 2 sekundy... (Próba {attempt} z {maxRetries})");
                            Console.ResetColor();

                            await Task.Delay(2000);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        if (attempt == maxRetries)
                        {
                            Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert po {maxRetries} próbach. Błąd: {ex.Message}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Błąd HTTP. Ponawiam probe za 4 sekundy... (Próba {attempt} z {maxRetries})");
                            Console.ResetColor();
                            await Task.Delay(4000);
                        }
                    }
                }

                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse))
                    firstPageRawResponse = rawResponse;

                lastFetchCount = newOffers.Count;

                foreach (var offer in newOffers)
                {
                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url))
                    {
                        allFoundOffers.Add(offerWithIndex);
                    }
                }

                startIndex += pageSize;

                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(500, 800));

            } while (lastFetchCount == pageSize);

            // --- 2. TRYB SMART Q (WRGA) Z LIMITEREM CENOWYM ---
            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse))
            {
                // Ustalenie catalogId do logów WRGA
                string? catalogIdForLog = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);
                Console.WriteLine($"[INFO] Uruchamiam tryb WRGA (Smart Q) dla produktu: {catalogIdForLog}");

                // Obliczamy średnią cenę z OAPV jako punkt odniesienia (Baseline)
                decimal baselinePrice = 0;
                if (allFoundOffers.Any())
                {
                    var prices = allFoundOffers.Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
                    if (prices.Any())
                    {
                        // Mediana jest odporna na błędy parsowania (outliery)
                        var sortedPrices = prices.OrderBy(p => p).ToList();
                        baselinePrice = sortedPrices[sortedPrices.Count / 2];
                        Console.WriteLine($"[DEBUG] Baseline (Mediana) dla zadania {coOfr.Id}: {baselinePrice:F2} zł");
                    }
                }

                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);

                if (!string.IsNullOrEmpty(productTitle))
                {
                    string encodedQ = Uri.EscapeDataString(productTitle);
                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

                    try
                    {
                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
                        var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);

                        foreach (var off in wrgaOffers)
                        {
                            if (baselinePrice > 0)
                            {
                                decimal wrgaPrice = ParsePrice(off.Price);
                                decimal diff = wrgaPrice - baselinePrice;
                                decimal percentageDiff = diff / baselinePrice;

                                if (percentageDiff < -0.8m || percentageDiff > 2.0m)
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine($"[LIMITER] Odrzucono ofertę WRGA ({off.Seller}). Cena: {wrgaPrice} vs Średnia OAPV: {baselinePrice:F2} (Różnica: {percentageDiff:P0})");
                                    Console.ResetColor();
                                    continue;
                                }
                            }

                            if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
                            {
                                var organicOffer = off with { OriginalIndex = allFoundOffers.Count + 1 };
                                allFoundOffers.Add(organicOffer);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARNING] Błąd w trybie Smart Q: {ex.Message}");
                    }
                }
            }

            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
            var finalOffersToProcess = new List<(TempOffer offer, int count)>();

            foreach (var group in groupedBySeller)
            {
                int storeOfferCount = group.Count();
                var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
                finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
            }

            foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
            {
                string? isBiddingValue = null;

                if (!string.IsNullOrEmpty(item.offer.Badge))
                {
                    string badgeLower = item.offer.Badge.ToLower();
                    if (badgeLower.Contains("cena")) isBiddingValue = "bpg";
                    else if (badgeLower.Contains("popularn") || badgeLower.Contains("wybór")) isBiddingValue = "hpg";
                }

                finalPriceHistory.Add(new CoOfrPriceHistoryClass
                {
                    CoOfrClassId = coOfr.Id, // Przypisanie ID zadania
                    GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl), 
                    GoogleStoreName = item.offer.Seller,
                    GooglePrice = ParsePrice(item.offer.Price),
                    GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),
                    GooglePosition = item.offer.OriginalIndex.ToString(),
                    IsBidding = isBiddingValue,
                    GoogleInStock = item.offer.IsInStock,
                    GoogleOfferPerStoreCount = item.count
                });
            }

            return finalPriceHistory;
        }
        #region Helper Methods
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
            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }
            return 0;
        }

        private decimal ParseDeliveryPrice(string? deliveryText)
        {
            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa") || deliveryText.ToLower().Contains("bezpłatnie"))
            {
                return 0;
            }
            return ParsePrice(deliveryText);
        }
        #endregion
    }

    public static class GoogleShoppingApiParser
    {
        // === PARSER WRGA (HTML) ===
        public static List<TempOffer> ParseWrga(string rawResponse)
        {
            var offers = new List<TempOffer>();
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

                    priceVal = ExtractRichSnippetPrice(block);

                    if (priceVal > 0)
                    {
                        deliveryVal = ExtractDeliveryFromRichSnippet(block);
                        badge = "RICH_SNIPPET";
                    }
                    else
                    {
                        string cleanText = StripHtml(block);
                        var analysisResult = AnalyzePricesInBlock(cleanText);
                        priceVal = analysisResult.Price;
                        deliveryVal = analysisResult.Delivery;
                        badge = "TEXT_ANALYSIS";
                    }

                    if (priceVal > 0)
                    {
                        string finalPrice = priceVal.ToString("F2");
                        string deliveryStr = deliveryVal > 0 ? deliveryVal.ToString("F2") : "0";

                        offers.Add(new TempOffer(
                            seller,
                            finalPrice,
                            url,
                            deliveryStr,
                            true,
                            badge,
                            offerIndex,
                            "WRGA"
                        ));
                    }
                }
            }
            catch { }
            return offers;
        }

        // === PARSER OAPV (JSON) ===
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
            catch (JsonException)
            {
                return new List<TempOffer>();
            }
        }

        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                if (IsPotentialSingleOffer(node))
                {
                    foreach (JsonElement potentialOffer in node.EnumerateArray())
                    {
                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
                        {
                            allOffers.Add(offer);
                        }
                    }
                }

                foreach (var element in node.EnumerateArray())
                {
                    FindAndParseAllOffers(root, element, allOffers);
                }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    FindAndParseAllOffers(root, property.Value, allOffers);
                }
            }
        }

        private static bool IsPotentialSingleOffer(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0)
            {
                var flatStrings = Flatten(node, 3).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
                if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google"))) return true;
            }
            return false;
        }

        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
        {
            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
            if (offerData.ValueKind != JsonValueKind.Array) return null;

            try
            {
                // Pobieramy wszystkie węzły (potrzebujemy dostępu do Number i String)
                var allNodes = Flatten(offerData, 20).ToList();
                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

                // Filtry stanu
                var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
                if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) return null;

                // Filtry dostępności
                bool isInStock = true;
                var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
                if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false;

                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));

                // --- LOGIKA CENY (PRODUKCYJNA) ---
                string? price = null;

                // KROK A: Szukamy Micros (Number)
                foreach (var node in allNodes)
                {
                    if (node.ValueKind == JsonValueKind.Number)
                    {
                        long val = node.GetInt64();
                        // Walidujemy Micros: zazwyczaj kończą się na 0000 i są w sensownym zakresie
                        if (val >= 1000000 && val < 1000000000000 && val % 10000 == 0)
                        {
                            price = (val / 1000000m).ToString("F2", CultureInfo.InvariantCulture);
                            break;
                        }
                    }
                }

                // KROK B: Fallback do stringów (Regex)
                var oapvRegex = new Regex(@"\d[\d\s,.]*\s*(?:PLN|zł|EUR|USD)", RegexOptions.IgnoreCase);
                if (string.IsNullOrEmpty(price))
                {
                    price = flatStrings.FirstOrDefault(s => oapvRegex.IsMatch(s) && !s.Trim().StartsWith("+"));
                }

                // --- KONIEC LOGIKI CENY ---
                string? seller = null;

                // KROK A: Próbujemy wyciągnąć nazwę z domeny URL (Najbezpieczniejsza opcja dla Twoich potrzeb)
                if (!string.IsNullOrEmpty(url))
                {
                    seller = GetDomainName(url);
                }

                // KROK B: (Opcjonalny Fallback) Jeśli domena zawiedzie, szukamy w JSON, 
                // ale wykluczamy waluty i inne "śmieci"
                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
                {
                    var offerElements = offerData.EnumerateArray().ToList();
                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };

                    for (int i = 0; i < offerElements.Count - 1; i++)
                    {
                        if (offerElements[i].ValueKind == JsonValueKind.Number && offerElements[i + 1].ValueKind == JsonValueKind.String)
                        {
                            string potential = offerElements[i + 1].GetString()!;

                            bool isBlacklisted = blacklist.Any(b => string.Equals(potential, b, StringComparison.OrdinalIgnoreCase));

                            if (!potential.StartsWith("http") && !isBlacklisted && !oapvRegex.IsMatch(potential) && potential.Length > 2)
                            {
                                seller = potential;
                                break;
                            }
                        }
                    }
                }
                if (seller == null && url != null) seller = GetDomainName(url);

                string? delivery = null;
                if (flatStrings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase)))
                    delivery = "Bezpłatna";
                else
                {
                    string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && oapvRegex.IsMatch(s));
                    if (rawDeliveryText != null) { Match m = oapvRegex.Match(rawDeliveryText); if (m.Success) delivery = m.Value.Trim(); }
                    else
                    {
                        string? alt = flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) && oapvRegex.IsMatch(s));
                        if (alt != null) { Match m = oapvRegex.Match(alt); if (m.Success) delivery = m.Value.Trim(); }
                    }
                }

                string? badge = ExtractBadgeStrict(offerData);
                if (string.IsNullOrEmpty(badge))
                {
                    string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" };
                    badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase)));
                }

                var ratingData = ExtractStoreRating(offerData);

                if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
                {
                    return new TempOffer(seller, price, url, delivery, isInStock, badge, 0, "OAPV", ratingData.Score, ratingData.Count);
                }
            }
            catch { }
            return null;
        }

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

        private static string GetDomainName(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLower().Replace("www.", "");
                return char.ToUpper(host[0]) + host.Substring(1);
            }
            catch { return "Nieznany"; }
        }

        private static bool IsGoogleLink(string url)
        {
            return url.Contains(".google.") || url.Contains("gstatic.") ||
                  url.Contains("/search?") || url.Contains("youtube.") ||
                  url.Contains("googleusercontent") || url.Contains("translate.google");
        }

        private static string? ExtractBadgeStrict(JsonElement offerData)
        {
            try
            {
                foreach (var element in offerData.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                    {
                        var inner = element[0];
                        if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0)
                        {
                            var potentialBadgeNode = inner[0];
                            if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1)
                            {
                                if (potentialBadgeNode[0].ValueKind == JsonValueKind.String)
                                {
                                    string text = potentialBadgeNode[0].GetString()!;
                                    if (new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))) return text;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static (string? Score, string? Count) ExtractStoreRating(JsonElement offerData)
        {
            try
            {
                var stack = new Stack<JsonElement>(); stack.Push(offerData);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (current.ValueKind == JsonValueKind.Array)
                    {
                        if (current.GetArrayLength() >= 2 && current[0].ValueKind == JsonValueKind.String && current[1].ValueKind == JsonValueKind.String)
                        {
                            string s1 = current[0].GetString()!;
                            if (s1.Contains("/5") && s1.Length < 10) return (s1, current[1].GetString()!);
                        }
                        foreach (var element in current.EnumerateArray()) if (element.ValueKind == JsonValueKind.Array) stack.Push(element);
                    }
                }
            }
            catch { }
            return (null, null);
        }

        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) yield break;
            var stack = new Stack<JsonElement>(); stack.Push(node);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current.ValueKind == JsonValueKind.Array) foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
                else if (current.ValueKind == JsonValueKind.Object) foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
                else yield return current;
            }
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
    }
}