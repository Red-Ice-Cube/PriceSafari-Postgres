using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PriceTracker.Services
{
    public class Scraper
    {
        private readonly HttpClient _httpClient;

        public Scraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(List<(string storeName, decimal price, string shippingCost, decimal? shippingCostNum, string availability, int? availabilityNum)> Prices, string Log)> GetProductPricesAsync(string url)
        {
            var prices = new List<(string storeName, decimal price, string shippingCost, decimal? shippingCostNum, string availability, int? availabilityNum)>();
            string log;

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var offerNodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'product-offers__list__item')]");

                if (offerNodes != null)
                {
                    foreach (var offerNode in offerNodes)
                    {
                        var storeName = offerNode.SelectSingleNode(".//div[@class='product-offer__store']//img")?.GetAttributeValue("alt", "");
                        var priceNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='value']");
                        var pennyNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='penny']");
                        var shippingNode = offerNode.SelectSingleNode(".//div[contains(@class, 'free-delivery-label')]") ??
                                           offerNode.SelectSingleNode(".//span[contains(@class, 'product-delivery-info js_deliveryInfo')]");
                        var availabilityNode = offerNode.SelectSingleNode(".//span[contains(@class, 'instock')]") ??
                                              offerNode.SelectSingleNode(".//span[contains(text(), 'Wysyłka')]");

                        string shippingCost = shippingNode?.InnerText.Trim() ?? "Nieznana";
                        decimal? shippingCostNum = null;
                        if (shippingNode != null && !shippingNode.InnerText.Contains("Darmowa wysyłka") && !shippingNode.InnerText.Contains("Nieznana"))
                        {
                            var shippingCostText = Regex.Match(shippingNode.InnerText, @"\d+[.,]?\d*").Value;
                            if (decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                            {
                                shippingCostNum = parsedShippingCost;
                            }
                        }

                        string availability = availabilityNode?.InnerText.Trim() ?? "Nieznana";
                        int? availabilityNum = null;
                        if (!availability.Contains("Nieznana"))
                        {
                            if (availability.Contains("Wysyłka w 1 dzień"))
                            {
                                availabilityNum = 1;
                            }
                            else if (availability.Contains("Wysyłka do"))
                            {
                                var daysText = Regex.Match(availability, @"\d+").Value;
                                if (int.TryParse(daysText, out var parsedDays))
                                {
                                    availabilityNum = parsedDays;
                                }
                            }
                        }

                        if (priceNode != null && pennyNode != null && !string.IsNullOrEmpty(storeName))
                        {
                            var priceText = priceNode.InnerText.Trim() + pennyNode.InnerText.Trim();
                            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                            if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                            {
                                prices.Add((storeName, price, shippingCost, shippingCostNum, availability, availabilityNum));
                            }
                        }
                    }
                    log = $"Successfully scraped prices from URL: {url}";
                    return (prices, log);
                }

                log = $"Failed to find prices on URL: {url}";
                throw new Exception("Prices not found on the page.");
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                return (prices, log);
            }
        }
    }
}
