using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Scrapers
{
    public class ClientProfileScraper
    {
        private readonly PriceSafariContext _context;
        private Browser _browser;
        private Page _page;

        public ClientProfileScraper(PriceSafariContext context)
        {
            _context = context;
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
                    Headless = false, // Tryb z głowicą
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
        public async Task<List<string>> ScrapeProfileUrlsAsync(string url, List<string> existingUrls)
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

                // Czekanie na załadowanie głównego kontenera strony
                var mainSelector = "#click > div:nth-child(2) > section.product-offers.product-offers--standard.js_async-offers-section-standard > ul";
                await _page.WaitForSelectorAsync(mainSelector);

                // Zbieranie URL-i profili
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

                // Formatuj URL-e
                var formattedUrls = profileUrls
                    .Select(u => "https://www.ceneo.pl/" + u.Split('#')[0].Trim())
                    .ToList();

                // Logowanie
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


        public async Task ProcessNewUrlsAsync(List<string> newUrls)
        {
            if (_page == null)
                throw new InvalidOperationException("Browser is not initialized. Call InitializeBrowserAsync first.");

            foreach (var url in newUrls)
            {
                try
                {
                    Console.WriteLine($"Processing URL: {url}");
                    await _page.GoToAsync(url);

                    // Kliknięcie przycisku wyświetlania e-maila, jeśli istnieje
                    var emailButtonSelector = "#body > div.wrapper.site-full-width > article > section > div.main-details-container > div.main-info-container > div.main-content-card > div:nth-child(4) > div:nth-child(2) > span.js_showShopEmailLinkSSL.dotted-link";
                    var emailButton = await _page.QuerySelectorAsync(emailButtonSelector);

                    if (emailButton != null)
                    {
                        Console.WriteLine($"Clicking to reveal email on: {url}");
                        await emailButton.ClickAsync();

                        // Tutaj zakładamy, że po kliknięciu może pojawić się captcha lub inna zagadka.
                        // Dajmy użytkownikowi czas na rozwiązanie.
                        // Zatrzymujemy proces i prosimy o wciśnięcie Enter w konsoli po rozwiązaniu zagadki.
                        Console.WriteLine("Jeżeli pojawiła się zagadka (captcha), rozwiąż ją teraz na otwartej przeglądarce i naciśnij Enter w konsoli, aby kontynuować...");
                        Console.ReadLine();

                        // Po wciśnięciu Enter zakładamy, że captcha rozwiązana, czekamy 2s aby strona się zaktualizowała.
                        await Task.Delay(2000);
                    }
                    else
                    {
                        Console.WriteLine($"Email button not found on {url}");
                    }

                    // Teraz sprawdzamy czy możemy pobrać email
                    var emailSelector = "#body > div.wrapper.site-full-width > article > section > div.main-details-container > div.main-info-container > div.main-content-card > div:nth-child(4) > div:nth-child(2) > span.js_ShopEmail.js_element-to-click";
                    var email = await WaitForEmailAsync(emailSelector);

                    if (!string.IsNullOrEmpty(email))
                    {
                        Console.WriteLine($"Found email for {url}: {email}");
                    }
                    else
                    {
                        Console.WriteLine($"No email found for {url}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing URL {url}: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Czeka na pojawienie się adresu e-mail w podanym selektorze.
        /// </summary>
        private async Task<string> WaitForEmailAsync(string emailSelector)
        {
            try
            {
                await _page.WaitForSelectorAsync(emailSelector, new WaitForSelectorOptions { Timeout = 60000 }); // Maksymalnie 60 sekund
                var email = await _page.EvaluateExpressionAsync<string>($"document.querySelector('{emailSelector}').textContent");
                return email?.Trim();
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Timeout waiting for email to appear.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error waiting for email: {ex.Message}");
                return null;
            }
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
