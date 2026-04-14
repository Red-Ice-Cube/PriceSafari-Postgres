using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace PriceSafari.Services.ScheduleService
{
    public class CopyXmlPricesService
    {
        private readonly PriceSafariContext _context;

        public CopyXmlPricesService(PriceSafariContext context)
        {
            _context = context;
        }

        public class CopyResult
        {
            public List<PriceHistoryClass> AppendedHistories { get; set; } = new();
            public HashSet<int> ProductIdsWithAppended { get; set; } = new();
        }

        // <summary>

        // Dokleja ceny z XML dla produktów które NIE mają jeszcze naszej oferty w Google

        // w przekazanym zbiorze priceHistoriesBag.

        // </summary>

        public async Task<CopyResult> AppendPricesForStoreAsync(
            StoreClass store,
            List<ProductClass> products,
            ConcurrentBag<PriceHistoryClass> priceHistoriesBag,
            ScrapHistoryClass scrapHistory,
            string canonicalStoreName)
        {
            var result = new CopyResult();

            if (!store.CopyXMLPrices) return result;

            var mapping = await _context.CopyXmlPriceMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.StoreId == store.StoreId);

            if (mapping == null || string.IsNullOrEmpty(mapping.KeyXPath)
                || string.IsNullOrEmpty(mapping.PriceXPath)
                || string.IsNullOrEmpty(mapping.ProductNodeXPath))
            {
                Console.WriteLine($"[CopyXmlPrices] Sklep {store.StoreId}: brak pełnego mapowania — pomijam.");
                return result;
            }

            if (string.IsNullOrEmpty(store.ProductMapXmlUrlGoogle))
            {
                Console.WriteLine($"[CopyXmlPrices] Sklep {store.StoreId}: brak ProductMapXmlUrlGoogle.");
                return result;
            }

            var storeNameLower = canonicalStoreName.ToLower().Trim();
            var productsWithGoogleOffer = priceHistoriesBag
                .Where(ph => ph.IsGoogle && ph.StoreName != null
                             && ph.StoreName.ToLower().Trim() == storeNameLower)
                .Select(ph => ph.ProductId)
                .ToHashSet();

            var productsWithoutGoogleOffer = products
                .Where(p => p.IsScrapable && !productsWithGoogleOffer.Contains(p.ProductId))
                .ToList();

            if (!productsWithoutGoogleOffer.Any())
            {
                Console.WriteLine($"[CopyXmlPrices] Sklep {store.StoreId}: wszystkie produkty mają ofertę w Google.");
                return result;
            }

            string xmlContent;
            try
            {
                xmlContent = await LoadXmlAsync(store.ProductMapXmlUrlGoogle);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CopyXmlPrices] Błąd ładowania XML: {ex.Message}");
                return result;
            }

            var xmlDoc = new XmlDocument();
            try { xmlDoc.LoadXml(xmlContent); }
            catch (Exception ex)
            {
                Console.WriteLine($"[CopyXmlPrices] Błąd parsowania XML: {ex.Message}");
                return result;
            }

            var index = BuildXmlIndex(xmlDoc, mapping);
            if (index.Count == 0)
            {
                Console.WriteLine($"[CopyXmlPrices] Sklep {store.StoreId}: pusty index z XML.");
                return result;
            }

            var markerLower = (mapping.InStockMarkerValue ?? "").Trim().ToLower();

            int appended = 0;
            foreach (var product in productsWithoutGoogleOffer)
            {
                var key = mapping.KeyField == CopyXmlKeyField.Ean
                    ? (product.Ean ?? "").Trim()
                    : (product.ExternalId?.ToString() ?? "");

                if (string.IsNullOrEmpty(key)) continue;
                if (!index.TryGetValue(key, out var row)) continue;
                if (row.Price == null || row.Price <= 0) continue;

                bool inStock = true;
                if (mapping.InStockXPath != null && !string.IsNullOrEmpty(markerLower))
                {
                    inStock = (row.InStockRaw ?? "").Trim().ToLower() == markerLower;
                }

                decimal? shippingCost = null;
                if (row.PriceWithShipping != null && row.PriceWithShipping > row.Price)
                {
                    shippingCost = row.PriceWithShipping - row.Price;
                }

                var ph = new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = canonicalStoreName,
                    Price = row.Price.Value,
                    Position = null,
                    IsBidding = "XML",
                    ScrapHistory = scrapHistory,
                    IsGoogle = true,
                    GoogleInStock = inStock,
                    ShippingCostNum = shippingCost
                };

                priceHistoriesBag.Add(ph);
                result.AppendedHistories.Add(ph);
                result.ProductIdsWithAppended.Add(product.ProductId);
                appended++;
            }

            Console.WriteLine($"[CopyXmlPrices] Sklep {store.StoreId}: doklejono {appended} cen z XML.");
            return result;
        }

        private async Task<string> LoadXmlAsync(string url)
        {
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var path = Uri.UnescapeDataString(url.Substring("file://".Length)).TrimStart('/', '\\');
                return await File.ReadAllTextAsync(path);
            }
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PriceSafari/1.0)");
            return await client.GetStringAsync(url);
        }

        private class XmlRow
        {
            public decimal? Price { get; set; }
            public decimal? PromoPrice { get; set; }

            public decimal? PriceWithShipping { get; set; }
            public string? InStockRaw { get; set; }
        }

        private Dictionary<string, XmlRow> BuildXmlIndex(XmlDocument xmlDoc, CopyXmlPriceMapping mapping)
        {
            var result = new Dictionary<string, XmlRow>();

            var productTag = mapping.ProductNodeXPath!.Split('/').LastOrDefault()?.Split('[').FirstOrDefault();
            if (string.IsNullOrEmpty(productTag)) return result;

            var entries = xmlDoc.GetElementsByTagName(productTag);
            foreach (XmlNode entry in entries)
            {
                var keyVal = ExtractValue(entry, mapping.KeyXPath!, mapping.ProductNodeXPath!);
                if (string.IsNullOrWhiteSpace(keyVal)) continue;
                var k = keyVal.Trim();
                if (result.ContainsKey(k)) continue;

                var row = new XmlRow
                {
                    Price = ParsePrice(ExtractValue(entry, mapping.PriceXPath!, mapping.ProductNodeXPath!)),

                    PromoPrice = !string.IsNullOrEmpty(mapping.PromoPriceXPath)
                        ? ParsePrice(ExtractValue(entry, mapping.PromoPriceXPath, mapping.ProductNodeXPath!))
                        : null,
                    PriceWithShipping = mapping.PriceWithShippingXPath != null
                        ? ParsePrice(ExtractValue(entry, mapping.PriceWithShippingXPath, mapping.ProductNodeXPath!))
                        : null,
                    InStockRaw = mapping.InStockXPath != null
                        ? ExtractValue(entry, mapping.InStockXPath, mapping.ProductNodeXPath!)
                        : null
                };

                if (row.PromoPrice.HasValue && row.PromoPrice.Value > 0)
                {
                    row.Price = row.PromoPrice.Value;
                }

                result[k] = row;
            }
            return result;
        }

        private string? ExtractValue(XmlNode entryNode, string fullXPath, string productNodePath)
        {
            string productNodeName = productNodePath.Split('/').LastOrDefault() ?? "";
            var identifier = "/" + productNodeName;
            var last = fullXPath.LastIndexOf(identifier, StringComparison.Ordinal);
            string relative;
            if (last != -1) relative = "." + fullXPath.Substring(last + identifier.Length);
            else
            {
                var pureName = "/" + productNodeName.Split('[')[0];
                var lastP = fullXPath.LastIndexOf(pureName, StringComparison.Ordinal);
                if (lastP == -1) return null;
                relative = "." + fullXPath.Substring(lastP + pureName.Length);
            }

            bool valueMode = fullXPath.EndsWith("/#value");
            if (valueMode) relative = relative.Substring(0, relative.Length - "/#value".Length);

            var nsMgr = new XmlNamespaceManager(entryNode.OwnerDocument!.NameTable);
            nsMgr.AddNamespace("g", "http://base.google.com/ns/1.0");

            try
            {

                var attrMatch = Regex.Match(relative, @"^(.*)/@([^/]+)$");
                if (attrMatch.Success)
                {
                    var elemPath = string.IsNullOrEmpty(attrMatch.Groups[1].Value) ? "." : attrMatch.Groups[1].Value;
                    var attrName = attrMatch.Groups[2].Value;
                    var node = entryNode.SelectSingleNode(elemPath, nsMgr);
                    return node?.Attributes?[attrName]?.Value?.Trim();
                }

                var result = entryNode.SelectSingleNode(relative, nsMgr);
                if (result == null)
                {

                    var tag = relative.Replace(".", "").TrimStart('/').Split('/').LastOrDefault()?.Split(':').LastOrDefault();
                    if (!string.IsNullOrEmpty(tag))
                    {
                        foreach (XmlNode c in ((XmlElement)entryNode).GetElementsByTagName("*"))
                        {
                            if (c.LocalName == tag) { result = c; break; }
                        }
                    }
                }
                if (result == null) return null;
                var txt = result.InnerText.Trim();
                if (string.IsNullOrEmpty(txt) && result is XmlElement xe)
                {
                    if (xe.LocalName == "link" && xe.HasAttribute("href")) return xe.GetAttribute("href");
                    if (xe.HasAttribute("src")) return xe.GetAttribute("src");
                }
                return txt;
            }
            catch { return null; }
        }

        private decimal? ParsePrice(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var clean = Regex.Replace(s, @"[^\d.,-]", "");
            if (clean.Contains(',') && clean.Contains('.'))
            {
                if (clean.IndexOf(',') < clean.IndexOf('.')) clean = clean.Replace(",", "");
                else clean = clean.Replace(".", "").Replace(",", ".");
            }
            else if (clean.Contains(',')) clean = clean.Replace(",", ".");
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return Math.Round(v, 2);
            return null;
        }
    }
}