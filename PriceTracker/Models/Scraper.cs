using HtmlAgilityPack;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
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

      

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> GetProductPricesAsync(string url, int tryCount = 1)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                if (response.Contains("/Captcha/Add"))
                {
                    return (prices, "CAPTCHA encountered.", new List<(string Reason, string Url)> { ("CAPTCHA", url) });
                }

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

                        var offerContainer = offerNode.SelectSingleNode(".//div[contains(@class, 'product-offer__container')]");
                        var offerType = offerContainer?.GetAttributeValue("data-offertype", "");
                        var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
                        var position = offerContainer?.GetAttributeValue("data-position", "");

                        decimal? shippingCostNum = null;
                        if (priceNode != null && pennyNode != null && !string.IsNullOrEmpty(storeName))
                        {
                            var priceText = priceNode.InnerText.Trim() + pennyNode.InnerText.Trim();
                            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                            if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                            {
                                if (shippingNode != null)
                                {
                                    var shippingText = WebUtility.HtmlDecode(shippingNode.InnerText.Trim());

                                    if (shippingText.Contains("Darmowa wysyłka"))
                                    {
                                        shippingCostNum = 0;
                                    }
                                    else if (shippingText.Contains("szczegóły dostawy"))
                                    {
                                        shippingCostNum = null;
                                    }
                                    else
                                    {
                                        var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                                        if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                                        {
                                            shippingCostNum = parsedShippingCost;
                                        }
                                        else
                                        {
                                            shippingCostNum = null;
                                        }
                                    }
                                }

                                int? availabilityNum = null;
                                if (availabilityNode != null)
                                {
                                    if (availabilityNode.InnerText.Contains("Wysyłka w 1 dzień"))
                                    {
                                        availabilityNum = 1;
                                    }
                                    else if (availabilityNode.InnerText.Contains("Wysyłka do"))
                                    {
                                        var daysText = Regex.Match(availabilityNode.InnerText, @"\d+").Value;
                                        if (int.TryParse(daysText, out var parsedDays))
                                        {
                                            availabilityNum = parsedDays;
                                        }
                                    }
                                }

                                prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
                            }
                        }
                        else
                        {
                            string reason = "Missing store name or price information.";
                            rejectedProducts.Add((reason, url));
                        }
                    }
                    log = $"Successfully scraped prices from URL: {url}";
                }
                else
                {
                    log = $"Failed to find prices on URL: {url}";
                    string reason = "No offer nodes found.";
                    rejectedProducts.Add((reason, url));
                }
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                string reason = $"Exception: {ex.Message}";
                rejectedProducts.Add((reason, url));
            }

            return (prices, log, rejectedProducts);
        }




    }
}

