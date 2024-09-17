using PriceSafari.Models;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PriceSafari.Services
{
    public class GooglePriceScraper
    {
        private Browser _browser;
        private Page _page;

        // Metoda do inicjalizacji przeglądarki
        public async Task InitializeAsync(Settings settings)
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = settings.HeadLess,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-gpu",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-software-rasterizer",
                    "--disable-extensions",
                    "--disable-dev-shm-usage",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--disable-infobars"
                }
            });

            _page = (Page)await _browser.NewPageAsync();
            await _page.SetJavaScriptEnabledAsync(settings.JavaScript);
            Console.WriteLine("Przeglądarka zainicjalizowana.");
        }

        // Metoda do scrapowania cen z Google Shopping z logowaniem i opóźnieniami
        public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct)
        {
            var scrapedData = new List<PriceData>();

            try
            {
                // Zmiana URL na "/offers"
                var productOffersUrl = $"{scrapingProduct.GoogleUrl}/offers";
                Console.WriteLine($"Odwiedzanie URL: {productOffersUrl}");

                await _page.GoToAsync(productOffersUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                await Task.Delay(4000); // Dłuższe opóźnienie po załadowaniu strony

                // Sprawdzanie, czy elementy są dostępne
                var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
                var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
                var offersCount = offerRows.Length;

                if (offersCount == 0)
                {
                    Console.WriteLine("Brak ofert na stronie.");
                    return scrapedData;
                }

                Console.WriteLine($"Znaleziono {offersCount} ofert. Rozpoczynam scrapowanie...");

                // Iterowanie przez wiersze ofert i zbieranie danych
                for (int i = 1; i <= offersCount; i++)
                {
                    // Selektory dla nazwy sklepu, ceny i oferty
                    var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
                    var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
                    var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";

                    // Pobieranie nazwy sklepu
                    var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
                    if (storeNameElement != null)
                    {
                        // Usuwanie tekstu "Otwiera się w nowym oknie" poprzez selekcję samego "textContent" dla tagu <a>
                        var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                        Console.WriteLine($"Znaleziono sklep: {storeName}");

                        // Pobieranie ceny
                        var priceElement = await _page.QuerySelectorAsync(priceSelector);
                        var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
                        var cleanPrice = decimal.TryParse(priceText.Replace("zł", "").Replace(",", ".").Trim(), out var price) ? price : 0;
                        Console.WriteLine($"Cena: {priceText} (Oczyszczona: {cleanPrice})");

                        // Pobieranie URL oferty
                        var offerUrlElement = await _page.QuerySelectorAsync(offerUrlSelector);
                        var offerUrl = offerUrlElement != null ? await offerUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
                        Console.WriteLine($"URL oferty: {offerUrl}");

                        // Dodajemy dane do listy wyników
                        scrapedData.Add(new PriceData
                        {
                            StoreName = storeName,
                            Price = cleanPrice,
                            PriceWithDelivery = cleanPrice, // Zakładamy brak osobnej ceny za dostawę
                            OfferUrl = offerUrl,
                            ScrapingProductId = scrapingProduct.ScrapingProductId,
                            RegionId = scrapingProduct.RegionId
                        });

                        // Zapis po każdym przetworzonym elemencie
                        await Task.Delay(1000); // Opóźnienie przed przetworzeniem kolejnej oferty
                    }
                    else
                    {
                        Console.WriteLine($"Nie znaleziono elementu nazwy sklepu w wierszu {i}.");
                    }
                }

                Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
            }

            return scrapedData;
        }

        // Zamknięcie przeglądarki
        public async Task CloseAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
            Console.WriteLine("Przeglądarka zamknięta.");
        }
    }
}
