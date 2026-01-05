//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOffer(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

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

//        string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
//            return finalPriceHistory;
//        }

//        string urlTemplate;
//        if (!string.IsNullOrEmpty(coOfr.GoogleGid))
//        {

//            Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
//        }
//        else
//        {

//            Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
//            urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
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

//        var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);
//        var finalOffersToProcess = new List<(TempOffer offer, int count)>();

//        foreach (var group in groupedBySeller)
//        {
//            int storeOfferCount = group.Count();
//            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
//            finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
//        }

//        int positionCounter = 1;

//        foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
//        {
//            finalPriceHistory.Add(new CoOfrPriceHistoryClass
//            {
//                GoogleStoreName = item.offer.Seller,
//                GooglePrice = ParsePrice(item.offer.Price),
//                GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),
//                GooglePosition = (positionCounter++).ToString(),
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
//                                ? offerContainer[0]
//                                : offerContainer;

//        if (offerData.ValueKind != JsonValueKind.Array) return null;

//        try
//        {
//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony" };
//            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                return null;
//            }

//            bool isInStock = true;
//            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
//            if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                isInStock = false;
//            }

//            string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//            string? price = flatStrings.FirstOrDefault(s => PricePattern.IsMatch(s) && !s.Trim().StartsWith("+"));

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
//                   .FirstOrDefault(item => item.ValueKind == JsonValueKind.Array
//                                         && item.GetArrayLength() > 1
//                                         && item[0].ValueKind == JsonValueKind.String
//                                         && item[1].ValueKind == JsonValueKind.String
//                                         && item[1].GetString()!.All(char.IsDigit));
//                if (sellerNode.ValueKind != JsonValueKind.Undefined)
//                {
//                    var potentialSeller = sellerNode[0].GetString()!;
//                    if (!int.TryParse(potentialSeller, out _))
//                    {
//                        seller = potentialSeller;
//                    }
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

//            string? delivery = null;
//            string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && PricePattern.IsMatch(s))
//                                   ?? flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) || s.Contains("delivery", StringComparison.OrdinalIgnoreCase));

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

//            if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
//            {
//                return new TempOffer(seller, price, url, delivery, isInStock);
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


using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// 1. Rekord pomocniczy
public record TempOffer(string Seller, string Price, string Url, string? Delivery, bool IsInStock, string? Badge, int OriginalIndex);

public class GoogleMainPriceScraper
{
    private static readonly HttpClient _httpClient;

    static GoogleMainPriceScraper()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
    }

    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
    {
        var finalPriceHistory = new List<CoOfrPriceHistoryClass>();

        // Krok 1: Wyodrębnij ID katalogu (CID)
        string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

        if (string.IsNullOrEmpty(catalogId))
        {
            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
            return finalPriceHistory;
        }



        //string urlTemplate;
        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
        //{
        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
        //    // Dodano gl:4, usunięto fs oraz isp
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
        //}
        //else
        //{
        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
        //    // Dodano gl:4, usunięto fs oraz isp
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
        //}

        //nowy z catalogid

        //string urlTemplate;
        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
        //{
        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
        //    // Dodano gl:4, usunięto fs oraz isp
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
        //}
        //else
        //{
        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
        //    // Dodano gl:4, usunięto fs oraz isp
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,sori:{{0}},mno:10,query:1,gl:4,pvt:hg,_fmt:jspb";
        //}







        //stary niepelny schemat

        //string urlTemplate;
        //if (!string.IsNullOrEmpty(coOfr.GoogleGid))
        //{
        //    Console.WriteLine($"Używam GID: {coOfr.GoogleGid} dla CID: {catalogId}");
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{coOfr.GoogleGid},catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
        //}
        //else
        //{
        //    Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
        //    urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
        //}



        //bez isp

        string urlTemplate;
        if (!string.IsNullOrEmpty(coOfr.GoogleGid))
        {
            Console.WriteLine($"metoda bez gid dla CID: {catalogId}");
            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
        }
        else
        {
            Console.WriteLine($"GID nie znaleziony dla CID: {catalogId}. Używam zapytania bez gpcid.");
            urlTemplate = $"https://www.google.com/async/oapv?udm=3&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";
        }

        var allFoundOffers = new List<TempOffer>();
        int startIndex = 0;
        const int pageSize = 10;
        int lastFetchCount;
        const int maxRetries = 3;

        do
        {
            string currentUrl = string.Format(urlTemplate, startIndex);
            List<TempOffer> newOffers = new List<TempOffer>();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);
                    newOffers = GoogleShoppingApiParser.Parse(rawResponse);

                    if (newOffers.Any() || rawResponse.Length < 100) break;
                    if (attempt < maxRetries) await Task.Delay(2000);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
                    }
                    else
                    {
                        await Task.Delay(2500);
                    }
                }
            }
            lastFetchCount = newOffers.Count;
            allFoundOffers.AddRange(newOffers);
            startIndex += pageSize;
            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(500, 800));
        } while (lastFetchCount == pageSize);

        // Krok 3: Nadanie oryginalnych indeksów
        var indexedOffers = allFoundOffers.Select((offer, index) => offer with { OriginalIndex = index + 1 }).ToList();

        var groupedBySeller = indexedOffers.GroupBy(o => o.Seller);
        var finalOffersToProcess = new List<(TempOffer offer, int count)>();

        foreach (var group in groupedBySeller)
        {
            int storeOfferCount = group.Count();
            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
            finalOffersToProcess.Add((cheapestOffer, storeOfferCount));
        }

        // Krok 4: Finalne mapowanie
        foreach (var item in finalOffersToProcess.OrderBy(i => ParsePrice(i.offer.Price)))
        {
            // =================================================================
            // LOGIKA ROZRÓŻNIANIA ODZNAK (BPG vs HPG)
            // =================================================================
            string? isBiddingValue = null;

            if (!string.IsNullOrEmpty(item.offer.Badge))
            {
                string badgeLower = item.offer.Badge.ToLower();

                if (badgeLower.Contains("cena")) // Najlepsza cena, Niska cena
                {
                    isBiddingValue = "bpg"; // Best Price Google
                }
                else if (badgeLower.Contains("popularn")) // Najpopularniejsze
                {
                    isBiddingValue = "hpg"; // High Popularity Google (lub inne rozwinięcie skrótu)
                }
            }
            // =================================================================

            finalPriceHistory.Add(new CoOfrPriceHistoryClass
            {
                GoogleStoreName = item.offer.Seller,
                GooglePrice = ParsePrice(item.offer.Price),
                GooglePriceWithDelivery = ParsePrice(item.offer.Price) + ParseDeliveryPrice(item.offer.Delivery),

                // Oryginalna pozycja z Google
                GooglePosition = item.offer.OriginalIndex.ToString(),

                // Kod odznaki (bpg / hpg / null)
                IsBidding = isBiddingValue,

                GoogleInStock = item.offer.IsInStock,
                GoogleOfferPerStoreCount = item.count
            });
        }

        return finalPriceHistory;
    }

    #region Helper Methods
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
        if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa"))
        {
            return 0;
        }
        return ParsePrice(deliveryText);
    }
    #endregion
}

public static class GoogleShoppingApiParser
{
    private static readonly Regex PricePattern = new(@"\d[\d\s,.]*\s*(?:PLN|zł)", RegexOptions.Compiled);

    public static List<TempOffer> Parse(string rawResponse)
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
            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
            {
                foreach (JsonElement potentialOffer in node.EnumerateArray())
                {
                    TempOffer? offer = ParseSingleOffer(root, potentialOffer);
                    if (offer != null)
                    {
                        if (!allOffers.Any(o => o.Url == offer.Url))
                        {
                            allOffers.Add(offer);
                        }
                    }
                }
            }
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
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
        JsonElement offerData = node;
        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
        {
            offerData = node[0];
        }

        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

        var flatStrings = Flatten(offerData)
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();

        if (flatStrings.Any(s => s.StartsWith("https://") && !s.Contains("google"))) return true;

        return false;
    }

    private static TempOffer? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
    {
        JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array)
                                        ? offerContainer[0]
                                        : offerContainer;

        if (offerData.ValueKind != JsonValueKind.Array) return null;

        try
        {
            var flatStrings = Flatten(offerData)
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();

            // 1. Filtrowanie używanych
            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony" };
            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            {
                return null;
            }

            // 2. Dostępność
            bool isInStock = true;
            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny" };
            if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            {
                isInStock = false;
            }

            // 3. Podstawowe dane
            string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
            string? price = flatStrings.FirstOrDefault(s => PricePattern.IsMatch(s) && !s.Trim().StartsWith("+"));

            // 4. Sprzedawca
            string? seller = null;
            var offerElements = offerData.EnumerateArray().ToList();
            for (int i = 0; i < offerElements.Count - 1; i++)
            {
                if (offerElements[i].ValueKind == JsonValueKind.Number &&
                    offerElements[i + 1].ValueKind == JsonValueKind.String)
                {
                    string potentialSeller = offerElements[i + 1].GetString()!;
                    if (!potentialSeller.StartsWith("http") && !PricePattern.IsMatch(potentialSeller) && potentialSeller.Length > 2)
                    {
                        seller = potentialSeller;
                        break;
                    }
                }
            }

            if (seller == null)
            {
                var sellerNode = offerData.EnumerateArray()
                    .FirstOrDefault(item => item.ValueKind == JsonValueKind.Array
                                            && item.GetArrayLength() > 1
                                            && item[0].ValueKind == JsonValueKind.String
                                            && item[1].ValueKind == JsonValueKind.String
                                            && item[1].GetString()!.All(char.IsDigit));
                if (sellerNode.ValueKind != JsonValueKind.Undefined)
                {
                    var potentialSeller = sellerNode[0].GetString()!;
                    if (!int.TryParse(potentialSeller, out _)) seller = potentialSeller;
                }
            }

            if (seller == null && url != null)
            {
                var docIdMatch = Regex.Match(url, @"shopping_docid(?:%253D|=)(\d+)|docid(?:%3D|=)(\d+)");
                if (docIdMatch.Success)
                {
                    string offerId = docIdMatch.Groups[1].Success ? docIdMatch.Groups[1].Value : docIdMatch.Groups[2].Value;
                    var sellerInfoNodes = FindNodesById(root, offerId);
                    foreach (var sellerInfoNode in sellerInfoNodes)
                    {
                        if (sellerInfoNode.ValueKind == JsonValueKind.Array && sellerInfoNode.GetArrayLength() > 1 && sellerInfoNode[1].ValueKind == JsonValueKind.Array)
                        {
                            var potentialSellerName = sellerInfoNode[1].EnumerateArray()
                                .FirstOrDefault(e => e.ValueKind == JsonValueKind.String);

                            if (potentialSellerName.ValueKind == JsonValueKind.String)
                            {
                                seller = potentialSellerName.GetString();
                                break;
                            }
                        }
                    }
                }
            }

            // 5. Dostawa
            string? delivery = null;
            string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && PricePattern.IsMatch(s))
                                      ?? flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) || s.Contains("delivery", StringComparison.OrdinalIgnoreCase));

            if (rawDeliveryText != null)
            {
                if (rawDeliveryText.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Free", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Darmowa", StringComparison.OrdinalIgnoreCase))
                    delivery = "Bezpłatna";
                else
                {
                    Match priceMatch = PricePattern.Match(rawDeliveryText);
                    if (priceMatch.Success) delivery = priceMatch.Value.Trim();
                }
            }

            // 6. Odznaka (Badge)
            string? badge = ExtractBadge(offerData);

            if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
            {
                // Przekazujemy 0 jako tymczasowy OriginalIndex
                return new TempOffer(seller, price, url, delivery, isInStock, badge, 0);
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractBadge(JsonElement offerData)
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

                                // STRICT MATCH: Tylko dokładne frazy (ignorując wielkość liter)
                                bool isValid =
                                    string.Equals(text, "Najlepsza cena", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(text, "Niska cena", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(text, "Najpopularniejsze", StringComparison.OrdinalIgnoreCase);

                                if (isValid)
                                {
                                    return text;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static List<JsonElement> FindNodesById(JsonElement node, string id)
    {
        var results = new List<JsonElement>();
        var stack = new Stack<JsonElement>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Array)
            {
                if (current.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == id))
                {
                    results.Add(current);
                }
                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
            }
            else if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
            }
        }
        return results;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node)
    {
        var stack = new Stack<JsonElement>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
            }
            else if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
            }
            else
            {
                yield return current;
            }
        }
    }
}