using PriceSafari.Models;
using PuppeteerSharp;
using System.Threading.Tasks;

namespace PriceSafari.Services
{
    public class GooglePriceScraper
    {
        private Browser _browser;

        public async Task InitializeAsync()
        {
            // Pobieramy przeglądarkę Puppeteer
            await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        }

        public async Task ScrapePricesAsync(GoogleScrapingProduct scrapingProduct)
        {
            // Otwieramy nową stronę
            using var page = await _browser.NewPageAsync();

            // Odwiedzamy URL produktu z Google Shopping
            await page.GoToAsync(scrapingProduct.GoogleUrl);

            // Zbieramy dane ze strony
            var storeNames = await page.EvaluateExpressionAsync<string[]>("Array.from(document.querySelectorAll('.sh-osd__merchant-name')).map(e => e.textContent)");
            var prices = await page.EvaluateExpressionAsync<string[]>("Array.from(document.querySelectorAll('.sh-osd__price')).map(e => e.textContent)");
            var offerUrls = await page.EvaluateExpressionAsync<string[]>("Array.from(document.querySelectorAll('.sh-osd__merchant a')).map(e => e.href)");

            // Zwracamy zebrane dane
            var scrapedData = new List<PriceData>();
            for (int i = 0; i < storeNames.Length; i++)
            {
                scrapedData.Add(new PriceData
                {
                    StoreName = storeNames[i],
                    Price = decimal.Parse(prices[i].Replace("$", "").Replace(",", "").Trim()), // Na potrzeby przykładu, zmień formatowanie, jeśli to nie dolar
                    OfferUrl = offerUrls[i],
                    ScrapingProductId = scrapingProduct.ScrapingProductId,
                    RegionId = scrapingProduct.RegionId
                });
            }

            // Zwracamy zebrane dane do kontrolera
            return scrapedData;
        }

        public async Task CloseAsync()
        {
            await _browser.CloseAsync();
        }
    }
}
