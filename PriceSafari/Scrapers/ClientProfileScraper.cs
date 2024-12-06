using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceSafari.Data;

namespace PriceSafari.Scrapers
{
    public class StoreData
    {
        public string OriginalUrl { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string StoreName { get; set; }
        public int ProductCount { get; set; }
    }

    public class ClientProfileScraper
    {
        private readonly PriceSafariContext _context;
        private Browser _browser;
        private Page _page;
        private bool _captchaSolved = false;
        private List<string> _existingUrls;

        public ClientProfileScraper(PriceSafariContext context, List<string> existingUrls)
        {
            _context = context;
            _existingUrls = existingUrls;
        }

        public async Task InitializeBrowserAsync()
        {
            try
            {
                Console.WriteLine("Initializing Puppeteer...");

                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();

                _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-gpu",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-software-rasterizer",
                        "--disable-infobars"
                    }
                });

                _page = (Page)await _browser.NewPageAsync();
                await _page.SetJavaScriptEnabledAsync(true);

                Console.WriteLine("Bot initialized. Warming up for 1 second...");
                await Task.Delay(1000);
                Console.WriteLine("Warm-up completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during browser initialization: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> ScrapeProfileUrlsAsync(string url)
        {
            if (_page == null)
                throw new InvalidOperationException("Browser is not initialized. Call InitializeBrowserAsync first.");

            try
            {
                Console.WriteLine($"Navigating to {url}...");
                await _page.GoToAsync(url);

                // Kliknięcie przycisku akceptacji ciasteczek
                try
                {
                    var cookieButtonSelector = "#js_cookie-consent-general > div > div.cookie-consent__buttons > button.cookie-consent__buttons__action.js_cookie-consent-necessary";
                    if (await _page.QuerySelectorAsync(cookieButtonSelector) != null)
                    {
                        await _page.ClickAsync(cookieButtonSelector);
                        Console.WriteLine("Cookie consent accepted.");
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cookie button not found or could not be clicked: {ex.Message}");
                }

                var mainSelector = "#click > div:nth-child(2) > section.product-offers.product-offers--standard.js_async-offers-section-standard > ul";
                await _page.WaitForSelectorAsync(mainSelector);

                var profileUrls = await _page.EvaluateExpressionAsync<IEnumerable<string>>(
                    @"
                    Array.from(document.querySelectorAll('#click > div:nth-child(2) > section.product-offers.product-offers--standard.js_async-offers-section-standard > ul > li > div > div.product-offer__details.js_offer-details > div.product-offer__details__toolbar > ul > li.offer-shop-opinions > a'))
                    .map(anchor => anchor.getAttribute('href'))
                    .filter(href => href !== null && href.includes('sklepy'))
                    "
                );

                if (profileUrls == null || !profileUrls.Any())
                {
                    Console.WriteLine("No URLs found on the page.");
                    return new List<string>();
                }

                var formattedUrls = profileUrls
                    .Select(u => "https://www.ceneo.pl/" + u.Split('#')[0].Trim())
                    .ToList();

                Console.WriteLine("Found URLs:");
                foreach (var fUrl in formattedUrls)
                {
                    Console.WriteLine(fUrl);
                }

                return formattedUrls;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during scraping: {ex.Message}");
                throw;
            }
        }

        public async Task<List<StoreData>> ProcessNewUrlsAsync(List<string> newUrls)
        {
            if (_page == null)
                throw new InvalidOperationException("Browser is not initialized. Call InitializeBrowserAsync first.");

            var results = new List<StoreData>();

            foreach (var url in newUrls)
            {
                try
                {
                    // Sprawdź czy już w momencie przetwarzania nie mamy tego URL-a
                    if (_existingUrls.Contains(url))
                    {
                        Console.WriteLine($"URL {url} already exists in DB, skipping.");
                        continue;
                    }

                    Console.WriteLine($"Processing URL: {url}");
                    await _page.GoToAsync(url);

                    // Kliknięcie przycisku wyświetlania e-maila, jeśli istnieje
                    var emailButtonSelector = "#body > div.wrapper.site-full-width > article > section > div.main-details-container > div.main-info-container > div.main-content-card > div:nth-child(4) > div:nth-child(2) > span.js_showShopEmailLinkSSL.dotted-link";
                    var emailButton = await _page.QuerySelectorAsync(emailButtonSelector);

                    if (emailButton != null)
                    {
                        Console.WriteLine($"Clicking to reveal email on: {url}");
                        await emailButton.ClickAsync();

                        // Jeżeli captcha nie została jeszcze rozwiązana, dajmy użytkownikowi szansę
                        if (!_captchaSolved)
                        {
                            Console.WriteLine("Jeżeli pojawiła się zagadka (captcha), rozwiąż ją teraz w przeglądarce i naciśnij Enter w konsoli, aby kontynuować...");
                            Console.ReadLine();
                            _captchaSolved = true;
                        }

                        // Poczekaj chwilę po rozwiązaniu captchy
                        await Task.Delay(2000);
                    }
                    else
                    {
                        Console.WriteLine($"Email button not found on {url}");
                    }

                    // Pobierz email
                    var emailSelector = "#body > div.wrapper.site-full-width > article > section > div.main-details-container > div.main-info-container > div.main-content-card > div:nth-child(4) > div:nth-child(2) > span.js_ShopEmail.js_element-to-click";
                    var email = await WaitForSelectorAndGetTextContentAsync(emailSelector);

                    // Pobierz telefon
                    var phoneSelector = "#body > div.wrapper.site-full-width > article > section > div.main-details-container > div.main-info-container > div.main-content-card > div:nth-child(3) > div:nth-child(2) > span";
                    var phone = await WaitForSelectorAndGetTextContentAsync(phoneSelector, optional: true);
                    if (string.IsNullOrWhiteSpace(phone))
                    {
                        phone = "BRAK";
                    }

                    if (string.IsNullOrEmpty(email))
                    {
                        email = "BRAK";
                    }

                    Console.WriteLine($"Email for {url}: {email}");
                    Console.WriteLine($"Phone for {url}: {phone}");

                    // Wyciągnięcie storeId z URL
                    var storeId = ExtractStoreId(url);

                    string storeName = null;
                    int productCountInt = 0;

                    if (!string.IsNullOrEmpty(storeId))
                    {
                        var storeProductsUrl = $"https://www.ceneo.pl/;{storeId}-0v.htm";
                        Console.WriteLine($"Navigating to store's products page: {storeProductsUrl}");
                        await _page.GoToAsync(storeProductsUrl);

                        var nameSelector = "#body > div > div > div.grid-cat__top > header > div > span > h1";
                        var productsCountSelector = "#body > div > div > div.grid-cat__top > header > div > span > span";

                        storeName = await WaitForSelectorAndGetTextContentAsync(nameSelector);
                        var productCount = await WaitForSelectorAndGetTextContentAsync(productsCountSelector);

                        // Usuwamy "Sklep " oraz białe znaki
                        if (!string.IsNullOrEmpty(storeName))
                        {
                            storeName = storeName.Replace("Sklep ", "").Trim();
                        }

                        // Usuwamy nawiasy z ilości produktów np. (720) -> 720
                        if (!string.IsNullOrEmpty(productCount))
                        {
                            productCount = productCount.Trim('(', ')');
                            int.TryParse(productCount, out productCountInt);
                        }

                        Console.WriteLine($"Store Name: {storeName}");
                        Console.WriteLine($"Products Count: {productCountInt}");
                    }
                    else
                    {
                        Console.WriteLine("Could not extract storeId from URL, skipping product count retrieval.");
                    }

                    // Nie pomijamy sklepów, dodajemy je wszystkie do wyników
                    results.Add(new StoreData
                    {
                        OriginalUrl = url,
                        Email = email,
                        Phone = phone,
                        StoreName = storeName,
                        ProductCount = productCountInt
                    });

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing URL {url}: {ex.Message}");
                }
            }

            return results;
        }


        private async Task<string> WaitForSelectorAndGetTextContentAsync(string selector, bool optional = false)
        {
            try
            {
                await _page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = 5000 });
                var content = await _page.EvaluateExpressionAsync<string>($"document.querySelector('{selector}').textContent");
                return content?.Trim();
            }
            catch (TimeoutException)
            {
                if (!optional)
                    Console.WriteLine($"Timeout waiting for selector: {selector}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error waiting for selector {selector}: {ex.Message}");
                return null;
            }
        }

        private string ExtractStoreId(string url)
        {
         
            try
            {
                var parts = url.Split('-');
                var lastPart = parts.LastOrDefault();
                if (lastPart != null && lastPart.StartsWith("s"))
                {
                    var idPart = lastPart.Substring(1);
                    return idPart;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting storeId from {url}: {ex.Message}");
            }
            return null;
        }

        public async Task CloseBrowserAsync()
        {
            if (_page != null)
            {
                await _page.CloseAsync();
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
            }
        }
    }
}
