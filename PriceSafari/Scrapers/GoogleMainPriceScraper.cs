

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
//            string urlTemplate;

//            // --- NOWA LOGIKA BUDOWANIA URL (Obsługa HID / CID) ---
//            // Najpierw sprawdzamy czy zadanie wymusza tryb HID
//            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
//            {
//                Console.WriteLine($"[INFO] Używam metody HID dla GID: {coOfr.GoogleGid} | HID: {coOfr.GoogleHid} (UseGoogleHidOffer = true)");
//                // W trybie HID zawsze wymagane jest gpcid oraz headlineOfferDocid zamiast catalogid
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
//            }
//            else
//            {
//                // Tryb standardowy (Katalogowy) - dotychczasowa logika
//                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);

//                if (string.IsNullOrEmpty(catalogId))
//                {
//                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
//                    return finalPriceHistory;
//                }

//                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//                {
//                    Console.WriteLine($"[INFO] Używam metody z GPCID dla CID: {catalogId} (UseGPID = true)");
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
//                }
//                else
//                {
//                    Console.WriteLine($"[INFO] Metoda standardowa (bez gpcid) dla CID: {catalogId}.");
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
//                }
//            }
//            // --- KONIEC ZMIAN W BUDOWANIU URL ---


//            // --- RESZTA LOGIKI BEZ ZMIAN (1:1) ---
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
//                // Ustalenie catalogId do logów WRGA
//                string? catalogIdForLog = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);
//                Console.WriteLine($"[INFO] Uruchamiam tryb WRGA (Smart Q) dla produktu: {catalogIdForLog}");

//                // Obliczamy średnią cenę z OAPV jako punkt odniesienia (Baseline)
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers.Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
//                    if (prices.Any())
//                    {
//                        // Mediana jest odporna na błędy parsowania (outliery)
//                        var sortedPrices = prices.OrderBy(p => p).ToList();
//                        baselinePrice = sortedPrices[sortedPrices.Count / 2];
//                        Console.WriteLine($"[DEBUG] Baseline (Mediana) dla zadania {coOfr.Id}: {baselinePrice:F2} zł");
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
//                    CoOfrClassId = coOfr.Id, // Przypisanie ID zadania
//                    GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl), 
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
//                // Pobieramy wszystkie węzły (potrzebujemy dostępu do Number i String)
//                var allNodes = Flatten(offerData, 20).ToList();
//                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//                // Filtry stanu
//                var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) return null;

//                // Filtry dostępności
//                bool isInStock = true;
//                var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
//                if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false;

//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));

//                // --- LOGIKA CENY (PRODUKCYJNA) ---
//                string? price = null;

//                // KROK A: Szukamy Micros (Number)
//                foreach (var node in allNodes)
//                {
//                    if (node.ValueKind == JsonValueKind.Number)
//                    {
//                        long val = node.GetInt64();
//                        // Walidujemy Micros: zazwyczaj kończą się na 0000 i są w sensownym zakresie
//                        if (val >= 1000000 && val < 1000000000000 && val % 10000 == 0)
//                        {
//                            price = (val / 1000000m).ToString("F2", CultureInfo.InvariantCulture);
//                            break;
//                        }
//                    }
//                }

//                // KROK B: Fallback do stringów (Regex)
//                var oapvRegex = new Regex(@"\d[\d\s,.]*\s*(?:PLN|zł|EUR|USD)", RegexOptions.IgnoreCase);
//                if (string.IsNullOrEmpty(price))
//                {
//                    price = flatStrings.FirstOrDefault(s => oapvRegex.IsMatch(s) && !s.Trim().StartsWith("+"));
//                }

//                // --- KONIEC LOGIKI CENY ---
//                string? seller = null;

//                // KROK A: Próbujemy wyciągnąć nazwę z domeny URL (Najbezpieczniejsza opcja dla Twoich potrzeb)
//                if (!string.IsNullOrEmpty(url))
//                {
//                    seller = GetDomainName(url);
//                }

//                // KROK B: (Opcjonalny Fallback) Jeśli domena zawiedzie, szukamy w JSON, 
//                // ale wykluczamy waluty i inne "śmieci"
//                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
//                {
//                    var offerElements = offerData.EnumerateArray().ToList();
//                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };

//                    for (int i = 0; i < offerElements.Count - 1; i++)
//                    {
//                        if (offerElements[i].ValueKind == JsonValueKind.Number && offerElements[i + 1].ValueKind == JsonValueKind.String)
//                        {
//                            string potential = offerElements[i + 1].GetString()!;

//                            bool isBlacklisted = blacklist.Any(b => string.Equals(potential, b, StringComparison.OrdinalIgnoreCase));

//                            if (!potential.StartsWith("http") && !isBlacklisted && !oapvRegex.IsMatch(potential) && potential.Length > 2)
//                            {
//                                seller = potential;
//                                break;
//                            }
//                        }
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
//        string? RatingCount = null,
//        string Condition = "NOWY",
//        string Currency = "PLN"
//    );

//    public class GoogleMainPriceScraper
//    {
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

//            var allDebugOffers = new List<TempOffer>();

//            Console.WriteLine($"\n[INFO] Start scrapowania dla ID: {coOfr.Id}...");

//            string urlTemplate;

//            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
//            {
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }
//            else
//            {
//                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);

//                if (string.IsNullOrEmpty(catalogId))
//                {
//                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
//                    return finalPriceHistory;
//                }

//                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//                {
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                }
//                else
//                {
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                }
//            }

//            var allFoundOffers = new List<TempOffer>();
//            string? firstPageRawResponse = null;

//            int startIndex = 0;
//            const int pageSize = 10;
//            int lastFetchCount;
//            const int maxRetries = 3;

//            do
//            {
//                string currentUrl = string.Format(urlTemplate, startIndex);
//                List<TempOffer> newOffers = new List<TempOffer>();
//                string rawResponse = "";

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {
//                    try
//                    {

//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] Wysłanie żądania OAPV (Start: {startIndex}):");
//                        Console.WriteLine($"   URL: {currentUrl}");
//                        Console.ResetColor();

//                        rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                        Console.ForegroundColor = ConsoleColor.DarkGray;
//                        int previewLength = Math.Min(500, rawResponse.Length);
//                        Console.WriteLine($"   [RESPONSE] Długość: {rawResponse.Length} znaków.");
//                        Console.WriteLine($"   [PREVIEW]: {rawResponse.Substring(0, previewLength)}...");
//                        Console.ResetColor();

//                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);

//                        if (newOffers.Count > 0) break;
//                        if (attempt < maxRetries)
//                        {
//                            Console.WriteLine("   [RETRY] Ponawiam próbę...");
//                            await Task.Delay(2000);
//                        }
//                    }
//                    catch (HttpRequestException ex)
//                    {
//                        Console.WriteLine($"   [ERROR] Błąd HTTP: {ex.Message}");
//                        if (attempt == maxRetries) Console.WriteLine($"[BŁĄD KRYTYCZNY] {ex.Message}");
//                        else await Task.Delay(4000);
//                    }
//                }

//                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse))
//                    firstPageRawResponse = rawResponse;

//                lastFetchCount = newOffers.Count;
//                Console.WriteLine($"   [INFO] Znaleziono ofert na stronie: {lastFetchCount}");

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

//            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse))
//            {
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers
//                        .Where(o => o.Currency == "PLN")
//                        .Select(o => ParsePrice(o.Price))
//                        .Where(p => p > 0)
//                        .ToList();

//                    if (prices.Any())
//                    {
//                        var sortedPrices = prices.OrderBy(p => p).ToList();
//                        baselinePrice = sortedPrices[sortedPrices.Count / 2];
//                        Console.WriteLine($"\n[INFO] Mediana cen z OAPV (Baseline): {baselinePrice:F2} zł");
//                    }
//                }

//                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);

//                if (!string.IsNullOrEmpty(productTitle))
//                {
//                    string encodedQ = Uri.EscapeDataString(productTitle);
//                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

//                    try
//                    {
//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] Wysłanie żądania WRGA (Smart Q):");
//                        Console.WriteLine($"   URL: {wrgaUrl}");
//                        Console.ResetColor();

//                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);

//                        Console.ForegroundColor = ConsoleColor.DarkGray;
//                        int previewLength = Math.Min(500, wrgaResponse.Length);
//                        Console.WriteLine($"   [RESPONSE] Długość: {wrgaResponse.Length} znaków.");
//                        Console.WriteLine($"   [PREVIEW]: {wrgaResponse.Substring(0, previewLength)}...");
//                        Console.ResetColor();

//                        var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);
//                        Console.WriteLine($"   [INFO] Znaleziono ofert WRGA: {wrgaOffers.Count}");

//                        foreach (var off in wrgaOffers)
//                        {
//                            if (baselinePrice > 0 && off.Currency == "PLN")
//                            {
//                                decimal wrgaPrice = ParsePrice(off.Price);
//                                decimal diff = wrgaPrice - baselinePrice;
//                                decimal percentageDiff = diff / baselinePrice;

//                                if (percentageDiff < -0.8m || percentageDiff > 2.0m)
//                                {
//                                    Console.ForegroundColor = ConsoleColor.Red;
//                                    Console.WriteLine($"\n[LIMITER - ODZRUT PROD] Odrzucono ofertę WRGA: {off.Seller}");
//                                    Console.WriteLine($"   Powód: Cena {wrgaPrice:F2} odbiega o {percentageDiff:P0} od średniej {baselinePrice:F2}");
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
//                        Console.WriteLine($"   [ERROR] Błąd WRGA: {ex.Message}");
//                    }
//                }
//            }

//            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);

//            var debugMainList = new List<dynamic>();
//            var debugAdditionalList = new List<dynamic>();

//            foreach (var group in groupedBySeller)
//            {

//                var bestValidOffer = group
//                    .Where(o => o.Condition == "NOWY" && o.Currency == "PLN")
//                    .OrderBy(o => ParsePrice(o.Price))
//                    .FirstOrDefault();

//                var sortedStoreOffers = group.OrderBy(o => ParsePrice(o.Price)).ToList();

//                foreach (var offer in sortedStoreOffers)
//                {
//                    bool isBest = (bestValidOffer != null && offer == bestValidOffer);

//                    if (isBest)
//                    {
//                        string? isBiddingValue = null;
//                        if (!string.IsNullOrEmpty(offer.Badge))
//                        {
//                            string badgeLower = offer.Badge.ToLower();
//                            if (badgeLower.Contains("cena")) isBiddingValue = "bpg";
//                            else if (badgeLower.Contains("popularn") || badgeLower.Contains("wybór")) isBiddingValue = "hpg";
//                        }

//                        finalPriceHistory.Add(new CoOfrPriceHistoryClass
//                        {
//                            CoOfrClassId = coOfr.Id,
//                            GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl),
//                            GoogleStoreName = offer.Seller,
//                            GooglePrice = ParsePrice(offer.Price),
//                            GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                            GooglePosition = offer.OriginalIndex.ToString(),
//                            IsBidding = isBiddingValue,
//                            GoogleInStock = offer.IsInStock,
//                            GoogleOfferPerStoreCount = group.Count()
//                        });
//                    }

//                    var debugItem = new
//                    {
//                        Pos = isBest ? offer.OriginalIndex.ToString() : "-",
//                        GPos = offer.OriginalIndex,
//                        Stock = offer.IsInStock ? "OK" : "BRAK",
//                        Cond = offer.Condition,
//                        Curr = offer.Currency,
//                        Info = offer.Badge ?? "-",
//                        Method = offer.Method,
//                        Seller = offer.Seller,
//                        Price = ParsePrice(offer.Price),
//                        Del = ParseDeliveryPrice(offer.Delivery),
//                        Total = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                        Url = offer.Url,
//                        IsMain = isBest
//                    };

//                    if (isBest) debugMainList.Add(debugItem);
//                    else debugAdditionalList.Add(debugItem);
//                }
//            }

//            finalPriceHistory = finalPriceHistory.OrderBy(x => x.GooglePrice).ToList();

//            debugMainList = debugMainList.OrderBy(x => x.Price).ToList();

//            for (int i = 0; i < debugMainList.Count; i++)
//            {

//                var old = debugMainList[i];
//                debugMainList[i] = new { old.Pos, old.GPos, old.Stock, old.Cond, old.Curr, old.Info, old.Method, old.Seller, old.Price, old.Del, old.Total, old.Url, old.IsMain, ListPos = (i + 1).ToString() };
//            }

//            debugAdditionalList = debugAdditionalList.OrderBy(x => x.Seller).ThenBy(x => x.Price).ToList();

//            Console.WriteLine("\n===============================================================================================================================================================================");
//            Console.WriteLine($" TABELA GŁÓWNA: Najlepsze oferty (NOWE, PLN) (Liczba: {debugMainList.Count})");
//            Console.WriteLine("===============================================================================================================================================================================");
//            Console.WriteLine($"{"Poz.",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");

//            foreach (var item in debugMainList) PrintDebugRow(item, item.ListPos);

//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");

//            if (debugAdditionalList.Any())
//            {
//                Console.WriteLine("\n");
//                Console.ForegroundColor = ConsoleColor.Yellow;
//                Console.WriteLine("===============================================================================================================================================================================");
//                Console.WriteLine($" TABELA DODATKOWA: Używane, Obce waluty lub Gorsze oferty (Liczba: {debugAdditionalList.Count})");
//                Console.WriteLine("===============================================================================================================================================================================");
//                Console.ResetColor();
//                Console.WriteLine($"{"---",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//                Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//                foreach (var item in debugAdditionalList) PrintDebugRow(item, "-");
//                Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//            }
//            else
//            {
//                Console.WriteLine("\n[INFO] Brak dodatkowych ofert.");
//            }

//            return finalPriceHistory;
//        }

//        #region Helper Methods (Logowanie i Parsowanie)

//        private void PrintDebugRow(dynamic item, string posLabel)
//        {
//            string infoCode = item.Info;
//            if (infoCode.Length > 15) infoCode = infoCode.Substring(0, 12) + "...";
//            string seller = item.Seller;
//            if (seller.Length > 20) seller = seller.Substring(0, 17) + "...";

//            Console.Write($"{posLabel,-4} | {item.GPos,-5} | {item.Stock,-4} | ");

//            string cond = item.Cond;
//            if (cond.Contains("UŻYW") || cond.Contains("OUTLET")) Console.ForegroundColor = ConsoleColor.Red;
//            else Console.ForegroundColor = ConsoleColor.Green;
//            Console.Write($"{cond,-6}");
//            Console.ResetColor();

//            Console.Write(" | ");

//            string curr = item.Curr;
//            if (curr != "PLN") Console.ForegroundColor = ConsoleColor.Magenta;
//            else Console.ForegroundColor = ConsoleColor.Gray;
//            Console.Write($"{curr,-6}");
//            Console.ResetColor();

//            Console.WriteLine($" | {infoCode,-15} | {item.Method,-6} | {seller,-20} | {item.Price,-10} | {item.Del,-9} | {item.Total,-10} | {item.Url}");
//        }

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
//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result)) return result;
//            return 0;
//        }

//        private decimal ParseDeliveryPrice(string? deliveryText)
//        {
//            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa") || deliveryText.ToLower().Contains("bezpłatnie")) return 0;
//            return ParsePrice(deliveryText);
//        }
//        #endregion
//    }

//    public static class GoogleShoppingApiParser
//    {
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

//                    string condition = "NOWY";
//                    string blockLower = block.ToLower();
//                    var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                    if (usedKeywords.Any(k => blockLower.Contains(k))) condition = "UŻYWANY";

//                    string currency = "PLN";
//                    if ((blockLower.Contains("eur") || blockLower.Contains("€")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "EUR";
//                    else if ((blockLower.Contains("usd") || blockLower.Contains("$")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "USD";

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
//                        offers.Add(new TempOffer(
//                            seller,
//                            priceVal.ToString("F2"),
//                            url,
//                            deliveryVal > 0 ? deliveryVal.ToString("F2") : "0",
//                            true,
//                            badge,
//                            offerIndex,
//                            "WRGA",
//                            null, null, condition, currency
//                        ));
//                    }
//                }
//            }
//            catch { }
//            return offers;
//        }

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
//            catch (JsonException) { return new List<TempOffer>(); }
//        }

//        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//        {
//            if (node.ValueKind == JsonValueKind.Array)
//            {

//                if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//                {
//                    foreach (JsonElement potentialOffer in node.EnumerateArray())
//                    {
//                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                            allOffers.Add(offer);
//                    }
//                }
//            }

//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in node.EnumerateArray())
//                    FindAndParseAllOffers(root, element, allOffers);
//            }
//            else if (node.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in node.EnumerateObject())
//                    FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//        private static bool IsPotentialSingleOffer(JsonElement node)
//        {

//            if (node.ValueKind != JsonValueKind.Array) return false;

//            int arrayChildren = 0;
//            int primitiveChildren = 0;

//            foreach (var child in node.EnumerateArray())
//            {
//                if (child.ValueKind == JsonValueKind.Array) arrayChildren++;
//                else if (child.ValueKind == JsonValueKind.String || child.ValueKind == JsonValueKind.Number || child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False) primitiveChildren++;
//            }

//            if (arrayChildren > 1 && primitiveChildren == 0) return false;

//            JsonElement offerData = node;
//            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//                offerData = node[0];

//            if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//            if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google") && !s.Contains("gstatic"))) return true;

//            return false;
//        }
//        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//        {
//            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
//            if (offerData.ValueKind != JsonValueKind.Array) return null;

//            try
//            {
//                var allNodes = Flatten(offerData).ToList();
//                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//                string? seller = null;
//                if (!string.IsNullOrEmpty(url)) seller = GetDomainName(url);

//                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
//                {
//                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };
//                    foreach (var s in flatStrings)
//                    {
//                        if (!s.StartsWith("http") && s.Length > 2 && !blacklist.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) && !Regex.IsMatch(s, @"\d"))
//                        {
//                            seller = s; break;
//                        }
//                    }
//                }
//                if (seller == null && url != null) seller = GetDomainName(url);

//                string condition = "NOWY";
//                var usedKeywords = new[] { "pre-owned", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };

//                foreach (var text in flatStrings)
//                {
//                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3) continue;
//                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

//                    string lowerText = text.ToLower();

//                    if (lowerText.Contains("nie używany") || lowerText.Contains("nieużywany")) continue;
//                    if (lowerText.Contains("nowy") && !lowerText.Contains("jak nowy")) continue;

//                    foreach (var keyword in usedKeywords)
//                    {
//                        if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
//                        {
//                            if (keyword == "używany" && (lowerText.Contains("fabrycznie nowy") || lowerText.Contains("produkt nowy")))
//                            {
//                                continue;
//                            }

//                            condition = "UŻYWANY";

//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[DEBUG DETECTOR] Sklep: {seller} -> ZNALEZIONO: '{keyword}'");
//                            Console.WriteLine($"                KONTEKST: \"{text}\"");
//                            Console.ResetColor();

//                            goto ConditionFound;
//                        }
//                    }
//                }
//            ConditionFound:;

//                bool isInStock = true;

//                bool hasPositiveStockText = flatStrings.Any(s =>
//                    s.Contains("W magazynie", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Dostępny", StringComparison.OrdinalIgnoreCase) ||
//                    s.Equals("In stock", StringComparison.OrdinalIgnoreCase));

//                if (hasPositiveStockText)
//                {
//                    isInStock = true;
//                }
//                else
//                {

//                    var outOfStockKeywords = new[] {
//                        "out of stock",
//                        "niedostępny",
//                        "brak w magazynie",
//                        "asortyment niedostępny",
//                        "wyprzedany",
//                        "chwilowy brak"
//                    };

//                    if (flatStrings.Any(text =>
//                        text.Length < 50 &&
//                        outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//                    {
//                        isInStock = false;
//                    }
//                }
//                List<(decimal Amount, string Currency)> structuralPrices = new();

//                for (int i = 1; i < allNodes.Count; i++)
//                {
//                    var current = allNodes[i];

//                    if (current.ValueKind == JsonValueKind.String)
//                    {
//                        string currCode = current.GetString()?.ToUpper() ?? "";
//                        if (currCode == "PLN" || currCode == "EUR" || currCode == "USD" || currCode == "GBP")
//                        {
//                            long micros = 0;
//                            bool foundPrice = false;

//                            var prev = allNodes[i - 1];
//                            if (prev.ValueKind == JsonValueKind.Number)
//                            {
//                                micros = prev.GetInt64();
//                                foundPrice = true;
//                            }

//                            else if (i >= 2)
//                            {
//                                var prevPrev = allNodes[i - 2];
//                                if (prevPrev.ValueKind == JsonValueKind.Number)
//                                {
//                                    micros = prevPrev.GetInt64();
//                                    foundPrice = true;
//                                }
//                            }

//                            if (foundPrice && micros >= 1000000)
//                            {
//                                structuralPrices.Add((micros / 1000000m, currCode));

//                                if (currCode == "PLN") break;
//                            }
//                        }
//                    }
//                }

//                bool hasTextualForeignEvidence = false;
//                string foreignTextCurrency = "";
//                var foreignRegex = new Regex(@"[\(\s](€|EUR|\$|USD)\s*(\d+[.,]?\d*)", RegexOptions.IgnoreCase);
//                foreach (var s in flatStrings)
//                {
//                    Match m = foreignRegex.Match(s);
//                    if (m.Success)
//                    {
//                        hasTextualForeignEvidence = true;
//                        string symbol = m.Groups[1].Value.ToUpper();
//                        foreignTextCurrency = (symbol.Contains("EUR") || symbol.Contains("€")) ? "EUR" : "USD";
//                        break;
//                    }
//                }

//                string? finalPrice = null;
//                string finalCurrency = "PLN";

//                var plnNode = structuralPrices.FirstOrDefault(x => x.Currency == "PLN");

//                if (plnNode != default)
//                {
//                    finalPrice = plnNode.Amount.ToString("F2", CultureInfo.InvariantCulture);
//                    if (hasTextualForeignEvidence) finalCurrency = foreignTextCurrency;
//                    else finalCurrency = "PLN";
//                }
//                else if (structuralPrices.Any(x => x.Currency != "PLN"))
//                {
//                    var foreign = structuralPrices.First(x => x.Currency != "PLN");
//                    finalPrice = foreign.Amount.ToString("F2", CultureInfo.InvariantCulture);
//                    finalCurrency = foreign.Currency;
//                }
//                else
//                {

//                    return null;
//                }

//                if (seller != null && (seller.Contains("Allegro") || seller.Contains("Ebay") || seller.Contains("eBay")))
//                {
//                    Console.ForegroundColor = ConsoleColor.DarkYellow;
//                    Console.Write($"[DEBUG {seller}] Struktura: ");
//                    foreach (var p in structuralPrices) Console.Write($"{p.Amount} {p.Currency} | ");
//                    Console.Write($"TekstForeign: {hasTextualForeignEvidence} ");
//                    Console.WriteLine($"-> DECYZJA: {finalCurrency}");
//                    Console.ResetColor();
//                }

//                string? delivery = null;

//                if (flatStrings.Any(s =>
//                    s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Bezpłatnie", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Free delivery", StringComparison.OrdinalIgnoreCase)))
//                {
//                    delivery = "Bezpłatna";
//                }
//                else
//                {

//                    var plusRegex = new Regex(@"^\+\s*(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                    foreach (var s in flatStrings)
//                    {
//                        var match = plusRegex.Match(s.Trim());
//                        if (match.Success)
//                        {
//                            delivery = ParsePriceDecimal(match.Groups[1].Value).ToString("F2");
//                            break;
//                        }
//                    }

//                    if (delivery == null)
//                    {
//                        var deliveryTextRegex = new Regex(
//                            @"(?:dostawa|wysyłka|delivery|shipping)(?:[^0-9]{0,30})(\d+[.,]\d{2})\s*(?:PLN|zł)|" +
//                            @"za\s+(\d+[.,]\d{2})\s*(?:PLN|zł)",
//                            RegexOptions.IgnoreCase);

//                        foreach (var s in flatStrings)
//                        {
//                            if (!s.ToLower().Contains("dostaw") &&
//                                !s.ToLower().Contains("wysyłk") &&
//                                !s.ToLower().Contains("delivery") &&
//                                !s.ToLower().Contains(" za "))
//                                continue;

//                            var match = deliveryTextRegex.Match(s);
//                            if (match.Success)
//                            {
//                                string priceStr = !string.IsNullOrEmpty(match.Groups[1].Value)
//                                    ? match.Groups[1].Value
//                                    : match.Groups[2].Value;

//                                decimal delPrice = ParsePriceDecimal(priceStr);

//                                if (delPrice > 0 && delPrice < 500)
//                                {
//                                    delivery = delPrice.ToString("F2");
//                                    break;
//                                }
//                            }
//                        }
//                    }

//                    if (delivery == null)
//                    {
//                        for (int i = 0; i < allNodes.Count - 1; i++)
//                        {
//                            var node = allNodes[i];
//                            if (node.ValueKind == JsonValueKind.Number)
//                            {
//                                try
//                                {
//                                    long val = node.GetInt64();
//                                    if (val == 110720 && i > 0)
//                                    {
//                                        var prevNode = allNodes[i - 1];
//                                        if (prevNode.ValueKind == JsonValueKind.String)
//                                        {
//                                            string delText = prevNode.GetString()!;
//                                            var priceMatch = Regex.Match(delText, @"(\d+[.,]\d{2})");
//                                            if (priceMatch.Success)
//                                            {
//                                                decimal delPrice = ParsePriceDecimal(priceMatch.Groups[1].Value);
//                                                if (delPrice > 0 && delPrice < 500)
//                                                {
//                                                    delivery = delPrice.ToString("F2");
//                                                    break;
//                                                }
//                                            }
//                                        }
//                                    }
//                                }
//                                catch { }
//                            }
//                        }
//                    }
//                }

//                string? badge = ExtractBadgeStrict(offerData);
//                if (string.IsNullOrEmpty(badge))
//                {
//                    string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" };
//                    badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase)));
//                }

//                if (!string.IsNullOrWhiteSpace(seller) && finalPrice != null && url != null)
//                {

//                    return new TempOffer(seller, finalPrice, url, delivery, isInStock, badge, 0, "OAPV", null, null, condition, finalCurrency);
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
//                   url.Contains("/search?") || url.Contains("youtube.") ||
//                   url.Contains("googleusercontent") || url.Contains("translate.google");
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

//        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0)
//        {
//            if (currentDepth > maxDepth) yield break;
//            yield return node;
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in node.EnumerateArray())
//                {
//                    foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child;
//                }
//            }
//            else if (node.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in node.EnumerateObject())
//                {
//                    foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child;
//                }
//            }
//        }

//        private static decimal ExtractRichSnippetPrice(string htmlBlock)
//        {
//            var richPriceRegex = new Regex(@"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>", RegexOptions.IgnoreCase);
//            var match = richPriceRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock)
//        {
//            var deliveryRegex = new Regex(@"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)", RegexOptions.IgnoreCase);
//            var match = deliveryRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text)
//        {
//            var priceRegex = new Regex(@"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?", RegexOptions.IgnoreCase);
//            var matches = priceRegex.Matches(text);
//            decimal bestPrice = 0;
//            decimal bestDelivery = 0;

//            foreach (Match m in matches)
//            {
//                if (!decimal.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue;
//                if (val < 1.0m) continue;

//                int contextStart = Math.Max(0, m.Index - 30);
//                int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20);
//                string snippetForLog = text.Substring(contextStart, contextLen).ToLower();

//                if (Regex.IsMatch(snippetForLog, @"(dostawa|wysyłka|delivery|\+)"))
//                {
//                    if (bestDelivery == 0) bestDelivery = val;
//                }
//                else if (bestPrice == 0)
//                {
//                    bestPrice = val;
//                }
//            }
//            return (bestPrice, bestDelivery);
//        }

//        private static string StripHtml(string html)
//        {
//            if (string.IsNullOrEmpty(html)) return "";
//            string s = html.Replace("<br>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " ");
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
//                if (clean.LastIndexOf(',') > clean.LastIndexOf('.'))
//                    clean = clean.Replace(".", "").Replace(",", ".");
//                else
//                    clean = clean.Replace(",", "");
//            }
//            else if (clean.Contains(","))
//            {
//                clean = clean.Replace(",", ".");
//            }

//            else if (clean.Count(c => c == '.') > 1)
//            {
//                int lastDot = clean.LastIndexOf('.');
//                clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot);
//            }

//            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res))
//                return res;
//            return 0;
//        }
//    }
//}




















// z rozgrzewka i kopia 

//using OpenQA.Selenium;
//using OpenQA.Selenium.Chrome;
//using OpenQA.Selenium.Support.UI;
//using PriceSafari.Models;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading;
//using System.Threading.Tasks;

//namespace PriceSafari.Services
//{
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
//        string? RatingCount = null,
//        string Condition = "NOWY",
//        string Currency = "PLN"
//    );

//    public class GoogleMainPriceScraper
//    {
//        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;

//        // ZMIANA: Usunięto 'static'. Teraz każdy obiekt Scrapera ma WŁASNEGO klienta, flagi i blokadę.
//        private HttpClient _httpClient;
//        private bool _areCookiesInitialized = false;
//        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

//        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

//        // ZMIANA: Konstruktor publiczny (nie statyczny), uruchamiany przy każdym 'new GoogleMainPriceScraper()'
//        public GoogleMainPriceScraper()
//        {
//            var handler = new HttpClientHandler
//            {
//                UseCookies = true,
//                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
//            };
//            _httpClient = new HttpClient(handler);
//            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
//            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//        }



//        //private void InitBrowserAndCookies()
//        //{
//        //    Console.ForegroundColor = ConsoleColor.Yellow;
//        //    Console.WriteLine($"\n[INIT] Bot {this.GetHashCode()}: Uruchamiam proces 'Cookie Warming' (Selenium)...");
//        //    Console.ResetColor();

//        //    var options = new ChromeOptions();
//        //    options.AddArgument("--disable-blink-features=AutomationControlled");
//        //    options.AddArgument("--start-maximized");
//        //    options.AddArgument($"user-agent={UserAgent}");
//        //    options.AddArgument("--log-level=3");


//        private void InitBrowserAndCookies()
//        {
//            Console.ForegroundColor = ConsoleColor.Yellow;
//            Console.WriteLine($"\n[INIT] Bot {this.GetHashCode()}: Uruchamiam proces 'Cookie Warming' (Selenium HEADLESS)...");
//            Console.ResetColor();

//            var options = new ChromeOptions();
//            // --- KONFIGURACJA HEADLESS ---
//            options.AddArgument("--headless=new"); // Nowy, lepszy tryb headless (mniej wykrywalny)
//            options.AddArgument("--window-size=1920,1080"); // WAŻNE: Wymuszamy dużą rozdzielczość, żeby elementy były widoczne
//                                                            // -----------------------------
//            options.AddArgument("--disable-blink-features=AutomationControlled");
//            options.AddArgument($"user-agent={UserAgent}");
//            options.AddArgument("--log-level=3");

//            // --- LISTA LOSOWYCH FRAZ DO ROZGRZEWANIA ---
//            var searchQueries = new[]
//            {
//                "iphone 15 pro", "samsung galaxy s24", "laptop dell", "karta graficzna rtx 4060", // Elektronika
//                "buty nike air max", "adidas ultraboost", "kurtka the north face", "plecak vans",       // Moda
//                "ekspres do kawy delonghi", "odkurzacz dyson v15", "robot sprzątający roborock",        // Dom
//                "klocki lego star wars", "konsola ps5 slim", "pad xbox series x", "nintendo switch",    // Rozrywka
//                "wiertarka wkrętarka makita", "zestaw kluczy yato", "kosiarka spalinowa",               // Narzędzia
//                "rower górski kross", "namiot 4 osobowy", "buty trekkingowe salomon"                    // Sport
//            };

//            // Wybierz losową frazę i zamień spacje na plusy (format URL Google)
//            var randomQuery = searchQueries[new Random().Next(searchQueries.Length)].Replace(" ", "+");
//            // ---------------------------------------------

//            try
//            {
//                using (var driver = new ChromeDriver(options))
//                {
//                    // Używamy wylosowanej frazy w URL
//                    string targetUrl = $"https://www.google.com/search?q={randomQuery}&tbm=shop";
//                    driver.Navigate().GoToUrl(targetUrl);

//                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

//                    // 1. Zgody RODO (SZYBKO)
//                    try
//                    {
//                        Console.WriteLine($"[AUTO Bot-{this.GetHashCode()}] Szukam przycisku zgody...");
//                        var consentButton = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'QS5gu') and (contains(., 'Zaakceptuj') or contains(., 'Odrzuć'))]")));
//                        Thread.Sleep(new Random().Next(200, 500)); // Skrócone opóźnienie
//                        consentButton.Click();
//                        Console.WriteLine($"[AUTO Bot-{this.GetHashCode()}] Kliknięto zgodę RODO.");
//                    }
//                    catch
//                    {
//                        Console.WriteLine($"[INFO Bot-{this.GetHashCode()}] Brak popupu RODO lub już zaakceptowano.");
//                    }

//                    Thread.Sleep(500); // Skrócone opóźnienie

//                    // 2. Kliknięcie w produkt (SZYBKO)
//                    try
//                    {
//                        Console.WriteLine($"[AUTO Bot-{this.GetHashCode()}] Szukam produktu ({randomQuery.Replace("+", " ")})...");
//                        var productCard = wait.Until(d => d.FindElement(By.CssSelector("div.njFjte[role='button']")));

//                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", productCard);
//                        Thread.Sleep(500); // Skrócone opóźnienie

//                        productCard.Click();
//                        Console.WriteLine($"[AUTO Bot-{this.GetHashCode()}] Kliknięto w produkt.");

//                        Thread.Sleep(2500); // Czas na zebranie ciastek (skrócony z 4000)
//                    }
//                    catch (Exception)
//                    {
//                        Console.WriteLine($"[WARNING Bot-{this.GetHashCode()}] Problem z kliknięciem w produkt (może być pomijalny).");
//                    }

//                    // 3. Pobranie ciastek
//                    Console.WriteLine($"[INFO Bot-{this.GetHashCode()}] Pobieram nowe ciasteczka...");
//                    var cookies = driver.Manage().Cookies.AllCookies;
//                    var cookieContainer = new CookieContainer();
//                    foreach (var c in cookies)
//                    {
//                        try { cookieContainer.Add(new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain)); } catch { }
//                    }

//                    var handler = new HttpClientHandler
//                    {
//                        CookieContainer = cookieContainer,
//                        UseCookies = true,
//                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
//                    };

//                    _httpClient = new HttpClient(handler);
//                    _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
//                    _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
//                    _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//                    _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not A(Brand\";v=\"99\", \"Google Chrome\";v=\"121\", \"Chromium\";v=\"121\"");
//                    _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
//                    _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");

//                    Console.WriteLine($"[SUKCES Bot-{this.GetHashCode()}] Sesja odświeżona na frazie: '{randomQuery}'.");
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"[SELENIUM CRITICAL Bot-{this.GetHashCode()}] {ex.Message}");
//            }
//        }

//        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//        {
//            // --- PIERWSZE URUCHOMIENIE DLA TEJ INSTANCJI ---
//            if (!_areCookiesInitialized)
//            {
//                await _initLock.WaitAsync();
//                try
//                {
//                    if (!_areCookiesInitialized)
//                    {
//                        InitBrowserAndCookies();
//                        _areCookiesInitialized = true;
//                    }
//                }
//                finally { _initLock.Release(); }
//            }
//            // ---------------------------------------------

//            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
//            var allDebugOffers = new List<TempOffer>();

//            Console.WriteLine($"\n[INFO] Start scrapowania dla ID: {coOfr.Id}...");

//            string urlTemplate;

//            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
//            {
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }
//            else
//            {
//                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);

//                if (string.IsNullOrEmpty(catalogId))
//                {
//                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
//                    return finalPriceHistory;
//                }

//                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//                {
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                }
//                else
//                {
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                }
//            }

//            var allFoundOffers = new List<TempOffer>();
//            string? firstPageRawResponse = null;

//            int startIndex = 0;
//            const int pageSize = 10;
//            int lastFetchCount;
//            const int maxRetries = 3;

//            do
//            {
//                string currentUrl = string.Format(urlTemplate, startIndex);
//                List<TempOffer> newOffers = new List<TempOffer>();
//                string rawResponse = "";

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {
//                    try
//                    {
//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] Wysłanie żądania OAPV (Start: {startIndex}):");
//                        Console.WriteLine($"   URL: {currentUrl}");
//                        Console.ResetColor();

//                        rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                        Console.ForegroundColor = ConsoleColor.DarkGray;
//                        int previewLength = Math.Min(500, rawResponse.Length);
//                        Console.WriteLine($"   [RESPONSE] Długość: {rawResponse.Length} znaków.");
//                        Console.WriteLine($"   [PREVIEW]: {rawResponse.Substring(0, previewLength)}...");
//                        Console.ResetColor();

//                        // --- WYKRYWANIE BLOKADY I REGENERATE COOKIES (Per Instancja) ---
//                        if (rawResponse.Length < 100 && rawResponse.Contains("ProductDetailsResult\":[]"))
//                        {
//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[BLOKADA] Wykryto pustą odpowiedź dla bota {this.GetHashCode()} (Próba {attempt}/{maxRetries}).");
//                            Console.WriteLine("[AUTO-FIX] Uruchamiam procedurę odświeżania ciasteczek dla tego bota...");
//                            Console.ResetColor();

//                            await _initLock.WaitAsync();
//                            try
//                            {
//                                InitBrowserAndCookies();
//                            }
//                            finally
//                            {
//                                _initLock.Release();
//                            }

//                            Console.WriteLine("[INFO] Ponawiam zapytanie z nowymi ciastkami...");
//                            continue;
//                        }
//                        // -------------------------------------------------------------

//                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);

//                        if (newOffers.Count > 0) break;

//                        if (attempt < maxRetries)
//                        {
//                            Console.WriteLine("   [RETRY] Ponawiam próbę...");
//                            await Task.Delay(2000);
//                        }
//                    }
//                    catch (HttpRequestException ex)
//                    {
//                        Console.WriteLine($"   [ERROR] Błąd HTTP: {ex.Message}");
//                        if (attempt == maxRetries) Console.WriteLine($"[BŁĄD KRYTYCZNY] {ex.Message}");
//                        else await Task.Delay(4000);
//                    }
//                }

//                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse))
//                    firstPageRawResponse = rawResponse;

//                lastFetchCount = newOffers.Count;
//                Console.WriteLine($"   [INFO] Znaleziono ofert na stronie: {lastFetchCount}");

//                foreach (var offer in newOffers)
//                {
//                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
//                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url))
//                    {
//                        allFoundOffers.Add(offerWithIndex);
//                    }
//                }

//                startIndex += pageSize;
//                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(100, 200));

//            } while (lastFetchCount == pageSize);

//            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse))
//            {
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers
//                        .Where(o => o.Currency == "PLN")
//                        .Select(o => ParsePrice(o.Price))
//                        .Where(p => p > 0)
//                        .ToList();

//                    if (prices.Any())
//                    {
//                        var sortedPrices = prices.OrderBy(p => p).ToList();
//                        baselinePrice = sortedPrices[sortedPrices.Count / 2];
//                        Console.WriteLine($"\n[INFO] Mediana cen z OAPV (Baseline): {baselinePrice:F2} zł");
//                    }
//                }

//                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);

//                if (!string.IsNullOrEmpty(productTitle))
//                {
//                    string encodedQ = Uri.EscapeDataString(productTitle);
//                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

//                    try
//                    {
//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] Wysłanie żądania WRGA (Smart Q):");
//                        Console.WriteLine($"   URL: {wrgaUrl}");
//                        Console.ResetColor();

//                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);

//                        if (wrgaResponse.Length < 100)
//                        {
//                            Console.WriteLine("[WARNING] WRGA zwróciło pustą odpowiedź. Pomijam.");
//                        }
//                        else
//                        {
//                            Console.ForegroundColor = ConsoleColor.DarkGray;
//                            int previewLength = Math.Min(500, wrgaResponse.Length);
//                            Console.WriteLine($"   [RESPONSE] Długość: {wrgaResponse.Length} znaków.");
//                            Console.WriteLine($"   [PREVIEW]: {wrgaResponse.Substring(0, previewLength)}...");
//                            Console.ResetColor();

//                            var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);
//                            Console.WriteLine($"   [INFO] Znaleziono ofert WRGA: {wrgaOffers.Count}");

//                            foreach (var off in wrgaOffers)
//                            {
//                                if (baselinePrice > 0 && off.Currency == "PLN")
//                                {
//                                    decimal wrgaPrice = ParsePrice(off.Price);
//                                    decimal diff = wrgaPrice - baselinePrice;
//                                    decimal percentageDiff = diff / baselinePrice;

//                                    if (percentageDiff < -0.8m || percentageDiff > 2.0m)
//                                    {
//                                        Console.ForegroundColor = ConsoleColor.Red;
//                                        Console.WriteLine($"\n[LIMITER - ODZRUT PROD] Odrzucono ofertę WRGA: {off.Seller}");
//                                        Console.WriteLine($"   Powód: Cena {wrgaPrice:F2} odbiega o {percentageDiff:P0} od średniej {baselinePrice:F2}");
//                                        Console.ResetColor();
//                                        continue;
//                                    }
//                                }

//                                if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
//                                {
//                                    var organicOffer = off with { OriginalIndex = allFoundOffers.Count + 1 };
//                                    allFoundOffers.Add(organicOffer);
//                                }
//                            }
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"   [ERROR] Błąd WRGA: {ex.Message}");
//                    }
//                }
//            }

//            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);

//            var debugMainList = new List<dynamic>();
//            var debugAdditionalList = new List<dynamic>();

//            foreach (var group in groupedBySeller)
//            {
//                var bestValidOffer = group
//                    .Where(o => o.Condition == "NOWY" && o.Currency == "PLN")
//                    .OrderBy(o => ParsePrice(o.Price))
//                    .FirstOrDefault();

//                var sortedStoreOffers = group.OrderBy(o => ParsePrice(o.Price)).ToList();

//                foreach (var offer in sortedStoreOffers)
//                {
//                    bool isBest = (bestValidOffer != null && offer == bestValidOffer);

//                    if (isBest)
//                    {
//                        string? isBiddingValue = null;
//                        if (!string.IsNullOrEmpty(offer.Badge))
//                        {
//                            string badgeLower = offer.Badge.ToLower();
//                            if (badgeLower.Contains("cena")) isBiddingValue = "bpg";
//                            else if (badgeLower.Contains("popularn") || badgeLower.Contains("wybór")) isBiddingValue = "hpg";
//                        }

//                        finalPriceHistory.Add(new CoOfrPriceHistoryClass
//                        {
//                            CoOfrClassId = coOfr.Id,
//                            GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl),
//                            GoogleStoreName = offer.Seller,
//                            GooglePrice = ParsePrice(offer.Price),
//                            GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                            GooglePosition = offer.OriginalIndex.ToString(),
//                            IsBidding = isBiddingValue,
//                            GoogleInStock = offer.IsInStock,
//                            GoogleOfferPerStoreCount = group.Count()
//                        });
//                    }

//                    var debugItem = new
//                    {
//                        Pos = isBest ? offer.OriginalIndex.ToString() : "-",
//                        GPos = offer.OriginalIndex,
//                        Stock = offer.IsInStock ? "OK" : "BRAK",
//                        Cond = offer.Condition,
//                        Curr = offer.Currency,
//                        Info = offer.Badge ?? "-",
//                        Method = offer.Method,
//                        Seller = offer.Seller,
//                        Price = ParsePrice(offer.Price),
//                        Del = ParseDeliveryPrice(offer.Delivery),
//                        Total = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                        Url = offer.Url,
//                        IsMain = isBest
//                    };

//                    if (isBest) debugMainList.Add(debugItem);
//                    else debugAdditionalList.Add(debugItem);
//                }
//            }

//            finalPriceHistory = finalPriceHistory.OrderBy(x => x.GooglePrice).ToList();

//            debugMainList = debugMainList.OrderBy(x => x.Price).ToList();

//            for (int i = 0; i < debugMainList.Count; i++)
//            {
//                var old = debugMainList[i];
//                debugMainList[i] = new { old.Pos, old.GPos, old.Stock, old.Cond, old.Curr, old.Info, old.Method, old.Seller, old.Price, old.Del, old.Total, old.Url, old.IsMain, ListPos = (i + 1).ToString() };
//            }

//            debugAdditionalList = debugAdditionalList.OrderBy(x => x.Seller).ThenBy(x => x.Price).ToList();

//            Console.WriteLine("\n===============================================================================================================================================================================");
//            Console.WriteLine($" TABELA GŁÓWNA: Najlepsze oferty (NOWE, PLN) (Liczba: {debugMainList.Count})");
//            Console.WriteLine("===============================================================================================================================================================================");
//            Console.WriteLine($"{"Poz.",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");

//            foreach (var item in debugMainList) PrintDebugRow(item, item.ListPos);

//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");

//            if (debugAdditionalList.Any())
//            {
//                Console.WriteLine("\n");
//                Console.ForegroundColor = ConsoleColor.Yellow;
//                Console.WriteLine("===============================================================================================================================================================================");
//                Console.WriteLine($" TABELA DODATKOWA: Używane, Obce waluty lub Gorsze oferty (Liczba: {debugAdditionalList.Count})");
//                Console.WriteLine("===============================================================================================================================================================================");
//                Console.ResetColor();
//                Console.WriteLine($"{"---",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//                Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//                foreach (var item in debugAdditionalList) PrintDebugRow(item, "-");
//                Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//            }
//            else
//            {
//                Console.WriteLine("\n[INFO] Brak dodatkowych ofert.");
//            }

//            return finalPriceHistory;
//        }

//        #region Helper Methods (Logowanie i Parsowanie)

//        private void PrintDebugRow(dynamic item, string posLabel)
//        {
//            string infoCode = item.Info;
//            if (infoCode.Length > 15) infoCode = infoCode.Substring(0, 12) + "...";
//            string seller = item.Seller;
//            if (seller.Length > 20) seller = seller.Substring(0, 17) + "...";

//            Console.Write($"{posLabel,-4} | {item.GPos,-5} | {item.Stock,-4} | ");

//            string cond = item.Cond;
//            if (cond.Contains("UŻYW") || cond.Contains("OUTLET")) Console.ForegroundColor = ConsoleColor.Red;
//            else Console.ForegroundColor = ConsoleColor.Green;
//            Console.Write($"{cond,-6}");
//            Console.ResetColor();

//            Console.Write(" | ");

//            string curr = item.Curr;
//            if (curr != "PLN") Console.ForegroundColor = ConsoleColor.Magenta;
//            else Console.ForegroundColor = ConsoleColor.Gray;
//            Console.Write($"{curr,-6}");
//            Console.ResetColor();

//            Console.WriteLine($" | {infoCode,-15} | {item.Method,-6} | {seller,-20} | {item.Price,-10} | {item.Del,-9} | {item.Total,-10} | {item.Url}");
//        }

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
//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result)) return result;
//            return 0;
//        }

//        private decimal ParseDeliveryPrice(string? deliveryText)
//        {
//            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa") || deliveryText.ToLower().Contains("bezpłatnie")) return 0;
//            return ParsePrice(deliveryText);
//        }
//        #endregion
//    }

//    public static class GoogleShoppingApiParser
//    {
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

//                    string condition = "NOWY";
//                    string blockLower = block.ToLower();
//                    var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                    if (usedKeywords.Any(k => blockLower.Contains(k))) condition = "UŻYWANY";

//                    string currency = "PLN";
//                    if ((blockLower.Contains("eur") || blockLower.Contains("€")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "EUR";
//                    else if ((blockLower.Contains("usd") || blockLower.Contains("$")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "USD";

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
//                        offers.Add(new TempOffer(
//                            seller,
//                            priceVal.ToString("F2"),
//                            url,
//                            deliveryVal > 0 ? deliveryVal.ToString("F2") : "0",
//                            true,
//                            badge,
//                            offerIndex,
//                            "WRGA",
//                            null, null, condition, currency
//                        ));
//                    }
//                }
//            }
//            catch { }
//            return offers;
//        }

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
//            catch (JsonException) { return new List<TempOffer>(); }
//        }

//        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//        {
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//                {
//                    foreach (JsonElement potentialOffer in node.EnumerateArray())
//                    {
//                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                            allOffers.Add(offer);
//                    }
//                }
//            }

//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in node.EnumerateArray())
//                    FindAndParseAllOffers(root, element, allOffers);
//            }
//            else if (node.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in node.EnumerateObject())
//                    FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//        private static bool IsPotentialSingleOffer(JsonElement node)
//        {
//            if (node.ValueKind != JsonValueKind.Array) return false;

//            int arrayChildren = 0;
//            int primitiveChildren = 0;

//            foreach (var child in node.EnumerateArray())
//            {
//                if (child.ValueKind == JsonValueKind.Array) arrayChildren++;
//                else if (child.ValueKind == JsonValueKind.String || child.ValueKind == JsonValueKind.Number || child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False) primitiveChildren++;
//            }

//            if (arrayChildren > 1 && primitiveChildren == 0) return false;

//            JsonElement offerData = node;
//            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//                offerData = node[0];

//            if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//            if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google") && !s.Contains("gstatic"))) return true;

//            return false;
//        }
//        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//        {
//            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
//            if (offerData.ValueKind != JsonValueKind.Array) return null;

//            try
//            {
//                var allNodes = Flatten(offerData).ToList();
//                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();

//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//                string? seller = null;
//                if (!string.IsNullOrEmpty(url)) seller = GetDomainName(url);

//                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
//                {
//                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };
//                    foreach (var s in flatStrings)
//                    {
//                        if (!s.StartsWith("http") && s.Length > 2 && !blacklist.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) && !Regex.IsMatch(s, @"\d"))
//                        {
//                            seller = s; break;
//                        }
//                    }
//                }
//                if (seller == null && url != null) seller = GetDomainName(url);

//                string condition = "NOWY";
//                var usedKeywords = new[] { "pre-owned", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };

//                foreach (var text in flatStrings)
//                {
//                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3) continue;
//                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

//                    string lowerText = text.ToLower();

//                    if (lowerText.Contains("nie używany") || lowerText.Contains("nieużywany")) continue;
//                    if (lowerText.Contains("nowy") && !lowerText.Contains("jak nowy")) continue;

//                    foreach (var keyword in usedKeywords)
//                    {
//                        if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
//                        {
//                            if (keyword == "używany" && (lowerText.Contains("fabrycznie nowy") || lowerText.Contains("produkt nowy")))
//                            {
//                                continue;
//                            }

//                            condition = "UŻYWANY";

//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[DEBUG DETECTOR] Sklep: {seller} -> ZNALEZIONO: '{keyword}'");
//                            Console.WriteLine($"                KONTEKST: \"{text}\"");
//                            Console.ResetColor();

//                            goto ConditionFound;
//                        }
//                    }
//                }
//            ConditionFound:;

//                bool isInStock = true;

//                bool hasPositiveStockText = flatStrings.Any(s =>
//                    s.Contains("W magazynie", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Dostępny", StringComparison.OrdinalIgnoreCase) ||
//                    s.Equals("In stock", StringComparison.OrdinalIgnoreCase));

//                if (hasPositiveStockText)
//                {
//                    isInStock = true;
//                }
//                else
//                {
//                    var outOfStockKeywords = new[] {
//                        "out of stock",
//                        "niedostępny",
//                        "brak w magazynie",
//                        "asortyment niedostępny",
//                        "wyprzedany",
//                        "chwilowy brak"
//                    };

//                    if (flatStrings.Any(text =>
//                        text.Length < 50 &&
//                        outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//                    {
//                        isInStock = false;
//                    }
//                }
//                List<(decimal Amount, string Currency)> structuralPrices = new();

//                for (int i = 1; i < allNodes.Count; i++)
//                {
//                    var current = allNodes[i];

//                    if (current.ValueKind == JsonValueKind.String)
//                    {
//                        string currCode = current.GetString()?.ToUpper() ?? "";
//                        if (currCode == "PLN" || currCode == "EUR" || currCode == "USD" || currCode == "GBP")
//                        {
//                            long micros = 0;
//                            bool foundPrice = false;

//                            var prev = allNodes[i - 1];
//                            if (prev.ValueKind == JsonValueKind.Number)
//                            {
//                                micros = prev.GetInt64();
//                                foundPrice = true;
//                            }

//                            else if (i >= 2)
//                            {
//                                var prevPrev = allNodes[i - 2];
//                                if (prevPrev.ValueKind == JsonValueKind.Number)
//                                {
//                                    micros = prevPrev.GetInt64();
//                                    foundPrice = true;
//                                }
//                            }

//                            if (foundPrice && micros >= 1000000)
//                            {
//                                structuralPrices.Add((micros / 1000000m, currCode));
//                                if (currCode == "PLN") break;
//                            }
//                        }
//                    }
//                }

//                bool hasTextualForeignEvidence = false;
//                string foreignTextCurrency = "";
//                var foreignRegex = new Regex(@"[\(\s](€|EUR|\$|USD)\s*(\d+[.,]?\d*)", RegexOptions.IgnoreCase);
//                foreach (var s in flatStrings)
//                {
//                    Match m = foreignRegex.Match(s);
//                    if (m.Success)
//                    {
//                        hasTextualForeignEvidence = true;
//                        string symbol = m.Groups[1].Value.ToUpper();
//                        foreignTextCurrency = (symbol.Contains("EUR") || symbol.Contains("€")) ? "EUR" : "USD";
//                        break;
//                    }
//                }

//                string? finalPrice = null;
//                string finalCurrency = "PLN";

//                var plnNode = structuralPrices.FirstOrDefault(x => x.Currency == "PLN");

//                if (plnNode != default)
//                {
//                    finalPrice = plnNode.Amount.ToString("F2", CultureInfo.InvariantCulture);
//                    if (hasTextualForeignEvidence) finalCurrency = foreignTextCurrency;
//                    else finalCurrency = "PLN";
//                }
//                else if (structuralPrices.Any(x => x.Currency != "PLN"))
//                {
//                    var foreign = structuralPrices.First(x => x.Currency != "PLN");
//                    finalPrice = foreign.Amount.ToString("F2", CultureInfo.InvariantCulture);
//                    finalCurrency = foreign.Currency;
//                }
//                else
//                {
//                    return null;
//                }

//                if (seller != null && (seller.Contains("Allegro") || seller.Contains("Ebay") || seller.Contains("eBay")))
//                {
//                    Console.ForegroundColor = ConsoleColor.DarkYellow;
//                    Console.Write($"[DEBUG {seller}] Struktura: ");
//                    foreach (var p in structuralPrices) Console.Write($"{p.Amount} {p.Currency} | ");
//                    Console.Write($"TekstForeign: {hasTextualForeignEvidence} ");
//                    Console.WriteLine($"-> DECYZJA: {finalCurrency}");
//                    Console.ResetColor();
//                }

//                string? delivery = null;

//                if (flatStrings.Any(s =>
//                    s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Bezpłatnie", StringComparison.OrdinalIgnoreCase) ||
//                    s.Contains("Free delivery", StringComparison.OrdinalIgnoreCase)))
//                {
//                    delivery = "Bezpłatna";
//                }
//                else
//                {
//                    var plusRegex = new Regex(@"^\+\s*(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                    foreach (var s in flatStrings)
//                    {
//                        var match = plusRegex.Match(s.Trim());
//                        if (match.Success)
//                        {
//                            delivery = ParsePriceDecimal(match.Groups[1].Value).ToString("F2");
//                            break;
//                        }
//                    }

//                    if (delivery == null)
//                    {
//                        var deliveryTextRegex = new Regex(
//                            @"(?:dostawa|wysyłka|delivery|shipping)(?:[^0-9]{0,30})(\d+[.,]\d{2})\s*(?:PLN|zł)|" +
//                            @"za\s+(\d+[.,]\d{2})\s*(?:PLN|zł)",
//                            RegexOptions.IgnoreCase);

//                        foreach (var s in flatStrings)
//                        {
//                            if (!s.ToLower().Contains("dostaw") &&
//                                !s.ToLower().Contains("wysyłk") &&
//                                !s.ToLower().Contains("delivery") &&
//                                !s.ToLower().Contains(" za "))
//                                continue;

//                            var match = deliveryTextRegex.Match(s);
//                            if (match.Success)
//                            {
//                                string priceStr = !string.IsNullOrEmpty(match.Groups[1].Value)
//                                    ? match.Groups[1].Value
//                                    : match.Groups[2].Value;

//                                decimal delPrice = ParsePriceDecimal(priceStr);

//                                if (delPrice > 0 && delPrice < 500)
//                                {
//                                    delivery = delPrice.ToString("F2");
//                                    break;
//                                }
//                            }
//                        }
//                    }

//                    if (delivery == null)
//                    {
//                        for (int i = 0; i < allNodes.Count - 1; i++)
//                        {
//                            var node = allNodes[i];
//                            if (node.ValueKind == JsonValueKind.Number)
//                            {
//                                try
//                                {
//                                    long val = node.GetInt64();
//                                    if (val == 110720 && i > 0)
//                                    {
//                                        var prevNode = allNodes[i - 1];
//                                        if (prevNode.ValueKind == JsonValueKind.String)
//                                        {
//                                            string delText = prevNode.GetString()!;
//                                            var priceMatch = Regex.Match(delText, @"(\d+[.,]\d{2})");
//                                            if (priceMatch.Success)
//                                            {
//                                                decimal delPrice = ParsePriceDecimal(priceMatch.Groups[1].Value);
//                                                if (delPrice > 0 && delPrice < 500)
//                                                {
//                                                    delivery = delPrice.ToString("F2");
//                                                    break;
//                                                }
//                                            }
//                                        }
//                                    }
//                                }
//                                catch { }
//                            }
//                        }
//                    }
//                }

//                string? badge = ExtractBadgeStrict(offerData);
//                if (string.IsNullOrEmpty(badge))
//                {
//                    string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" };
//                    badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase)));
//                }

//                if (!string.IsNullOrWhiteSpace(seller) && finalPrice != null && url != null)
//                {
//                    return new TempOffer(seller, finalPrice, url, delivery, isInStock, badge, 0, "OAPV", null, null, condition, finalCurrency);
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
//                   url.Contains("/search?") || url.Contains("youtube.") ||
//                   url.Contains("googleusercontent") || url.Contains("translate.google");
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

//        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0)
//        {
//            if (currentDepth > maxDepth) yield break;
//            yield return node;
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in node.EnumerateArray())
//                {
//                    foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child;
//                }
//            }
//            else if (node.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in node.EnumerateObject())
//                {
//                    foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child;
//                }
//            }
//        }

//        private static decimal ExtractRichSnippetPrice(string htmlBlock)
//        {
//            var richPriceRegex = new Regex(@"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>", RegexOptions.IgnoreCase);
//            var match = richPriceRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock)
//        {
//            var deliveryRegex = new Regex(@"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)", RegexOptions.IgnoreCase);
//            var match = deliveryRegex.Match(htmlBlock);
//            if (match.Success) return ParsePriceDecimal(match.Groups[1].Value);
//            return 0;
//        }

//        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text)
//        {
//            var priceRegex = new Regex(@"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?", RegexOptions.IgnoreCase);
//            var matches = priceRegex.Matches(text);
//            decimal bestPrice = 0;
//            decimal bestDelivery = 0;

//            foreach (Match m in matches)
//            {
//                if (!decimal.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue;
//                if (val < 1.0m) continue;

//                int contextStart = Math.Max(0, m.Index - 30);
//                int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20);
//                string snippetForLog = text.Substring(contextStart, contextLen).ToLower();

//                if (Regex.IsMatch(snippetForLog, @"(dostawa|wysyłka|delivery|\+)"))
//                {
//                    if (bestDelivery == 0) bestDelivery = val;
//                }
//                else if (bestPrice == 0)
//                {
//                    bestPrice = val;
//                }
//            }
//            return (bestPrice, bestDelivery);
//        }

//        private static string StripHtml(string html)
//        {
//            if (string.IsNullOrEmpty(html)) return "";
//            string s = html.Replace("<br>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " ");
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
//                if (clean.LastIndexOf(',') > clean.LastIndexOf('.'))
//                    clean = clean.Replace(".", "").Replace(",", ".");
//                else
//                    clean = clean.Replace(",", "");
//            }
//            else if (clean.Contains(","))
//            {
//                clean = clean.Replace(",", ".");
//            }

//            else if (clean.Count(c => c == '.') > 1)
//            {
//                int lastDot = clean.LastIndexOf('.');
//                clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot);
//            }

//            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res))
//                return res;
//            return 0;
//        }
//    }
//}





























// logika z magazynem ciastek i generatorami






//using OpenQA.Selenium;
//using OpenQA.Selenium.Chrome;
//using OpenQA.Selenium.Support.UI;
//using PriceSafari.Models;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading;
//using System.Threading.Tasks;

//namespace PriceSafari.Services
//{
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
//        string? RatingCount = null,
//        string Condition = "NOWY",
//        string Currency = "PLN"
//    );

//    public class GoogleMainPriceScraper
//    {
//        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;
//        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

//        private static readonly BlockingCollection<CookieContainer> _cookieWarehouse = new BlockingCollection<CookieContainer>();

//        private static CancellationTokenSource _generatorCts = new CancellationTokenSource();
//        private static bool _generatorsStarted = false;
//        private static readonly object _lockObj = new object();

//        // <summary>

//        // Metoda uruchamiająca generatory. Wywołaj RAZ przed startem zadań scrapowania.

//        // </summary>

//        public static async Task InitializeGeneratorsAsync(int botCount, int headStartSeconds = 20)
//        {
//            lock (_lockObj)
//            {
//                if (_generatorsStarted) return;

//                if (_generatorCts.IsCancellationRequested)
//                {
//                    _generatorCts.Dispose();
//                    _generatorCts = new CancellationTokenSource();
//                }

//                _generatorsStarted = true;
//            }

//            Console.ForegroundColor = ConsoleColor.Cyan;
//            Console.WriteLine($"\n[SYSTEM] Uruchamiam {botCount} generatorów ciasteczek w tle...");
//            Console.WriteLine($"[SYSTEM] HEAD START: Czekam {headStartSeconds} sekund na rozgrzanie magazynu...");
//            Console.ResetColor();

//            for (int i = 0; i < botCount; i++)
//            {
//                int botId = i + 1;
//                Task.Run(() => RunGeneratorLoop(botId, _generatorCts.Token));
//            }

//            for (int i = 0; i < headStartSeconds; i++)
//            {
//                if (_cookieWarehouse.Count >= botCount)
//                {
//                    Console.WriteLine("\n[SYSTEM] Magazyn zapełniony szybciej niż timeout! Ruszamy.");
//                    break;
//                }

//                if (_generatorCts.Token.IsCancellationRequested) break;

//                await Task.Delay(1000);
//                Console.Write(".");
//            }
//            Console.WriteLine($"\n[SYSTEM] Rozgrzewka zakończona. Dostępne sesje w magazynie: {_cookieWarehouse.Count}");
//        }

//        public static void StopAndCleanUp()
//        {
//            lock (_lockObj)
//            {
//                if (!_generatorsStarted) return;

//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine("\n[SYSTEM] ZATRZYMANIE AWARYJNE: Wyłączanie generatorów i czyszczenie magazynu...");
//                Console.ResetColor();

//                _generatorCts.Cancel();

//                while (_cookieWarehouse.TryTake(out _)) { }

//                Console.WriteLine($"[SYSTEM] Magazyn wyczyszczony. Stan: {_cookieWarehouse.Count}");

//                _generatorsStarted = false;
//            }
//        }

//        private static void RunGeneratorLoop(int id, CancellationToken token)
//        {
//            while (!token.IsCancellationRequested)
//            {
//                try
//                {

//                    if (_cookieWarehouse.Count > 100)
//                    {
//                        Thread.Sleep(2000);
//                        continue;
//                    }

//                    var container = GenerateSingleSession(id);

//                    if (container != null && !token.IsCancellationRequested)
//                    {
//                        _cookieWarehouse.Add(container, token);

//                        Console.ForegroundColor = ConsoleColor.Green;
//                        Console.WriteLine($"[GEN-{id}] +1 Sesja. Magazyn: {_cookieWarehouse.Count}");
//                        Console.ResetColor();
//                    }
//                }
//                catch (OperationCanceledException)
//                {
//                    Console.WriteLine($"[GEN-{id}] Zatrzymano pracę (Cancellation Token).");
//                    break;
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"[GENERATOR-{id} ERROR] {ex.Message}. Ponawiam...");
//                    Thread.Sleep(5000);
//                }
//            }
//        }

//        private static CookieContainer? GenerateSingleSession(int botId)
//        {

//            var options = new ChromeOptions();
//            options.AddArgument("--headless=new");
//            options.AddArgument("--window-size=1920,1080");
//            options.AddArgument("--disable-blink-features=AutomationControlled");
//            options.AddArgument($"user-agent={UserAgent}");
//            options.AddArgument("--log-level=3");

//            var searchQueries = new[]
//            {
//                "iphone 15 pro", "samsung galaxy s24", "laptop dell", "karta graficzna rtx 4060",
//                "buty nike air max", "adidas ultraboost", "kurtka the north face", "plecak vans",
//                "ekspres do kawy delonghi", "odkurzacz dyson v15", "robot sprzątający roborock",
//                "klocki lego star wars", "konsola ps5 slim", "pad xbox series x", "nintendo switch",
//                "wiertarka wkrętarka makita", "zestaw kluczy yato", "kosiarka spalinowa",
//                "rower górski kross", "namiot 4 osobowy", "buty trekkingowe salomon"
//            };

//            var randomQuery = searchQueries[new Random().Next(searchQueries.Length)].Replace(" ", "+");

//            try
//            {
//                using (var driver = new ChromeDriver(options))
//                {
//                    string targetUrl = $"https://www.google.com/search?q={randomQuery}&tbm=shop";
//                    driver.Navigate().GoToUrl(targetUrl);

//                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

//                    try
//                    {
//                        var consentButton = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'QS5gu') and (contains(., 'Zaakceptuj') or contains(., 'Odrzuć'))]")));
//                        Thread.Sleep(new Random().Next(200, 500));
//                        consentButton.Click();
//                    }
//                    catch { }

//                    Thread.Sleep(500);

//                    try
//                    {
//                        var productCard = wait.Until(d => d.FindElement(By.CssSelector("div.njFjte[role='button']")));
//                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", productCard);
//                        Thread.Sleep(500);
//                        productCard.Click();
//                        Thread.Sleep(2500);
//                    }
//                    catch { }

//                    var cookies = driver.Manage().Cookies.AllCookies;
//                    var cookieContainer = new CookieContainer();
//                    foreach (var c in cookies)
//                    {
//                        try { cookieContainer.Add(new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain)); } catch { }
//                    }
//                    return cookieContainer;
//                }
//            }
//            catch
//            {
//                return null;
//            }
//        }

//        private HttpClient _httpClient;
//        private int _requestsOnCurrentIdentity = 0;

//        public GoogleMainPriceScraper()
//        {

//            LoadNewIdentityFromWarehouse();
//        }

//        private void LoadNewIdentityFromWarehouse()
//        {
//            try
//            {

//                var cookieContainer = _cookieWarehouse.Take();
//                _requestsOnCurrentIdentity = 0;
//                var handler = new HttpClientHandler
//                {
//                    CookieContainer = cookieContainer,
//                    UseCookies = true,
//                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
//                };
//                _httpClient = new HttpClient(handler);
//                _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
//                _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
//                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not A(Brand\";v=\"99\", \"Google Chrome\";v=\"121\", \"Chromium\";v=\"121\"");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
//            }
//            catch (InvalidOperationException)
//            {

//                Console.WriteLine("[SCRAPER] Nie udało się pobrać ciastek (Magazyn zamknięty).");
//            }
//        }

//        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//        {
//            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
//            var allFoundOffers = new List<TempOffer>();

//            if (!_generatorsStarted)
//            {
//                Console.WriteLine("[INFO] Generatory zatrzymane. Przerywam scrapowanie.");
//                return finalPriceHistory;
//            }

//            Console.WriteLine($"\n[INFO] Start scrapowania dla ID: {coOfr.Id}...");

//            string urlTemplate;
//            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
//            {
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }
//            else
//            {
//                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);
//                if (string.IsNullOrEmpty(catalogId))
//                {
//                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
//                    return finalPriceHistory;
//                }
//                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                else
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }

//            var allOffers = new List<TempOffer>();
//            string? firstPageRawResponse = null;
//            int startIndex = 0;
//            const int pageSize = 10;
//            int lastFetchCount;
//            const int maxRetries = 3;

//            do
//            {
//                string currentUrl = string.Format(urlTemplate, startIndex);
//                List<TempOffer> newOffers = new List<TempOffer>();
//                string rawResponse = "";

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {

//                    if (!_generatorsStarted) return finalPriceHistory;

//                    try
//                    {
//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] GET OAPV (Start: {startIndex}, Magazyn: {_cookieWarehouse.Count}):");
//                        Console.ResetColor();

//                        _requestsOnCurrentIdentity++;

//                        rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                        if (rawResponse.Length < 100 && rawResponse.Contains("ProductDetailsResult\":[]"))
//                        {
//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[BLOKADA] Pusta odpowiedź! Biorę nowe ciastka z magazynu...");
//                            Console.ResetColor();

//                            Console.ForegroundColor = ConsoleColor.Blue;
//                            Console.WriteLine($"[STATS] Ciastko spalone po {_requestsOnCurrentIdentity} requestach.");
//                            Console.ResetColor();

//                            LoadNewIdentityFromWarehouse();
//                            continue;
//                        }

//                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);
//                        if (newOffers.Count > 0) break;

//                        if (attempt < maxRetries) await Task.Delay(1500);
//                    }
//                    catch (HttpRequestException ex)
//                    {
//                        Console.WriteLine($"[ERROR HTTP] {ex.Message}. Biorę nowe ciastka.");
//                        LoadNewIdentityFromWarehouse();
//                        if (attempt < maxRetries) await Task.Delay(2000);
//                    }
//                }

//                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse)) firstPageRawResponse = rawResponse;
//                lastFetchCount = newOffers.Count;
//                Console.WriteLine($"   [INFO] Znaleziono ofert na stronie: {lastFetchCount}");

//                foreach (var offer in newOffers)
//                {
//                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
//                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url)) allFoundOffers.Add(offerWithIndex);
//                }

//                startIndex += pageSize;
//                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(5, 15));

//            } while (lastFetchCount == pageSize);

//            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse) && _generatorsStarted)
//            {
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers.Where(o => o.Currency == "PLN").Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
//                    if (prices.Any()) baselinePrice = prices.OrderBy(p => p).ToList()[prices.Count / 2];
//                }

//                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);
//                if (!string.IsNullOrEmpty(productTitle))
//                {
//                    string encodedQ = Uri.EscapeDataString(productTitle);
//                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

//                    try
//                    {
//                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
//                        if (wrgaResponse.Length > 100)
//                        {
//                            var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);
//                            foreach (var off in wrgaOffers)
//                            {
//                                if (baselinePrice > 0 && off.Currency == "PLN")
//                                {
//                                    decimal wrgaPrice = ParsePrice(off.Price);
//                                    decimal diff = wrgaPrice - baselinePrice;
//                                    decimal percentageDiff = diff / baselinePrice;
//                                    if (percentageDiff < -0.8m || percentageDiff > 2.0m) continue;
//                                }
//                                if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
//                                {
//                                    allFoundOffers.Add(off with { OriginalIndex = allFoundOffers.Count + 1 });
//                                }
//                            }
//                        }
//                    }
//                    catch { }
//                }
//            }

//            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
//            var debugMainList = new List<dynamic>();
//            var debugAdditionalList = new List<dynamic>();

//            foreach (var group in groupedBySeller)
//            {
//                var bestValidOffer = group.Where(o => o.Condition == "NOWY" && o.Currency == "PLN").OrderBy(o => ParsePrice(o.Price)).FirstOrDefault();
//                var sortedStoreOffers = group.OrderBy(o => ParsePrice(o.Price)).ToList();

//                foreach (var offer in sortedStoreOffers)
//                {
//                    bool isBest = (bestValidOffer != null && offer == bestValidOffer);
//                    if (isBest)
//                    {
//                        string? isBiddingValue = null;
//                        if (!string.IsNullOrEmpty(offer.Badge))
//                        {
//                            string b = offer.Badge.ToLower();
//                            if (b.Contains("cena")) isBiddingValue = "bpg";
//                            else if (b.Contains("popularn") || b.Contains("wybór")) isBiddingValue = "hpg";
//                        }

//                        finalPriceHistory.Add(new CoOfrPriceHistoryClass
//                        {
//                            CoOfrClassId = coOfr.Id,
//                            GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl),
//                            GoogleStoreName = offer.Seller,
//                            GooglePrice = ParsePrice(offer.Price),
//                            GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                            GooglePosition = offer.OriginalIndex.ToString(),
//                            IsBidding = isBiddingValue,
//                            GoogleInStock = offer.IsInStock,
//                            GoogleOfferPerStoreCount = group.Count()
//                        });
//                    }

//                    var debugItem = new
//                    {
//                        Pos = isBest ? offer.OriginalIndex.ToString() : "-",
//                        GPos = offer.OriginalIndex,
//                        Stock = offer.IsInStock ? "OK" : "BRAK",
//                        Cond = offer.Condition,
//                        Curr = offer.Currency,
//                        Info = offer.Badge ?? "-",
//                        Method = offer.Method,
//                        Seller = offer.Seller,
//                        Price = ParsePrice(offer.Price),
//                        Del = ParseDeliveryPrice(offer.Delivery),
//                        Total = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                        Url = offer.Url,
//                        IsMain = isBest
//                    };
//                    if (isBest) debugMainList.Add(debugItem); else debugAdditionalList.Add(debugItem);
//                }
//            }

//            finalPriceHistory = finalPriceHistory.OrderBy(x => x.GooglePrice).ToList();
//            debugMainList = debugMainList.OrderBy(x => x.Price).ToList();

//            for (int i = 0; i < debugMainList.Count; i++)
//            {
//                var old = debugMainList[i];
//                debugMainList[i] = new { old.Pos, old.GPos, old.Stock, old.Cond, old.Curr, old.Info, old.Method, old.Seller, old.Price, old.Del, old.Total, old.Url, old.IsMain, ListPos = (i + 1).ToString() };
//            }
//            debugAdditionalList = debugAdditionalList.OrderBy(x => x.Seller).ThenBy(x => x.Price).ToList();

//            Console.WriteLine("\n===============================================================================================================================================================================");
//            Console.WriteLine($" TABELA GŁÓWNA (ID: {coOfr.Id}) - Najlepsze: {debugMainList.Count}");
//            Console.WriteLine("===============================================================================================================================================================================");
//            Console.WriteLine($"{"Poz.",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//            foreach (var item in debugMainList) PrintDebugRow(item, item.ListPos);

//            if (debugAdditionalList.Any())
//            {
//                Console.WriteLine("\n--- DODATKOWE ---");
//                foreach (var item in debugAdditionalList) PrintDebugRow(item, "-");
//            }

//            return finalPriceHistory;
//        }

//        #region Helper Methods
//        private void PrintDebugRow(dynamic item, string posLabel)
//        {
//            string infoCode = item.Info;
//            if (infoCode.Length > 15) infoCode = infoCode.Substring(0, 12) + "...";
//            string seller = item.Seller;
//            if (seller.Length > 20) seller = seller.Substring(0, 17) + "...";

//            Console.Write($"{posLabel,-4} | {item.GPos,-5} | {item.Stock,-4} | ");

//            if (item.Cond.Contains("UŻYW") || item.Cond.Contains("OUTLET")) Console.ForegroundColor = ConsoleColor.Red;
//            else Console.ForegroundColor = ConsoleColor.Green;
//            Console.Write($"{item.Cond,-6}");
//            Console.ResetColor();
//            Console.Write(" | ");

//            if (item.Curr != "PLN") Console.ForegroundColor = ConsoleColor.Magenta;
//            else Console.ForegroundColor = ConsoleColor.Gray;
//            Console.Write($"{item.Curr,-6}");
//            Console.ResetColor();

//            Console.WriteLine($" | {infoCode,-15} | {item.Method,-6} | {seller,-20} | {item.Price,-10} | {item.Del,-9} | {item.Total,-10} | {item.Url}");
//        }

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
//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result)) return result;
//            return 0;
//        }

//        private decimal ParseDeliveryPrice(string? deliveryText)
//        {
//            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa")) return 0;
//            return ParsePrice(deliveryText);
//        }
//        #endregion
//    }

//    public static class GoogleShoppingApiParser
//    {
//        public static List<TempOffer> ParseWrga(string rawResponse)
//        {
//            var offers = new List<TempOffer>();
//            if (string.IsNullOrWhiteSpace(rawResponse)) return offers;
//            try
//            {
//                string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//                string decodedContent = Regex.Unescape(cleaned);
//                decodedContent = WebUtility.HtmlDecode(decodedContent);
//                var blockRegex = new Regex(@"<div[^>]*class=""[^""]*tF2Cxc[^""]*""[^>]*>([\s\S]*?)(?=<div[^>]*class=""[^""]*tF2Cxc|$)", RegexOptions.IgnoreCase);
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
//                    string condition = "NOWY";
//                    string blockLower = block.ToLower();
//                    var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                    if (usedKeywords.Any(k => blockLower.Contains(k))) condition = "UŻYWANY";
//                    string currency = "PLN";
//                    if ((blockLower.Contains("eur") || blockLower.Contains("€")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "EUR";
//                    else if ((blockLower.Contains("usd") || blockLower.Contains("$")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "USD";
//                    priceVal = ExtractRichSnippetPrice(block);
//                    if (priceVal > 0) { deliveryVal = ExtractDeliveryFromRichSnippet(block); badge = "RICH_SNIPPET"; }
//                    else { string cleanText = StripHtml(block); var analysisResult = AnalyzePricesInBlock(cleanText); priceVal = analysisResult.Price; deliveryVal = analysisResult.Delivery; badge = "TEXT_ANALYSIS"; }
//                    if (priceVal > 0)
//                    {
//                        offers.Add(new TempOffer(seller, priceVal.ToString("F2"), url, deliveryVal > 0 ? deliveryVal.ToString("F2") : "0", true, badge, offerIndex, "WRGA", null, null, condition, currency));
//                    }
//                }
//            }
//            catch { }
//            return offers;
//        }

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
//            catch (JsonException) { return new List<TempOffer>(); }
//        }

//        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//        {
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//                {
//                    foreach (JsonElement potentialOffer in node.EnumerateArray())
//                    {
//                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url)) allOffers.Add(offer);
//                    }
//                }
//            }
//            if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) FindAndParseAllOffers(root, element, allOffers); }
//            else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) FindAndParseAllOffers(root, property.Value, allOffers); }
//        }

//        private static bool IsPotentialSingleOffer(JsonElement node)
//        {
//            if (node.ValueKind != JsonValueKind.Array) return false;
//            int arrayChildren = 0; int primitiveChildren = 0;
//            foreach (var child in node.EnumerateArray()) { if (child.ValueKind == JsonValueKind.Array) arrayChildren++; else if (child.ValueKind == JsonValueKind.String || child.ValueKind == JsonValueKind.Number || child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False) primitiveChildren++; }
//            if (arrayChildren > 1 && primitiveChildren == 0) return false;
//            JsonElement offerData = node;
//            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array) offerData = node[0];
//            if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;
//            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//            if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google") && !s.Contains("gstatic"))) return true;
//            return false;
//        }

//        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//        {
//            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
//            if (offerData.ValueKind != JsonValueKind.Array) return null;
//            try
//            {
//                var allNodes = Flatten(offerData).ToList();
//                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//                string? seller = null;
//                if (!string.IsNullOrEmpty(url)) seller = GetDomainName(url);
//                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
//                {
//                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };
//                    foreach (var s in flatStrings) { if (!s.StartsWith("http") && s.Length > 2 && !blacklist.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) && !Regex.IsMatch(s, @"\d")) { seller = s; break; } }
//                }
//                if (seller == null && url != null) seller = GetDomainName(url);
//                string condition = "NOWY";
//                var usedKeywords = new[] { "pre-owned", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                foreach (var text in flatStrings)
//                {
//                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3) continue;
//                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
//                    string lowerText = text.ToLower();
//                    if (lowerText.Contains("nie używany") || lowerText.Contains("nieużywany")) continue;
//                    if (lowerText.Contains("nowy") && !lowerText.Contains("jak nowy")) continue;
//                    foreach (var keyword in usedKeywords) { if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) { if (keyword == "używany" && (lowerText.Contains("fabrycznie nowy") || lowerText.Contains("produkt nowy"))) continue; condition = "UŻYWANY"; goto ConditionFound; } }
//                }
//            ConditionFound:;
//                bool isInStock = true;
//                bool hasPositiveStockText = flatStrings.Any(s => s.Contains("W magazynie", StringComparison.OrdinalIgnoreCase) || s.Contains("Dostępny", StringComparison.OrdinalIgnoreCase) || s.Equals("In stock", StringComparison.OrdinalIgnoreCase));
//                if (hasPositiveStockText) isInStock = true;
//                else { var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "wyprzedany", "chwilowy brak" }; if (flatStrings.Any(text => text.Length < 50 && outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false; }
//                List<(decimal Amount, string Currency)> structuralPrices = new();
//                for (int i = 1; i < allNodes.Count; i++)
//                {
//                    var current = allNodes[i];
//                    if (current.ValueKind == JsonValueKind.String)
//                    {
//                        string currCode = current.GetString()?.ToUpper() ?? "";
//                        if (currCode == "PLN" || currCode == "EUR" || currCode == "USD" || currCode == "GBP")
//                        {
//                            long micros = 0; bool foundPrice = false;
//                            var prev = allNodes[i - 1];
//                            if (prev.ValueKind == JsonValueKind.Number) { micros = prev.GetInt64(); foundPrice = true; }
//                            else if (i >= 2) { var prevPrev = allNodes[i - 2]; if (prevPrev.ValueKind == JsonValueKind.Number) { micros = prevPrev.GetInt64(); foundPrice = true; } }
//                            if (foundPrice && micros >= 1000000) { structuralPrices.Add((micros / 1000000m, currCode)); if (currCode == "PLN") break; }
//                        }
//                    }
//                }
//                bool hasTextualForeignEvidence = false; string foreignTextCurrency = "";
//                var foreignRegex = new Regex(@"[\(\s](€|EUR|\$|USD)\s*(\d+[.,]?\d*)", RegexOptions.IgnoreCase);
//                foreach (var s in flatStrings) { Match m = foreignRegex.Match(s); if (m.Success) { hasTextualForeignEvidence = true; string symbol = m.Groups[1].Value.ToUpper(); foreignTextCurrency = (symbol.Contains("EUR") || symbol.Contains("€")) ? "EUR" : "USD"; break; } }
//                string? finalPrice = null; string finalCurrency = "PLN";
//                var plnNode = structuralPrices.FirstOrDefault(x => x.Currency == "PLN");
//                if (plnNode != default) { finalPrice = plnNode.Amount.ToString("F2", CultureInfo.InvariantCulture); if (hasTextualForeignEvidence) finalCurrency = foreignTextCurrency; else finalCurrency = "PLN"; }
//                else if (structuralPrices.Any(x => x.Currency != "PLN")) { var foreign = structuralPrices.First(x => x.Currency != "PLN"); finalPrice = foreign.Amount.ToString("F2", CultureInfo.InvariantCulture); finalCurrency = foreign.Currency; }
//                else return null;
//                string? delivery = null;
//                if (flatStrings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) || s.Contains("Bezpłatnie", StringComparison.OrdinalIgnoreCase) || s.Contains("Free delivery", StringComparison.OrdinalIgnoreCase))) delivery = "Bezpłatna";
//                else
//                {
//                    var plusRegex = new Regex(@"^\+\s*(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                    foreach (var s in flatStrings) { var match = plusRegex.Match(s.Trim()); if (match.Success) { delivery = ParsePriceDecimal(match.Groups[1].Value).ToString("F2"); break; } }
//                    if (delivery == null)
//                    {
//                        var deliveryTextRegex = new Regex(@"(?:dostawa|wysyłka|delivery|shipping)(?:[^0-9]{0,30})(\d+[.,]\d{2})\s*(?:PLN|zł)|za\s+(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                        foreach (var s in flatStrings)
//                        {
//                            if (!s.ToLower().Contains("dostaw") && !s.ToLower().Contains("wysyłk") && !s.ToLower().Contains("delivery") && !s.ToLower().Contains(" za ")) continue;
//                            var match = deliveryTextRegex.Match(s);
//                            if (match.Success) { string priceStr = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value; decimal delPrice = ParsePriceDecimal(priceStr); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } }
//                        }
//                    }
//                    if (delivery == null) { for (int i = 0; i < allNodes.Count - 1; i++) { var node = allNodes[i]; if (node.ValueKind == JsonValueKind.Number) { try { long val = node.GetInt64(); if (val == 110720 && i > 0) { var prevNode = allNodes[i - 1]; if (prevNode.ValueKind == JsonValueKind.String) { string delText = prevNode.GetString()!; var priceMatch = Regex.Match(delText, @"(\d+[.,]\d{2})"); if (priceMatch.Success) { decimal delPrice = ParsePriceDecimal(priceMatch.Groups[1].Value); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } } } } } catch { } } } }
//                }
//                string? badge = ExtractBadgeStrict(offerData);
//                if (string.IsNullOrEmpty(badge)) { string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }; badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase))); }
//                if (!string.IsNullOrWhiteSpace(seller) && finalPrice != null && url != null) return new TempOffer(seller, finalPrice, url, delivery, isInStock, badge, 0, "OAPV", null, null, condition, finalCurrency);
//            }
//            catch { }
//            return null;
//        }

//        public static string? ExtractProductTitle(string rawResponse)
//        {
//            try { string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse; using JsonDocument doc = JsonDocument.Parse(cleaned); if (doc.RootElement.TryGetProperty("ProductDetailsResult", out JsonElement pd) && pd.GetArrayLength() > 0) return pd[0].GetString(); } catch { }
//            return null;
//        }

//        private static string GetDomainName(string url) { try { var host = new Uri(url).Host.ToLower().Replace("www.", ""); return char.ToUpper(host[0]) + host.Substring(1); } catch { return "Nieznany"; } }
//        private static bool IsGoogleLink(string url) { return url.Contains(".google.") || url.Contains("gstatic.") || url.Contains("/search?") || url.Contains("youtube.") || url.Contains("googleusercontent") || url.Contains("translate.google"); }
//        private static string? ExtractBadgeStrict(JsonElement offerData) { try { foreach (var element in offerData.EnumerateArray()) { if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0) { var inner = element[0]; if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0) { var potentialBadgeNode = inner[0]; if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1) { if (potentialBadgeNode[0].ValueKind == JsonValueKind.String) { string text = potentialBadgeNode[0].GetString()!; if (new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))) return text; } } } } } } catch { } return null; }
//        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0) { if (currentDepth > maxDepth) yield break; yield return node; if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) { foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child; } } else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) { foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child; } } }
//        private static decimal ExtractRichSnippetPrice(string htmlBlock) { var richPriceRegex = new Regex(@"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>", RegexOptions.IgnoreCase); var match = richPriceRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
//        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock) { var deliveryRegex = new Regex(@"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)", RegexOptions.IgnoreCase); var match = deliveryRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
//        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text) { var priceRegex = new Regex(@"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?", RegexOptions.IgnoreCase); var matches = priceRegex.Matches(text); decimal bestPrice = 0; decimal bestDelivery = 0; foreach (Match m in matches) { if (!decimal.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue; if (val < 1.0m) continue; int contextStart = Math.Max(0, m.Index - 30); int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20); string snippetForLog = text.Substring(contextStart, contextLen).ToLower(); if (Regex.IsMatch(snippetForLog, @"(dostawa|wysyłka|delivery|\+)")) { if (bestDelivery == 0) bestDelivery = val; } else if (bestPrice == 0) { bestPrice = val; } } return (bestPrice, bestDelivery); }
//        private static string StripHtml(string html) { if (string.IsNullOrEmpty(html)) return ""; string s = html.Replace("<br>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " "); s = Regex.Replace(s, "<.*?>", " "); s = WebUtility.HtmlDecode(s); s = Regex.Replace(s, @"\s+", " ").Trim(); return s; }
//        private static decimal ParsePriceDecimal(string priceStr) { if (string.IsNullOrEmpty(priceStr)) return 0; string clean = Regex.Replace(priceStr, @"[^\d,.]", ""); if (clean.Contains(",") && clean.Contains(".")) { if (clean.LastIndexOf(',') > clean.LastIndexOf('.')) clean = clean.Replace(".", "").Replace(",", "."); else clean = clean.Replace(",", ""); } else if (clean.Contains(",")) { clean = clean.Replace(",", "."); } else if (clean.Count(c => c == '.') > 1) { int lastDot = clean.LastIndexOf('.'); clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot); } if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res; return 0; }
//    }
//}








//poprawiony zapis i logi




//using OpenQA.Selenium;
//using OpenQA.Selenium.Chrome;
//using OpenQA.Selenium.Support.UI;
//using PriceSafari.Models;
//using Microsoft.Extensions.DependencyInjection;
//using PriceSafari.Data;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading;
//using System.Threading.Tasks;

//namespace PriceSafari.Services
//{
//    // --- NOWOŚĆ: SYSTEM BATCHINGU (MAGAZYNIER WYNIKÓW) ---
//    public static class ResultBatchProcessor
//    {
//        private static readonly ConcurrentQueue<CoOfrPriceHistoryClass> _resultsQueue = new();
//        private static readonly CancellationTokenSource _cts = new();
//        private static Task? _processorTask;
//        private static IServiceScopeFactory _scopeFactory;

//        // Konfiguracja: Zapis co 50 sztuk lub co 3 sekundy
//        private const int BATCH_SIZE_TRIGGER = 50;
//        private const int FLUSH_INTERVAL_MS = 3000;

//        public static void Initialize(IServiceScopeFactory scopeFactory)
//        {
//            _scopeFactory = scopeFactory;
//            if (_processorTask != null) return;

//            _processorTask = Task.Run(async () =>
//            {
//                Console.WriteLine("[BATCH] Startuje asynchroniczny magazynier wyników...");
//                while (!_cts.Token.IsCancellationRequested)
//                {
//                    try
//                    {
//                        await Task.Delay(FLUSH_INTERVAL_MS, _cts.Token);
//                        await FlushQueueAsync();
//                    }
//                    catch (OperationCanceledException) { break; }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"[BATCH ERROR] Main Loop: {ex.Message}");
//                    }
//                }
//            });
//        }

//        public static void Enqueue(IEnumerable<CoOfrPriceHistoryClass> items)
//        {
//            foreach (var item in items) _resultsQueue.Enqueue(item);
//        }

//        public static async Task StopAndFlushAsync()
//        {
//            _cts.Cancel();
//            await FlushQueueAsync(); // Zapisz resztki
//            Console.WriteLine("[BATCH] Zatrzymano procesor.");
//        }

//        private static async Task FlushQueueAsync()
//        {
//            if (_resultsQueue.IsEmpty) return;

//            var batch = new List<CoOfrPriceHistoryClass>();
//            // Pobieramy max 500 na raz, żeby nie zatkać SQL transakcji
//            while (_resultsQueue.TryDequeue(out var item) && batch.Count < 500)
//            {
//                batch.Add(item);
//            }

//            if (batch.Any())
//            {
//                try
//                {
//                    using (var scope = _scopeFactory.CreateScope())
//                    {
//                        var db = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                        // Bulk Insert (AddRange jest wystarczająco szybki dla paczek 500)
//                        await db.CoOfrPriceHistories.AddRangeAsync(batch);
//                        await db.SaveChangesAsync();
//                        Console.WriteLine($"[BATCH] Zapisano paczkę: {batch.Count} rekordów.");
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"[BATCH CRITICAL ERROR] Nie udało się zapisać {batch.Count} rekordów: {ex.Message}");
//                    // W produkcji można tu dodać mechanizm retry (ponownego dodania do kolejki)
//                }
//            }
//        }
//    }
//    // -----------------------------------------------------

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
//        string? RatingCount = null,
//        string Condition = "NOWY",
//        string Currency = "PLN"
//    );

//    public class GoogleMainPriceScraper
//    {
//        private const decimal WRGA_PRICE_DEVIATION_LIMIT = 0.8m;
//        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

//        private static readonly BlockingCollection<CookieContainer> _cookieWarehouse = new BlockingCollection<CookieContainer>();

//        private static CancellationTokenSource _generatorCts = new CancellationTokenSource();
//        private static bool _generatorsStarted = false;
//        private static readonly object _lockObj = new object();

//        public static async Task InitializeGeneratorsAsync(int botCount, int headStartSeconds = 20)
//        {
//            lock (_lockObj)
//            {
//                if (_generatorsStarted) return;

//                if (_generatorCts.IsCancellationRequested)
//                {
//                    _generatorCts.Dispose();
//                    _generatorCts = new CancellationTokenSource();
//                }

//                _generatorsStarted = true;
//            }

//            Console.ForegroundColor = ConsoleColor.Cyan;
//            Console.WriteLine($"\n[SYSTEM] Uruchamiam {botCount} generatorów ciasteczek w tle...");
//            Console.WriteLine($"[SYSTEM] HEAD START: Czekam {headStartSeconds} sekund na rozgrzanie magazynu...");
//            Console.ResetColor();

//            for (int i = 0; i < botCount; i++)
//            {
//                int botId = i + 1;
//                Task.Run(() => RunGeneratorLoop(botId, _generatorCts.Token));
//            }

//            for (int i = 0; i < headStartSeconds; i++)
//            {
//                if (_cookieWarehouse.Count >= botCount)
//                {
//                    Console.WriteLine("\n[SYSTEM] Magazyn zapełniony szybciej niż timeout! Ruszamy.");
//                    break;
//                }

//                if (_generatorCts.Token.IsCancellationRequested) break;

//                await Task.Delay(1000);
//                Console.Write(".");
//            }
//            Console.WriteLine($"\n[SYSTEM] Rozgrzewka zakończona. Dostępne sesje w magazynie: {_cookieWarehouse.Count}");
//        }

//        public static void StopAndCleanUp()
//        {
//            lock (_lockObj)
//            {
//                if (!_generatorsStarted) return;

//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine("\n[SYSTEM] ZATRZYMANIE AWARYJNE: Wyłączanie generatorów i czyszczenie magazynu...");
//                Console.ResetColor();

//                _generatorCts.Cancel();

//                while (_cookieWarehouse.TryTake(out _)) { }

//                Console.WriteLine($"[SYSTEM] Magazyn wyczyszczony. Stan: {_cookieWarehouse.Count}");

//                _generatorsStarted = false;
//            }
//        }

//        private static void RunGeneratorLoop(int id, CancellationToken token)
//        {
//            while (!token.IsCancellationRequested)
//            {
//                try
//                {

//                    if (_cookieWarehouse.Count > 100)
//                    {
//                        Thread.Sleep(2000);
//                        continue;
//                    }

//                    var container = GenerateSingleSession(id);

//                    if (container != null && !token.IsCancellationRequested)
//                    {
//                        _cookieWarehouse.Add(container, token);

//                        Console.ForegroundColor = ConsoleColor.Green;
//                        Console.WriteLine($"[GEN-{id}] +1 Sesja. Magazyn: {_cookieWarehouse.Count}");
//                        Console.ResetColor();
//                    }
//                }
//                catch (OperationCanceledException)
//                {
//                    Console.WriteLine($"[GEN-{id}] Zatrzymano pracę (Cancellation Token).");
//                    break;
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"[GENERATOR-{id} ERROR] {ex.Message}. Ponawiam...");
//                    Thread.Sleep(5000);
//                }
//            }
//        }

//        private static CookieContainer? GenerateSingleSession(int botId)
//        {

//            var options = new ChromeOptions();
//            options.AddArgument("--headless=new");
//            options.AddArgument("--window-size=1920,1080");
//            options.AddArgument("--disable-blink-features=AutomationControlled");
//            options.AddArgument($"user-agent={UserAgent}");
//            options.AddArgument("--log-level=3");

//            var searchQueries = new[]
//            {
//                "iphone 15 pro", "samsung galaxy s24", "laptop dell", "karta graficzna rtx 4060",
//                "buty nike air max", "adidas ultraboost", "kurtka the north face", "plecak vans",
//                "ekspres do kawy delonghi", "odkurzacz dyson v15", "robot sprzątający roborock",
//                "klocki lego star wars", "konsola ps5 slim", "pad xbox series x", "nintendo switch",
//                "wiertarka wkrętarka makita", "zestaw kluczy yato", "kosiarka spalinowa",
//                "rower górski kross", "namiot 4 osobowy", "buty trekkingowe salomon"
//            };

//            var randomQuery = searchQueries[new Random().Next(searchQueries.Length)].Replace(" ", "+");

//            try
//            {
//                using (var driver = new ChromeDriver(options))
//                {
//                    string targetUrl = $"https://www.google.com/search?q={randomQuery}&tbm=shop";
//                    driver.Navigate().GoToUrl(targetUrl);

//                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

//                    try
//                    {
//                        var consentButton = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'QS5gu') and (contains(., 'Zaakceptuj') or contains(., 'Odrzuć'))]")));
//                        Thread.Sleep(new Random().Next(200, 500));
//                        consentButton.Click();
//                    }
//                    catch { }

//                    Thread.Sleep(500);

//                    try
//                    {
//                        var productCard = wait.Until(d => d.FindElement(By.CssSelector("div.njFjte[role='button']")));
//                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", productCard);
//                        Thread.Sleep(500);
//                        productCard.Click();
//                        Thread.Sleep(2500);
//                    }
//                    catch { }

//                    var cookies = driver.Manage().Cookies.AllCookies;
//                    var cookieContainer = new CookieContainer();
//                    foreach (var c in cookies)
//                    {
//                        try { cookieContainer.Add(new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain)); } catch { }
//                    }
//                    return cookieContainer;
//                }
//            }
//            catch
//            {
//                return null;
//            }
//        }

//        private HttpClient _httpClient;
//        private int _requestsOnCurrentIdentity = 0;

//        public GoogleMainPriceScraper()
//        {

//            LoadNewIdentityFromWarehouse();
//        }

//        private void LoadNewIdentityFromWarehouse()
//        {
//            try
//            {

//                var cookieContainer = _cookieWarehouse.Take();
//                _requestsOnCurrentIdentity = 0;
//                var handler = new HttpClientHandler
//                {
//                    CookieContainer = cookieContainer,
//                    UseCookies = true,
//                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
//                };
//                _httpClient = new HttpClient(handler);
//                _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
//                _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
//                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not A(Brand\";v=\"99\", \"Google Chrome\";v=\"121\", \"Chromium\";v=\"121\"");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
//                _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
//            }
//            catch (InvalidOperationException)
//            {

//                Console.WriteLine("[SCRAPER] Nie udało się pobrać ciastek (Magazyn zamknięty).");
//            }
//        }

//        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
//        {
//            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
//            var allFoundOffers = new List<TempOffer>();

//            if (!_generatorsStarted)
//            {
//                Console.WriteLine("[INFO] Generatory zatrzymane. Przerywam scrapowanie.");
//                return finalPriceHistory;
//            }

//            Console.WriteLine($"\n[INFO] Start scrapowania dla ID: {coOfr.Id}...");

//            string urlTemplate;
//            if (coOfr.UseGoogleHidOffer && !string.IsNullOrEmpty(coOfr.GoogleHid))
//            {
//                urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},headlineOfferDocid:{coOfr.GoogleHid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }
//            else
//            {
//                string? catalogId = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl);
//                if (string.IsNullOrEmpty(catalogId))
//                {
//                    Console.WriteLine($"[BŁĄD] Brak CID dla zadania ID: {coOfr.Id}");
//                    return finalPriceHistory;
//                }
//                if (!string.IsNullOrEmpty(coOfr.GoogleGid) && coOfr.UseGPID)
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//                else
//                    urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&pvf=GgIQAQ&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,pvf:GgIQAQ,_fmt:jspb";
//            }

//            string? firstPageRawResponse = null;
//            int startIndex = 0;
//            const int pageSize = 10;
//            int lastFetchCount;
//            const int maxRetries = 3;

//            do
//            {
//                string currentUrl = string.Format(urlTemplate, startIndex);
//                List<TempOffer> newOffers = new List<TempOffer>();
//                string rawResponse = "";

//                for (int attempt = 1; attempt <= maxRetries; attempt++)
//                {

//                    if (!_generatorsStarted) return finalPriceHistory;

//                    try
//                    {
//                        Console.ForegroundColor = ConsoleColor.Magenta;
//                        Console.WriteLine($"\n[DEBUG HTTP] GET OAPV (Start: {startIndex}, Magazyn: {_cookieWarehouse.Count}):");
//                        Console.ResetColor();

//                        _requestsOnCurrentIdentity++;

//                        rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                        if (rawResponse.Length < 100 && rawResponse.Contains("ProductDetailsResult\":[]"))
//                        {
//                            Console.ForegroundColor = ConsoleColor.Red;
//                            Console.WriteLine($"[BLOKADA] Pusta odpowiedź! Biorę nowe ciastka z magazynu...");
//                            Console.ResetColor();

//                            Console.ForegroundColor = ConsoleColor.Blue;
//                            Console.WriteLine($"[STATS] Ciastko spalone po {_requestsOnCurrentIdentity} requestach.");
//                            Console.ResetColor();

//                            LoadNewIdentityFromWarehouse();
//                            continue;
//                        }

//                        newOffers = GoogleShoppingApiParser.ParseOapv(rawResponse);
//                        if (newOffers.Count > 0) break;

//                        if (attempt < maxRetries) await Task.Delay(1500);
//                    }
//                    catch (HttpRequestException ex)
//                    {
//                        Console.WriteLine($"[ERROR HTTP] {ex.Message}. Biorę nowe ciastka.");
//                        LoadNewIdentityFromWarehouse();
//                        if (attempt < maxRetries) await Task.Delay(2000);
//                    }
//                }

//                if (startIndex == 0 && !string.IsNullOrEmpty(rawResponse)) firstPageRawResponse = rawResponse;
//                lastFetchCount = newOffers.Count;
//                Console.WriteLine($"   [INFO] Znaleziono ofert na stronie: {lastFetchCount}");

//                foreach (var offer in newOffers)
//                {
//                    var offerWithIndex = offer with { OriginalIndex = allFoundOffers.Count + 1 };
//                    if (!allFoundOffers.Any(o => o.Url == offerWithIndex.Url)) allFoundOffers.Add(offerWithIndex);
//                }

//                startIndex += pageSize;
//                if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(5, 15));

//            } while (lastFetchCount == pageSize);

//            if (coOfr.UseWRGA && !string.IsNullOrEmpty(firstPageRawResponse) && _generatorsStarted)
//            {
//                decimal baselinePrice = 0;
//                if (allFoundOffers.Any())
//                {
//                    var prices = allFoundOffers.Where(o => o.Currency == "PLN").Select(o => ParsePrice(o.Price)).Where(p => p > 0).ToList();
//                    if (prices.Any()) baselinePrice = prices.OrderBy(p => p).ToList()[prices.Count / 2];
//                }

//                string? productTitle = GoogleShoppingApiParser.ExtractProductTitle(firstPageRawResponse);
//                if (!string.IsNullOrEmpty(productTitle))
//                {
//                    string encodedQ = Uri.EscapeDataString(productTitle);
//                    string wrgaUrl = $"https://www.google.com/async/wrga?q={encodedQ}&async=_fmt:prog,gl:4";

//                    try
//                    {
//                        string wrgaResponse = await _httpClient.GetStringAsync(wrgaUrl);
//                        if (wrgaResponse.Length > 100)
//                        {
//                            var wrgaOffers = GoogleShoppingApiParser.ParseWrga(wrgaResponse);
//                            foreach (var off in wrgaOffers)
//                            {
//                                if (baselinePrice > 0 && off.Currency == "PLN")
//                                {
//                                    decimal wrgaPrice = ParsePrice(off.Price);
//                                    decimal diff = wrgaPrice - baselinePrice;
//                                    decimal percentageDiff = diff / baselinePrice;
//                                    if (percentageDiff < -0.8m || percentageDiff > 2.0m) continue;
//                                }
//                                if (!allFoundOffers.Any(existing => AreUrlsEqual(existing.Url, off.Url)))
//                                {
//                                    allFoundOffers.Add(off with { OriginalIndex = allFoundOffers.Count + 1 });
//                                }
//                            }
//                        }
//                    }
//                    catch { }
//                }
//            }

//            var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
//            var debugMainList = new List<dynamic>();
//            var debugAdditionalList = new List<dynamic>();

//            foreach (var group in groupedBySeller)
//            {
//                var bestValidOffer = group.Where(o => o.Condition == "NOWY" && o.Currency == "PLN").OrderBy(o => ParsePrice(o.Price)).FirstOrDefault();
//                var sortedStoreOffers = group.OrderBy(o => ParsePrice(o.Price)).ToList();

//                foreach (var offer in sortedStoreOffers)
//                {
//                    bool isBest = (bestValidOffer != null && offer == bestValidOffer);
//                    if (isBest)
//                    {
//                        string? isBiddingValue = null;
//                        if (!string.IsNullOrEmpty(offer.Badge))
//                        {
//                            string b = offer.Badge.ToLower();
//                            if (b.Contains("cena")) isBiddingValue = "bpg";
//                            else if (b.Contains("popularn") || b.Contains("wybór")) isBiddingValue = "hpg";
//                        }

//                        finalPriceHistory.Add(new CoOfrPriceHistoryClass
//                        {
//                            CoOfrClassId = coOfr.Id,
//                            GoogleCid = coOfr.GoogleCid ?? ExtractProductId(coOfr.GoogleOfferUrl),
//                            GoogleStoreName = offer.Seller,
//                            GooglePrice = ParsePrice(offer.Price),
//                            GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                            GooglePosition = offer.OriginalIndex.ToString(),
//                            IsBidding = isBiddingValue,
//                            GoogleInStock = offer.IsInStock,
//                            GoogleOfferPerStoreCount = group.Count()
//                        });
//                    }

//                    var debugItem = new
//                    {
//                        Pos = isBest ? offer.OriginalIndex.ToString() : "-",
//                        GPos = offer.OriginalIndex,
//                        Stock = offer.IsInStock ? "OK" : "BRAK",
//                        Cond = offer.Condition,
//                        Curr = offer.Currency,
//                        Info = offer.Badge ?? "-",
//                        Method = offer.Method,
//                        Seller = offer.Seller,
//                        Price = ParsePrice(offer.Price),
//                        Del = ParseDeliveryPrice(offer.Delivery),
//                        Total = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
//                        Url = offer.Url,
//                        IsMain = isBest
//                    };
//                    if (isBest) debugMainList.Add(debugItem); else debugAdditionalList.Add(debugItem);
//                }
//            }

//            finalPriceHistory = finalPriceHistory.OrderBy(x => x.GooglePrice).ToList();
//            debugMainList = debugMainList.OrderBy(x => x.Price).ToList();

//            for (int i = 0; i < debugMainList.Count; i++)
//            {
//                var old = debugMainList[i];
//                debugMainList[i] = new { old.Pos, old.GPos, old.Stock, old.Cond, old.Curr, old.Info, old.Method, old.Seller, old.Price, old.Del, old.Total, old.Url, old.IsMain, ListPos = (i + 1).ToString() };
//            }
//            debugAdditionalList = debugAdditionalList.OrderBy(x => x.Seller).ThenBy(x => x.Price).ToList();

//            Console.WriteLine("\n===============================================================================================================================================================================");
//            Console.WriteLine($" TABELA GŁÓWNA (ID: {coOfr.Id}) - Najlepsze: {debugMainList.Count}");
//            Console.WriteLine("===============================================================================================================================================================================");
//            Console.WriteLine($"{"Poz.",-4} | {"G-Pos",-5} | {"Mag.",-4} | {"Stan",-6} | {"Waluta",-6} | {"Info",-15} | {"Metoda",-6} | {"Sprzedawca",-20} | {"Cena",-10} | {"Dostawa",-9} | {"Razem",-10} | URL");
//            Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------");
//            foreach (var item in debugMainList) PrintDebugRow(item, item.ListPos);

//            if (debugAdditionalList.Any())
//            {
//                Console.WriteLine("\n--- DODATKOWE ---");
//                foreach (var item in debugAdditionalList) PrintDebugRow(item, "-");
//            }

//            return finalPriceHistory;
//        }

//        #region Helper Methods
//        private void PrintDebugRow(dynamic item, string posLabel)
//        {
//            string infoCode = item.Info;
//            if (infoCode.Length > 15) infoCode = infoCode.Substring(0, 12) + "...";
//            string seller = item.Seller;
//            if (seller.Length > 20) seller = seller.Substring(0, 17) + "...";

//            Console.Write($"{posLabel,-4} | {item.GPos,-5} | {item.Stock,-4} | ");

//            if (item.Cond.Contains("UŻYW") || item.Cond.Contains("OUTLET")) Console.ForegroundColor = ConsoleColor.Red;
//            else Console.ForegroundColor = ConsoleColor.Green;
//            Console.Write($"{item.Cond,-6}");
//            Console.ResetColor();
//            Console.Write(" | ");

//            if (item.Curr != "PLN") Console.ForegroundColor = ConsoleColor.Magenta;
//            else Console.ForegroundColor = ConsoleColor.Gray;
//            Console.Write($"{item.Curr,-6}");
//            Console.ResetColor();

//            Console.WriteLine($" | {infoCode,-15} | {item.Method,-6} | {seller,-20} | {item.Price,-10} | {item.Del,-9} | {item.Total,-10} | {item.Url}");
//        }

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
//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result)) return result;
//            return 0;
//        }

//        private decimal ParseDeliveryPrice(string? deliveryText)
//        {
//            if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa")) return 0;
//            return ParsePrice(deliveryText);
//        }
//        #endregion
//    }

//    public static class GoogleShoppingApiParser
//    {
//        public static List<TempOffer> ParseWrga(string rawResponse)
//        {
//            var offers = new List<TempOffer>();
//            if (string.IsNullOrWhiteSpace(rawResponse)) return offers;
//            try
//            {
//                string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//                string decodedContent = Regex.Unescape(cleaned);
//                decodedContent = WebUtility.HtmlDecode(decodedContent);
//                var blockRegex = new Regex(@"<div[^>]*class=""[^""]*tF2Cxc[^""]*""[^>]*>([\s\S]*?)(?=<div[^>]*class=""[^""]*tF2Cxc|$)", RegexOptions.IgnoreCase);
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
//                    string condition = "NOWY";
//                    string blockLower = block.ToLower();
//                    var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                    if (usedKeywords.Any(k => blockLower.Contains(k))) condition = "UŻYWANY";
//                    string currency = "PLN";
//                    if ((blockLower.Contains("eur") || blockLower.Contains("€")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "EUR";
//                    else if ((blockLower.Contains("usd") || blockLower.Contains("$")) && !blockLower.Contains("pln") && !blockLower.Contains("zł")) currency = "USD";
//                    priceVal = ExtractRichSnippetPrice(block);
//                    if (priceVal > 0) { deliveryVal = ExtractDeliveryFromRichSnippet(block); badge = "RICH_SNIPPET"; }
//                    else { string cleanText = StripHtml(block); var analysisResult = AnalyzePricesInBlock(cleanText); priceVal = analysisResult.Price; deliveryVal = analysisResult.Delivery; badge = "TEXT_ANALYSIS"; }
//                    if (priceVal > 0)
//                    {
//                        offers.Add(new TempOffer(seller, priceVal.ToString("F2"), url, deliveryVal > 0 ? deliveryVal.ToString("F2") : "0", true, badge, offerIndex, "WRGA", null, null, condition, currency));
//                    }
//                }
//            }
//            catch { }
//            return offers;
//        }

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
//            catch (JsonException) { return new List<TempOffer>(); }
//        }

//        private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOffer> allOffers)
//        {
//            if (node.ValueKind == JsonValueKind.Array)
//            {
//                if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//                {
//                    foreach (JsonElement potentialOffer in node.EnumerateArray())
//                    {
//                        TempOffer? offer = ParseSingleOffer(root, potentialOffer);
//                        if (offer != null && !allOffers.Any(o => o.Url == offer.Url)) allOffers.Add(offer);
//                    }
//                }
//            }
//            if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) FindAndParseAllOffers(root, element, allOffers); }
//            else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) FindAndParseAllOffers(root, property.Value, allOffers); }
//        }

//        private static bool IsPotentialSingleOffer(JsonElement node)
//        {
//            if (node.ValueKind != JsonValueKind.Array) return false;
//            int arrayChildren = 0; int primitiveChildren = 0;
//            foreach (var child in node.EnumerateArray()) { if (child.ValueKind == JsonValueKind.Array) arrayChildren++; else if (child.ValueKind == JsonValueKind.String || child.ValueKind == JsonValueKind.Number || child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False) primitiveChildren++; }
//            if (arrayChildren > 1 && primitiveChildren == 0) return false;
//            JsonElement offerData = node;
//            if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array) offerData = node[0];
//            if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;
//            var flatStrings = Flatten(offerData).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//            if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google") && !s.Contains("gstatic"))) return true;
//            return false;
//        }

//        private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//        {
//            JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array) ? offerContainer[0] : offerContainer;
//            if (offerData.ValueKind != JsonValueKind.Array) return null;
//            try
//            {
//                var allNodes = Flatten(offerData).ToList();
//                var flatStrings = allNodes.Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
//                string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//                string? seller = null;
//                if (!string.IsNullOrEmpty(url)) seller = GetDomainName(url);
//                if (string.IsNullOrEmpty(seller) || seller == "Nieznany")
//                {
//                    var blacklist = new[] { "PLN", "zł", "EUR", "USD", "netto", "brutto" };
//                    foreach (var s in flatStrings) { if (!s.StartsWith("http") && s.Length > 2 && !blacklist.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)) && !Regex.IsMatch(s, @"\d")) { seller = s; break; } }
//                }
//                if (seller == null && url != null) seller = GetDomainName(url);
//                string condition = "NOWY";
//                var usedKeywords = new[] { "pre-owned", "używany", "outlet", "renewed", "refurbished", "odnowiony", "powystawowy" };
//                foreach (var text in flatStrings)
//                {
//                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3) continue;
//                    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
//                    string lowerText = text.ToLower();
//                    if (lowerText.Contains("nie używany") || lowerText.Contains("nieużywany")) continue;
//                    if (lowerText.Contains("nowy") && !lowerText.Contains("jak nowy")) continue;
//                    foreach (var keyword in usedKeywords) { if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) { if (keyword == "używany" && (lowerText.Contains("fabrycznie nowy") || lowerText.Contains("produkt nowy"))) continue; condition = "UŻYWANY"; goto ConditionFound; } }
//                }
//            ConditionFound:;
//                bool isInStock = true;
//                bool hasPositiveStockText = flatStrings.Any(s => s.Contains("W magazynie", StringComparison.OrdinalIgnoreCase) || s.Contains("Dostępny", StringComparison.OrdinalIgnoreCase) || s.Equals("In stock", StringComparison.OrdinalIgnoreCase));
//                if (hasPositiveStockText) isInStock = true;
//                else { var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "wyprzedany", "chwilowy brak" }; if (flatStrings.Any(text => text.Length < 50 && outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))) isInStock = false; }
//                List<(decimal Amount, string Currency)> structuralPrices = new();
//                for (int i = 1; i < allNodes.Count; i++)
//                {
//                    var current = allNodes[i];
//                    if (current.ValueKind == JsonValueKind.String)
//                    {
//                        string currCode = current.GetString()?.ToUpper() ?? "";
//                        if (currCode == "PLN" || currCode == "EUR" || currCode == "USD" || currCode == "GBP")
//                        {
//                            long micros = 0; bool foundPrice = false;
//                            var prev = allNodes[i - 1];
//                            if (prev.ValueKind == JsonValueKind.Number) { micros = prev.GetInt64(); foundPrice = true; }
//                            else if (i >= 2) { var prevPrev = allNodes[i - 2]; if (prevPrev.ValueKind == JsonValueKind.Number) { micros = prevPrev.GetInt64(); foundPrice = true; } }
//                            if (foundPrice && micros >= 1000000) { structuralPrices.Add((micros / 1000000m, currCode)); if (currCode == "PLN") break; }
//                        }
//                    }
//                }
//                bool hasTextualForeignEvidence = false; string foreignTextCurrency = "";
//                var foreignRegex = new Regex(@"[\(\s](€|EUR|\$|USD)\s*(\d+[.,]?\d*)", RegexOptions.IgnoreCase);
//                foreach (var s in flatStrings) { Match m = foreignRegex.Match(s); if (m.Success) { hasTextualForeignEvidence = true; string symbol = m.Groups[1].Value.ToUpper(); foreignTextCurrency = (symbol.Contains("EUR") || symbol.Contains("€")) ? "EUR" : "USD"; break; } }
//                string? finalPrice = null; string finalCurrency = "PLN";
//                var plnNode = structuralPrices.FirstOrDefault(x => x.Currency == "PLN");
//                if (plnNode != default) { finalPrice = plnNode.Amount.ToString("F2", CultureInfo.InvariantCulture); if (hasTextualForeignEvidence) finalCurrency = foreignTextCurrency; else finalCurrency = "PLN"; }
//                else if (structuralPrices.Any(x => x.Currency != "PLN")) { var foreign = structuralPrices.First(x => x.Currency != "PLN"); finalPrice = foreign.Amount.ToString("F2", CultureInfo.InvariantCulture); finalCurrency = foreign.Currency; }
//                else return null;
//                string? delivery = null;
//                if (flatStrings.Any(s => s.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || s.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) || s.Contains("Bezpłatnie", StringComparison.OrdinalIgnoreCase) || s.Contains("Free delivery", StringComparison.OrdinalIgnoreCase))) delivery = "Bezpłatna";
//                else
//                {
//                    var plusRegex = new Regex(@"^\+\s*(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                    foreach (var s in flatStrings) { var match = plusRegex.Match(s.Trim()); if (match.Success) { delivery = ParsePriceDecimal(match.Groups[1].Value).ToString("F2"); break; } }
//                    if (delivery == null)
//                    {
//                        var deliveryTextRegex = new Regex(@"(?:dostawa|wysyłka|delivery|shipping)(?:[^0-9]{0,30})(\d+[.,]\d{2})\s*(?:PLN|zł)|za\s+(\d+[.,]\d{2})\s*(?:PLN|zł)", RegexOptions.IgnoreCase);
//                        foreach (var s in flatStrings)
//                        {
//                            if (!s.ToLower().Contains("dostaw") && !s.ToLower().Contains("wysyłk") && !s.ToLower().Contains("delivery") && !s.ToLower().Contains(" za ")) continue;
//                            var match = deliveryTextRegex.Match(s);
//                            if (match.Success) { string priceStr = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value; decimal delPrice = ParsePriceDecimal(priceStr); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } }
//                        }
//                    }
//                    if (delivery == null) { for (int i = 0; i < allNodes.Count - 1; i++) { var node = allNodes[i]; if (node.ValueKind == JsonValueKind.Number) { try { long val = node.GetInt64(); if (val == 110720 && i > 0) { var prevNode = allNodes[i - 1]; if (prevNode.ValueKind == JsonValueKind.String) { string delText = prevNode.GetString()!; var priceMatch = Regex.Match(delText, @"(\d+[.,]\d{2})"); if (priceMatch.Success) { decimal delPrice = ParsePriceDecimal(priceMatch.Groups[1].Value); if (delPrice > 0 && delPrice < 500) { delivery = delPrice.ToString("F2"); break; } } } } } catch { } } } }
//                }
//                string? badge = ExtractBadgeStrict(offerData);
//                if (string.IsNullOrEmpty(badge)) { string[] possibleBadges = { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }; badge = flatStrings.FirstOrDefault(s => possibleBadges.Any(pb => string.Equals(s, pb, StringComparison.OrdinalIgnoreCase))); }
//                if (!string.IsNullOrWhiteSpace(seller) && finalPrice != null && url != null) return new TempOffer(seller, finalPrice, url, delivery, isInStock, badge, 0, "OAPV", null, null, condition, finalCurrency);
//            }
//            catch { }
//            return null;
//        }

//        public static string? ExtractProductTitle(string rawResponse)
//        {
//            try { string cleaned = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse; using JsonDocument doc = JsonDocument.Parse(cleaned); if (doc.RootElement.TryGetProperty("ProductDetailsResult", out JsonElement pd) && pd.GetArrayLength() > 0) return pd[0].GetString(); } catch { }
//            return null;
//        }

//        private static string GetDomainName(string url) { try { var host = new Uri(url).Host.ToLower().Replace("www.", ""); return char.ToUpper(host[0]) + host.Substring(1); } catch { return "Nieznany"; } }
//        private static bool IsGoogleLink(string url) { return url.Contains(".google.") || url.Contains("gstatic.") || url.Contains("/search?") || url.Contains("youtube.") || url.Contains("googleusercontent") || url.Contains("translate.google"); }
//        private static string? ExtractBadgeStrict(JsonElement offerData) { try { foreach (var element in offerData.EnumerateArray()) { if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0) { var inner = element[0]; if (inner.ValueKind == JsonValueKind.Array && inner.GetArrayLength() > 0) { var potentialBadgeNode = inner[0]; if (potentialBadgeNode.ValueKind == JsonValueKind.Array && potentialBadgeNode.GetArrayLength() >= 1) { if (potentialBadgeNode[0].ValueKind == JsonValueKind.String) { string text = potentialBadgeNode[0].GetString()!; if (new[] { "Najlepsza cena", "Najpopularniejsze", "Wybór", "Niska cena" }.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))) return text; } } } } } } catch { } return null; }
//        private static IEnumerable<JsonElement> Flatten(JsonElement node, int maxDepth = 20, int currentDepth = 0) { if (currentDepth > maxDepth) yield break; yield return node; if (node.ValueKind == JsonValueKind.Array) { foreach (var element in node.EnumerateArray()) { foreach (var child in Flatten(element, maxDepth, currentDepth + 1)) yield return child; } } else if (node.ValueKind == JsonValueKind.Object) { foreach (var property in node.EnumerateObject()) { foreach (var child in Flatten(property.Value, maxDepth, currentDepth + 1)) yield return child; } } }
//        private static decimal ExtractRichSnippetPrice(string htmlBlock) { var richPriceRegex = new Regex(@"<span[^>]*class=""[^""]*\bLI0TWe\b[^""]*""[^>]*>\s*([\d,\.\s]+(?:zł|PLN|EUR|USD)?)\s*</span>", RegexOptions.IgnoreCase); var match = richPriceRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
//        private static decimal ExtractDeliveryFromRichSnippet(string htmlBlock) { var deliveryRegex = new Regex(@"(\d+[,\.]\d{2})\s*(?:PLN|zł)?\s*(?:delivery|dostawa|wysyłka)", RegexOptions.IgnoreCase); var match = deliveryRegex.Match(htmlBlock); if (match.Success) return ParsePriceDecimal(match.Groups[1].Value); return 0; }
//        private static (decimal Price, decimal Delivery) AnalyzePricesInBlock(string text) { var priceRegex = new Regex(@"(?:cena|price|za)?\s*[:;-]?\s*(?<!\d)(\d{1,3}(?:[\s\.]\d{3})*[,\.]\d{2})(?!\d)\s*(?:zł|PLN|EUR|USD)?", RegexOptions.IgnoreCase); var matches = priceRegex.Matches(text); decimal bestPrice = 0; decimal bestDelivery = 0; foreach (Match m in matches) { if (!decimal.TryParse(m.Groups[1].Value.Replace(" ", "").Replace(".", ","), NumberStyles.Any, new CultureInfo("pl-PL"), out decimal val)) continue; if (val < 1.0m) continue; int contextStart = Math.Max(0, m.Index - 30); int contextLen = Math.Min(text.Length - contextStart, (m.Index + m.Length - contextStart) + 20); string snippetForLog = text.Substring(contextStart, contextLen).ToLower(); if (Regex.IsMatch(snippetForLog, @"(dostawa|wysyłka|delivery|\+)")) { if (bestDelivery == 0) bestDelivery = val; } else if (bestPrice == 0) { bestPrice = val; } } return (bestPrice, bestDelivery); }
//        private static string StripHtml(string html) { if (string.IsNullOrEmpty(html)) return ""; string s = html.Replace("<br>", " ").Replace("</div>", " ").Replace("</span>", " ").Replace("</b>", " "); s = Regex.Replace(s, "<.*?>", " "); s = WebUtility.HtmlDecode(s); s = Regex.Replace(s, @"\s+", " ").Trim(); return s; }
//        private static decimal ParsePriceDecimal(string priceStr) { if (string.IsNullOrEmpty(priceStr)) return 0; string clean = Regex.Replace(priceStr, @"[^\d,.]", ""); if (clean.Contains(",") && clean.Contains(".")) { if (clean.LastIndexOf(',') > clean.LastIndexOf('.')) clean = clean.Replace(".", "").Replace(",", "."); else clean = clean.Replace(",", ""); } else if (clean.Contains(",")) { clean = clean.Replace(",", "."); } else if (clean.Count(c => c == '.') > 1) { int lastDot = clean.LastIndexOf('.'); clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot); } if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal res)) return res; return 0; }
//    }
//}












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
    // --- NOWOŚĆ: GLOBALNY MAGAZYN CIASTEK (ODDZIELONY OD SCRAPERA) ---
    public static class GlobalCookieWarehouse
    {
        // Konfiguracja magazynu
        private const int MAX_COOKIES_IN_QUEUE = 200; // Maksymalna pojemność magazynu
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";

        // Kolejka blokująca - wątki HTTP będą tu czekać, jeśli jest pusta
        private static readonly BlockingCollection<CookieContainer> _cookieQueue = new(MAX_COOKIES_IN_QUEUE);

        private static CancellationTokenSource _generatorCts = new();
        private static bool _isStarted = false;
        private static readonly object _lock = new();

        // Informacja dla logów/debugowania
        public static int AvailableCookies => _cookieQueue.Count;

        /// <summary>
        /// Uruchamia określoną liczbę generatorów Selenium w tle.
        /// </summary>
        public static void StartGenerators(int generatorCount)
        {
            lock (_lock)
            {
                if (_isStarted) return; // Już działają

                _generatorCts = new CancellationTokenSource();
                _isStarted = true;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[WAREHOUSE] Uruchamiam {generatorCount} niezależnych generatorów ciastek...");
                Console.ResetColor();

                for (int i = 0; i < generatorCount; i++)
                {
                    int botId = i + 1;
                    Task.Run(() => RunGeneratorLoop(botId, _generatorCts.Token));
                }
            }
        }

        /// <summary>
        /// Zatrzymuje generatory i czyści magazyn (np. przy StopScraping).
        /// </summary>
        public static void StopAndClear()
        {
            lock (_lock)
            {
                if (!_isStarted) return;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[WAREHOUSE] Zatrzymywanie generatorów i czyszczenie magazynu...");
                Console.ResetColor();

                _generatorCts.Cancel();

                // Opróżnianie kolejki
                while (_cookieQueue.TryTake(out _)) { }

                _isStarted = false;
            }
        }

        /// <summary>
        /// Metoda dla Scrapera HTTP - pobiera ciastko. Blokuje wątek, jeśli magazyn jest pusty.
        /// </summary>
        public static CookieContainer TakeCookie(CancellationToken token)
        {
            try
            {
                // To wywołanie zablokuje scrapera, dopóki generator nie wrzuci ciastka
                return _cookieQueue.Take(token);
            }
            catch (OperationCanceledException)
            {
                throw; // Przekaż anulowanie wyżej
            }
            catch (InvalidOperationException)
            {
                // Kolejka została zamknięta (CompleteAdding)
                throw new Exception("Magazyn ciastek został zamknięty.");
            }
        }

        // --- PĘTLA GENERATORA (SELENIUM) ---
        private static void RunGeneratorLoop(int id, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Jeśli magazyn jest pełny, śpij dłużej, żeby nie męczyć CPU
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

            //urwany łeb
            options.AddArgument("--headless=new");
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
                    // Ustawienie timeoutów, żeby nie wisiało w nieskończoność
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(40);

                    string targetUrl = $"https://www.google.com/search?q={randomQuery}&tbm=shop";
                    driver.Navigate().GoToUrl(targetUrl);

                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                    // Próba kliknięcia zgody
                    try
                    {
                        var consentButton = wait.Until(d => d.FindElement(By.XPath("//div[contains(@class, 'QS5gu') and (contains(., 'Zaakceptuj') or contains(., 'Odrzuć'))]")));
                        Thread.Sleep(new Random().Next(200, 500));
                        consentButton.Click();
                    }
                    catch { }

                    Thread.Sleep(500);

                    // Symulacja interakcji (kliknięcie w produkt)
                    try
                    {
                        var productCard = wait.Until(d => d.FindElement(By.CssSelector("div.njFjte[role='button']")));
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center', behavior: 'smooth'});", productCard);
                        Thread.Sleep(500);
                        productCard.Click();
                        Thread.Sleep(2500);
                    }
                    catch { }

                    // Pobranie ciastek
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

    // --- SYSTEM BATCHINGU (MAGAZYNIER WYNIKÓW) ---
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
            await FlushQueueAsync(); // Zapisz resztki
            Console.WriteLine("[BATCH] Zatrzymano procesor.");
        }

        private static async Task FlushQueueAsync()
        {
            if (_resultsQueue.IsEmpty) return;

            var batch = new List<CoOfrPriceHistoryClass>();
            // Pobieramy max 500 na raz
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

    // --- REKORD DANYCH TYMCZASOWYCH ---
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

    // --- KLASA WORKERA HTTP (Konsument ciastek) ---
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

        // Teraz pobieramy ciastka z globalnego magazynu (Static class)
        private void LoadNewIdentityFromWarehouse()
        {
            try
            {
                Console.WriteLine("[SCRAPER] Pobieram nowe ciastka z globalnego magazynu...");

                // UWAGA: To zablokuje scrapera, jeśli magazyn jest pusty!
                // Dzięki temu wątek HTTP nie będzie "mielił" błędami, tylko grzecznie poczeka na Selenium.
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
                // Jeśli nie udało się pobrać, można spróbować poczekać i ponowić, 
                // ale w tym modelu po prostu rzucamy wyjątek, żeby worker zrestartował zadanie.
                throw;
            }
        }

        public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
        {
            var finalPriceHistory = new List<CoOfrPriceHistoryClass>();
            var allFoundOffers = new List<TempOffer>();

            // Sprawdzamy, czy w ogóle mamy ciastka w magazynie (lub czy generatory działają)
            // Jeśli magazyn pusty, to LoadNewIdentity i tak zablokuje, ale tu można dodać logikę wczesnego wyjścia
            // jeśli np. GlobalCookieWarehouse.StopAndClear() zostało wywołane.

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

                        // Wykrywanie blokady / pustego response
                        if (rawResponse.Length < 100 && rawResponse.Contains("ProductDetailsResult\":[]"))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[BLOKADA] Pusta odpowiedź! Wymieniam tożsamość...");
                            Console.ResetColor();

                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine($"[STATS] Ciastko spalone po {_requestsOnCurrentIdentity} requestach.");
                            Console.ResetColor();

                            LoadNewIdentityFromWarehouse();
                            continue; // Retry z nowym ciastkiem
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

            // LOGIKA WRGA (bez zmian)
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

            // --- PRZETWARZANIE WYNIKÓW I DEBUG ---
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

    // --- PARSERY (Bez zmian) ---
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