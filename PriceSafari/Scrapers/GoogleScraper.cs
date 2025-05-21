using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

// Nowa klasa do opakowywania wyników i statusu CAPTCHA
public class ScraperResult<T>
{
    public T Data { get; set; }
    public bool IsSuccess { get; set; }
    public bool CaptchaEncountered { get; set; }
    public string ErrorMessage { get; set; }

    // Konstruktor dla sukcesu bez CAPTCHA
    public ScraperResult(T data)
    {
        Data = data;
        IsSuccess = true;
        CaptchaEncountered = false;
        ErrorMessage = null;
    }

    // Konstruktor dla różnych scenariuszy
    public ScraperResult(T data, bool isSuccess, bool captchaEncountered, string errorMessage = null)
    {
        Data = data;
        IsSuccess = isSuccess;
        CaptchaEncountered = captchaEncountered;
        ErrorMessage = errorMessage;
    }

    // Statyczne metody pomocnicze do tworzenia instancji
    public static ScraperResult<T> Success(T data) => new ScraperResult<T>(data, true, false);
    public static ScraperResult<T> Fail(string errorMessage, T defaultValue = default) => new ScraperResult<T>(defaultValue, false, false, errorMessage);
    public static ScraperResult<T> Captcha(T defaultValue = default) => new ScraperResult<T>(defaultValue, false, true, "CAPTCHA encountered.");
}

public class GoogleScraper
{
    private IBrowser _browser;
    private IPage _page;

    public IPage CurrentPage => _page;
    public bool IsCaptchaEncountered { get; private set; } // Flaga do sygnalizacji CAPTCHA

    public async Task InitializeBrowserAsync()
    {
        IsCaptchaEncountered = false; // Reset flagi przy inicjalizacji
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false, 
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

        _page = await _browser.NewPageAsync();
    
    }

    public async Task<ScraperResult<List<string>>> SearchInitialProductCIDsAsync(string title, int maxCIDsToExtract = 10)
    {
        var cids = new List<string>();
        Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial CIDs.");
        IsCaptchaEncountered = false;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
            {
                await InitializeBrowserAsync();
            }

            var encodedTitle = HttpUtility.UrlEncode(title);
            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

            await _page.GoToAsync(url, new NavigationOptions
            {
                Timeout = 60000, // Zwiększony timeout dla pewności
                WaitUntil = new[] { WaitUntilNavigation.Load }
            });

            var currentUrl = _page.Url;
            if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
            {
                Console.WriteLine("Natrafiono na stronę CAPTCHA podczas wyszukiwania.");
                IsCaptchaEncountered = true;
                // await CloseBrowserAsync(); // Rozważ, czy zamykać tutaj, czy dać szansę kontrolerowi
                return ScraperResult<List<string>>.Captcha(cids); // Zwraca informację o CAPTCHA
            }

            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null)
            {
                Console.WriteLine("Znaleziono przycisk 'Odrzuć wszystko'. Klikam...");
                await rejectButton.ClickAsync();
                await Task.Delay(1000); // Krótkie opóźnienie na przetworzenie kliknięcia
            }

            Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");

            // Czekanie na pojawienie się boksów produktów
            try
            {
                 await _page.WaitForSelectorAsync("div.sh-dgr__content", new WaitForSelectorOptions { Timeout = 100 }); // Zaktualizowany selektor i timeout
            }
            catch (WaitTaskTimeoutException)
            {
                 // Próba z alternatywnym selektorem, jeśli pierwszy zawiedzie
                try
                {
                    await _page.WaitForSelectorAsync("div.MtXiu", new WaitForSelectorOptions { Timeout = 500 });
                }
                catch (WaitTaskTimeoutException ex)
                {
                     Console.WriteLine($"Nie znaleziono boksów produktów (ani .sh-dgr__content ani .MtXiu) w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");
                     return ScraperResult<List<string>>.Fail("Nie znaleziono boksów produktów.", cids);
                }
            }


            // Preferowany selektor dla boksów produktów, jeśli istnieje
            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content"); // Bardziej ogólny selektor dla kontenera produktu
            if (productBoxes.Length == 0)
            {
                // Fallback do starszego selektora, jeśli nowy nic nie zwróci
                productBoxes = await _page.QuerySelectorAllAsync("div.MtXiu.mZ9c3d.wYFOId.M919M.W5CKGc.wTrwWd");
            }


            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

            if (productBoxes.Length == 0)
            {
                return ScraperResult<List<string>>.Success(cids); // Brak produktów, ale operacja udana
            }

            int count = 0;
            foreach (var box in productBoxes)
            {
                if (count >= maxCIDsToExtract) break;

                // Próba wyciągnięcia data-cid z linku wewnątrz boksu, jeśli bezpośredni atrybut nie działa
                string cid = null;
                var linkElement = await box.QuerySelectorAsync("a[data-cid]");
                if (linkElement != null)
                {
                    cid = await linkElement.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
                } else {
                    // Fallback do oryginalnej logiki, jeśli link z data-cid nie został znaleziony
                    cid = await box.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
                }


                if (!string.IsNullOrEmpty(cid))
                {
                    cids.Add(cid);
                    Console.WriteLine($"Ekstrahowano CID: {cid}");
                    count++;
                }
                else
                {
                     // Spróbuj wyciągnąć CID z atrybutu 'data-docid' jeśli 'data-cid' nie ma
                    var productLink = await box.QuerySelectorAsync("a[data-docid]");
                    if (productLink != null)
                    {
                        cid = await productLink.EvaluateFunctionAsync<string>("element => element.getAttribute('data-docid')");
                        if (!string.IsNullOrEmpty(cid))
                        {
                             cids.Add(cid);
                             Console.WriteLine($"Ekstrahowano CID (z data-docid): {cid}");
                             count++;
                        }
                    } else {
                        Console.WriteLine("Nie udało się wyekstrahować CID z boksu produktu (ani data-cid, ani data-docid w linku).");
                    }
                }
            }
            return ScraperResult<List<string>>.Success(cids);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji CID-ów: {ex.Message}");
            // await CloseBrowserAsync(); // Rozważ, czy zamykać tutaj
            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji CID: {ex.Message}", cids);
        }
    }

    public async Task<ScraperResult<bool>> NavigateToProductPageAndExpandOffersAsync(string cid)
    {
        Console.WriteLine($"Nawigacja do strony ofert produktu Google Shopping (CID: {cid})...");
        IsCaptchaEncountered = false;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
            {
                await InitializeBrowserAsync();
            }

            var url = $"https://www.google.com/shopping/product/{cid}/offers?prds=cid:{cid},cond:1&gl=pl&hl=pl";
            await _page.GoToAsync(url, new NavigationOptions
            {
                Timeout = 60000,
                WaitUntil = new[] { WaitUntilNavigation.Load }
            });

            var currentUrl = _page.Url;
            if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
            {
                Console.WriteLine("Natrafiono na stronę CAPTCHA podczas nawigacji do ofert produktu.");
                IsCaptchaEncountered = true;
                // await CloseBrowserAsync();
                return ScraperResult<bool>.Captcha(false);
            }

            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null)
            {
                Console.WriteLine("Znaleziono przycisk 'Odrzuć wszystko'. Klikam...");
                await rejectButton.ClickAsync();
                await Task.Delay(1000);
            }


            return ScraperResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas nawigacji do strony ofert produktu Google Shopping (CID: {cid}): {ex.Message}");
            // await CloseBrowserAsync();
            return ScraperResult<bool>.Fail($"Błąd nawigacji do ofert: {ex.Message}", false);
        }
    }

    public async Task<ScraperResult<List<string>>> ExtractStoreOffersAsync(IPage page)
    {
        var storeUrls = new List<string>();
  
        IsCaptchaEncountered = false;

        try
        {
            // Sprawdzenie CAPTCHA na podstawie URL przed próbą ekstrakcji
            if (page.Url.Contains("/sorry/") || page.Url.Contains("/captcha"))
            {
                 Console.WriteLine("Strona ofert jest stroną CAPTCHA. Przerywam ekstrakcję ofert.");
                 IsCaptchaEncountered = true;
                 return ScraperResult<List<string>>.Captcha(storeUrls);
            }

            // Czekanie na selektor linków ofert
            try
            {
                 await page.WaitForSelectorAsync("a.sh-osd__seller-link", new WaitForSelectorOptions { Timeout = 500 }); // Zaktualizowany selektor i dłuższy timeout
            }
            catch(WaitTaskTimeoutException)
            {
                // Fallback do starego selektora, jeśli nowy nie zadziałał
                try
                {
                    await page.WaitForSelectorAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp", new WaitForSelectorOptions { Timeout = 500 });
                }
                catch (WaitTaskTimeoutException ex)
                {
                     Console.WriteLine($"Nie znaleziono elementów linków ofert sklepów (ani .sh-osd__seller-link ani .UAVKwf > a) w określonym czasie: {ex.Message}. Możliwe, że strona się zmieniła lub brak ofert.");
                     return ScraperResult<List<string>>.Success(storeUrls); // Traktujemy jako sukces, ale z pustą listą, bo nie ma ofert
                }

            }

            var offerLinkElements = await page.QuerySelectorAllAsync("a.sh-osd__seller-link");
            if (offerLinkElements.Length == 0)
            {
                offerLinkElements = await page.QuerySelectorAllAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp");
            }

            Console.WriteLine($"Znaleziono {offerLinkElements.Length} linków ofert.");

            foreach (var linkElement in offerLinkElements)
            {
                var rawStoreUrl = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
                if (string.IsNullOrEmpty(rawStoreUrl))
                {
                    Console.WriteLine("Pusty href, pomijam...");
                    continue;
                }

                string extractedStoreUrl;
                if (rawStoreUrl.Contains("google.com/url"))
                {
                    // teraz złapiemy każdy google-redirect, nie tylko te bez domeny
                    extractedStoreUrl = ExtractStoreUrlFromGoogleRedirect(rawStoreUrl);
                }
                else
                {
                    // zwykły link sklepu
                    extractedStoreUrl = rawStoreUrl;
                }

                var cleanedStoreUrl = CleanUrlParameters(extractedStoreUrl);
                storeUrls.Add(cleanedStoreUrl);
                Console.WriteLine($"Ekstrahowano i oczyszczono URL sklepu: {cleanedStoreUrl}");
            }

            return ScraperResult<List<string>>.Success(storeUrls);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas ekstrakcji ofert sklepów: {ex.Message}");
            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji ofert: {ex.Message}", storeUrls);
        }
    }

    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
    {
        try
        {
            // Jeśli URL zaczyna się od /url?q=, doklej domenę Google
            string fullUrl = googleRedirectUrl;
            if (googleRedirectUrl.StartsWith("/url?q="))
            {
                fullUrl = "https://www.google.com" + googleRedirectUrl;
            }

            var uri = new Uri(fullUrl);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var storeUrlEncoded = queryParams["q"] ?? queryParams["url"]; // Sprawdź 'q' a potem 'url'

            Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Parametr 'q' lub 'url' (zakodowany) = {storeUrlEncoded}");

            if (!string.IsNullOrEmpty(storeUrlEncoded))
            {
                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
                Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Odkodowany URL sklepu = {storeUrl}");
                return storeUrl;
            }
            else
            {
                Console.WriteLine($"DEBUG: Parametr 'q' ani 'url' nie znaleziony w query string. Zwracam cały URL jako fallback: {googleRedirectUrl}");
                return googleRedirectUrl; // Zwróć oryginalny, jeśli nie ma parametru q/url
            }
        }
        catch (UriFormatException ex)
        {
            Console.WriteLine($"BŁĄD: Nieprawidłowy format URL przekierowania w ExtractStoreUrlFromGoogleRedirect: '{googleRedirectUrl}'. Błąd: {ex.Message}. Zwracam go bez zmian.");
            return googleRedirectUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BŁĄD podczas ekstrakcji URL sklepu z przekierowania: {ex.Message}");
            return googleRedirectUrl;
        }
    }

    public string CleanUrlParameters(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        int qm = url.IndexOf("?");
        if (qm > 0)
            url = url.Substring(0, qm);

        int htmlIdx = url.LastIndexOf(".html");
        if (htmlIdx > 0)
        {
            var basePart = url.Substring(0, htmlIdx);
            int dot = basePart.LastIndexOf(".");
            if (dot > 0)
            {
                var suffix = basePart[(dot + 1)..];
                if (new[] { "google", "shopping", "merchant", "gshop", "product" }
                    .Any(s => suffix.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    url = basePart.Substring(0, dot) + ".html";
                }
            }
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var u1))
                url = u1.ToString();
            else if (Uri.TryCreate("http://" + url, UriKind.Absolute, out var u2))
                url = u2.ToString();
        }

        int hash = url.IndexOf("#");
        if (hash > 0)
            url = url.Substring(0, hash);

        return url;
    }

    public async Task CloseBrowserAsync()
    {
        if (_page != null && !_page.IsClosed)
        {
            try
            {
                await _page.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas zamykania strony: {ex.Message}");
            }
        }
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas zamykania przeglądarki: {ex.Message}");
            }
        }
        _browser = null;
        _page = null;
        IsCaptchaEncountered = false; 
    }
}






//using PuppeteerSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Web; // Upewnij się, że System.Web jest potrzebny i dostępny (np. przez pakiet System.Web.HttpUtility)

//public class GoogleScraper
//{
//    private IBrowser _browser;
//    private IPage _page;

//    public IPage CurrentPage => _page;

//    public async Task InitializeBrowserAsync()
//    {
//        var launchOptions = new LaunchOptions
//        {
//            Headless = false,
//            Args = new[]
//            {
//                "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu",
//                "--disable-blink-features=AutomationControlled", "--disable-software-rasterizer",
//                "--disable-extensions", "--disable-dev-shm-usage",
//                "--disable-features=IsolateOrigins,site-per-process", "--disable-infobars",
//            }
//        };

//        // Dla starszych wersji PuppeteerSharp, pobieranie przeglądarki może wymagać jawnego działania:
//        // var browserFetcher = new BrowserFetcher();
//        // if (!browserFetcher.LocalRevisions().Contains(BrowserFetcher.DefaultChromiumRevision)) // Sprawdź, czy domyślna wersja jest pobrana
//        // {
//        //    Console.WriteLine($"Pobieranie przeglądarki Chromium (wersja: {BrowserFetcher.DefaultChromiumRevision})...");
//        //    await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
//        //    Console.WriteLine("Pobieranie zakończone.");
//        // }


//        if (_browser == null || _browser.IsClosed)
//        {
//            _browser = await Puppeteer.LaunchAsync(launchOptions);
//        }

//        if (_page == null || _page.IsClosed)
//        {
//            _page = await _browser.NewPageAsync();
//            await _page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
//            await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
//            {
//                { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
//            });
//            await _page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36");
//        }
//    }

//    public async Task<string> SearchAndClickFirstProductAsync(string title)
//    {
//        Console.WriteLine($"Nawigacja do Google Shopping z tytułem produktu: {title}");
//        try
//        {
//            if (_browser == null || _page == null || _page.IsClosed)
//            {
//                Console.WriteLine("Przeglądarka lub strona nie zainicjalizowana. Inicjalizuję...");
//                await InitializeBrowserAsync();
//                if (_page == null || _page.IsClosed)
//                {
//                    Console.WriteLine("Nie udało się zainicjalizować strony. Zwracam null.");
//                    return null;
//                }
//            }

//            var encodedTitle = HttpUtility.UrlEncode(title);
//            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

//            Console.WriteLine($"Próba przejścia do URL: {url}");
//            await _page.GoToAsync(url, new NavigationOptions
//            {
//                Timeout = 60000,
//                WaitUntil = new[] { WaitUntilNavigation.Load, WaitUntilNavigation.Networkidle2 }
//            });
//            Console.WriteLine($"Aktualny URL po GoToAsync: {_page.Url}");

//            var currentUrl = _page.Url;
//            if (currentUrl.Contains("/sorry/") || currentUrl.Contains("consent.google.com"))
//            {
//                Console.WriteLine("Wykryto stronę CAPTCHA lub stronę zgody (cookies).");
//                var rejectConsentButton = await _page.QuerySelectorAsync("form[action*='reject'] button, button[aria-label*='Odrzuć'], button[jsname='tWT92d']");
//                if (rejectConsentButton != null)
//                {
//                    Console.WriteLine("Znaleziono przycisk odrzucenia zgody. Klikam...");
//                    await rejectConsentButton.ClickAsync();
//                    await _page.WaitForNavigationAsync(new NavigationOptions
//                    {
//                        Timeout = 30000,
//                        WaitUntil = new[] { WaitUntilNavigation.Load, WaitUntilNavigation.Networkidle2 }
//                    });
//                    Console.WriteLine("Po kliknięciu odrzucenia zgody, nowy URL: " + _page.Url);
//                    currentUrl = _page.Url;
//                }

//                if (currentUrl.Contains("/sorry/"))
//                {
//                    Console.WriteLine("Natrafiono na stronę CAPTCHA. Oczekiwanie 18 sekund na ręczne rozwiązanie...");
//                    await Task.Delay(18000);
//                    currentUrl = _page.Url;
//                    if (currentUrl.Contains("/sorry/"))
//                    {
//                        Console.WriteLine("CAPTCHA nie została rozwiązana. Zamykanie przeglądarki i zwracanie null.");
//                        await CloseBrowserAsync();
//                        return null;
//                    }
//                    else
//                    {
//                        Console.WriteLine("CAPTCHA prawdopodobnie rozwiązana. Kontynuuję...");
//                    }
//                }
//            }

//            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//            if (rejectButton != null)
//            {
//                Console.WriteLine("Znaleziono przycisk 'Odrzuć wszystko' na stronie wyników. Klikam...");
//                await rejectButton.ClickAsync();
//                await Task.Delay(2000);
//            }

//            Console.WriteLine("Strona wyników Google Shopping załadowana.");

//            var firstProductBox = await _page.QuerySelectorAsync("div.MtXiu.mZ9c3d.wYFOId.M919M.W5CKGc.wTrwWd");
//            if (firstProductBox != null)
//            {
//                string cid = await firstProductBox.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
//                Console.WriteLine($"Znaleziono data-cid: {cid} dla pierwszego produktu.");
//                try
//                {
//                    await firstProductBox.ClickAsync();
//                    Console.WriteLine("Kliknięto w pierwszy boks produktu.");
//                }
//                catch (Exception clickEx)
//                {
//                    Console.WriteLine($"OSTRZEŻENIE: Kliknięcie w boks produktu nie powiodło się: {clickEx.Message}. Próbuję ponownie...");
//                    await Task.Delay(300);
//                    try { await firstProductBox.ClickAsync(); Console.WriteLine("Ponowne kliknięcie powiodło się."); }
//                    catch (Exception retryEx) { Console.WriteLine($"BŁĄD: Ponowne kliknięcie również nie powiodło się: {retryEx.Message}."); return null; }
//                }
//                Console.WriteLine("Oczekiwanie 5 sekund na załadowanie sklepów.");
//                await Task.Delay(5000);
//                return cid;
//            }
//            else
//            {
//                Console.WriteLine("Nie znaleziono żadnych boksów produktowych. Zwracam null.");
//                return null;
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"KRYTYCZNY BŁĄD w SearchAndClickFirstProductAsync: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
//            await CloseBrowserAsync();
//            return null;
//        }
//    }

//    public async Task<(string Cid, List<string> Offers)?> ClickProductBoxByIndexAndExtractOffersAsync(int boxIndex, string productBoxSelector = "div.MtXiu.mZ9c3d.wYFOId.M919M.W5CKGc.wTrwWd")
//    {
//        if (_page == null || _page.IsClosed)
//        {
//            Console.WriteLine($"Strona nie jest dostępna w ClickProductBoxByIndexAndExtractOffersAsync (indeks: {boxIndex}). Zwracam null.");
//            return null;
//        }

//        var productBoxes = await _page.QuerySelectorAllAsync(productBoxSelector);
//        if (boxIndex < 0 || boxIndex >= productBoxes.Length)
//        {
//            Console.WriteLine($"Boks produktu o indeksie {boxIndex} jest poza zakresem (dostępne: {productBoxes.Length}).");
//            return null;
//        }

//        var productBoxToClick = productBoxes[boxIndex];
//        string cid = "";
//        try
//        {
//            cid = await productBoxToClick.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
//        }
//        catch (Exception evalEx)
//        {
//            Console.WriteLine($"Nie udało się pobrać CID dla boksu {boxIndex}: {evalEx.Message}.");
//            return null;
//        }

//        Console.WriteLine($"Próba kliknięcia boksu produktu o indeksie {boxIndex} z CID: {cid}");

//        try
//        {
//            // Sprawdzenie widoczności i przewinięcie, jeśli trzeba (zamiast ScrollIntoViewIfNeededAsync)
//            bool isVisible = await productBoxToClick.IsIntersectingViewportAsync();
//            if (!isVisible)
//            {
//                Console.WriteLine($"Boks {boxIndex} nie jest widoczny. Próba przewinięcia...");
//                await _page.EvaluateFunctionAsync("element => element.scrollIntoView({ behavior: 'auto', block: 'center', inline: 'center' })", productBoxToClick);
//                await Task.Delay(500);
//            }

//            await productBoxToClick.ClickAsync();
//            Console.WriteLine($"Kliknięto boks produktu o indeksie {boxIndex}.");

//            Console.WriteLine("Oczekiwanie 1 sekund na załadowanie sklepów dla wybranego boksu.");
//            await Task.Delay(1000);

//            var offers = await ExtractStoreOffersAsync(_page);
//            return (cid, offers);
//        }
//        catch (PuppeteerException pEx) // Użyj bardziej ogólnego PuppeteerException
//        {
//            Console.WriteLine($"Błąd Puppeteer (np. element odłączony) podczas klikania boksu {boxIndex} (CID: {cid}): {pEx.Message}");
//            return null;
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Ogólny błąd podczas klikania lub ekstrakcji dla boksu {boxIndex} (CID: {cid}): {ex.Message}");
//            return null;
//        }
//    }

//    public async Task<List<string>> ExtractStoreOffersAsync(IPage page)
//    {
//        var storeUrls = new List<string>();
//        Console.WriteLine("Rozpoczynanie ekstrakcji URL-i sklepów...");

//        if (page == null || page.IsClosed)
//        {
//            Console.WriteLine("Strona nie jest dostępna w ExtractStoreOffersAsync. Zwracam pustą listę.");
//            return storeUrls;
//        }

//        try
//        {
//            try
//            {
//                await page.WaitForSelectorAsync("div[role='listitem'].R5K7Cb", new WaitForSelectorOptions { Timeout = 2000 });
//            }
//            catch (WaitTaskTimeoutException)
//            {
//                Console.WriteLine("Timeout (2s) podczas oczekiwania na selektor ofert 'div[role=\"listitem\"].R5K7Cb'.");
//            }

//            int clickMoreStoresCount = 0;
//            int maxClickMoreStores = 5;
//            while (clickMoreStoresCount < maxClickMoreStores)
//            {
//                var moreStoresButton = await page.QuerySelectorAsync("div.jgbNbb.YbJ8Df.g5UPGe[role='button'][aria-label='Więcej sklepów']");
//                if (moreStoresButton == null)
//                {
//                    var buttons = await page.QuerySelectorAllAsync("div[role='button']");
//                    foreach (var btn in buttons)
//                    {
//                        // Zamiast GetAttributeAsync, użyj EvaluateFunctionAsync
//                        var ariaLabel = await btn.EvaluateFunctionAsync<string>("el => el.getAttribute('aria-label')");
//                        if (ariaLabel != null && ariaLabel.Trim() == "Więcej sklepów")
//                        {
//                            moreStoresButton = btn;
//                            break;
//                        }
//                    }
//                }

//                if (moreStoresButton != null)
//                {
//                    Console.WriteLine("Znaleziono przycisk 'Więcej sklepów'. Klikam...");
//                    try
//                    {
//                        await moreStoresButton.ClickAsync();
//                        clickMoreStoresCount++;
//                        await Task.Delay(2500);
//                    }
//                    catch (Exception clickEx)
//                    {
//                        Console.WriteLine($"OSTRZEŻENIE: Kliknięcie 'Więcej sklepów' nie powiodło się: {clickEx.Message}.");
//                        break;
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("Przycisk 'Więcej sklepów' nie znaleziony.");
//                    break;
//                }
//            }
//            if (clickMoreStoresCount >= maxClickMoreStores)
//            {
//                Console.WriteLine($"Osiągnięto limit {maxClickMoreStores} kliknięć 'Więcej sklepów'.");
//            }

//            var offerListItems = await page.QuerySelectorAllAsync("div[role='listitem'].R5K7Cb.SPI3ee.ePEmoc");
//            Console.WriteLine($"Znaleziono {offerListItems.Length} elementów listy ofert.");

//            if (offerListItems.Length == 0)
//            {
//                Console.WriteLine("Brak elementów ofert (div.R5K7Cb.SPI3ee.ePEmoc).");
//                return storeUrls;
//            }

//            foreach (var offerItem in offerListItems)
//            {
//                var linkElement = await offerItem.QuerySelectorAsync("a.P9159d.hMk97e.BbI1ub");
//                if (linkElement == null)
//                {
//                    linkElement = await offerItem.QuerySelectorAsync("a[href]");
//                    if (linkElement == null)
//                    {
//                        Console.WriteLine("OSTRZEŻENIE: Nie znaleziono elementu <a> w ofercie.");
//                        continue;
//                    }
//                }

//                var rawStoreUrl = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
//                Console.WriteLine($"DEBUG: Surowy URL oferty z href: {rawStoreUrl}");

//                if (!string.IsNullOrEmpty(rawStoreUrl))
//                {
//                    string processedUrl = rawStoreUrl;
//                    if (rawStoreUrl.Contains("/url?q=") && Uri.TryCreate(rawStoreUrl, UriKind.Absolute, out Uri googleRedirectUri))
//                    {
//                        var queryParams = HttpUtility.ParseQueryString(googleRedirectUri.Query);
//                        string actualUrl = queryParams["q"];
//                        if (!string.IsNullOrEmpty(actualUrl))
//                        {
//                            processedUrl = actualUrl;
//                            Console.WriteLine($"DEBUG: URL po wyciągnięciu z 'q': {processedUrl}");
//                        }
//                    }

//                    var cleanedStoreUrl = CleanUrlParameters(processedUrl);
//                    if (!string.IsNullOrWhiteSpace(cleanedStoreUrl))
//                    {
//                        storeUrls.Add(cleanedStoreUrl);
//                        Console.WriteLine($"Ekstrahowano i oczyszczono URL: {cleanedStoreUrl}");
//                    }
//                    else { Console.WriteLine("OSTRZEŻENIE: Oczyszczony URL jest pusty."); }
//                }
//                else { Console.WriteLine("OSTRZEŻENIE: Surowy URL z linku jest pusty/null."); }
//            }
//        }
//        catch (WaitTaskTimeoutException ex)
//        {
//            Console.WriteLine($"Timeout podczas oczekiwania na elementy: {ex.Message}.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd w ExtractStoreOffersAsync: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
//        }
//        return storeUrls.Distinct().ToList();
//    }

//    public string CleanUrlParameters(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return url;
//        string tempUrl = url;
//        try
//        {
//            if (tempUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
//            {
//                tempUrl = HttpUtility.UrlDecode(tempUrl);
//            }
//        }
//        catch { /* Ignoruj błąd dekodowania, użyj oryginalnego URL */ }

//        int questionMarkIndex = tempUrl.IndexOf("?");
//        if (questionMarkIndex >= 0) tempUrl = tempUrl.Substring(0, questionMarkIndex);
//        int hashIndex = tempUrl.IndexOf("#");
//        if (hashIndex >= 0) tempUrl = tempUrl.Substring(0, hashIndex);

//        if (!tempUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
//            !tempUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
//            tempUrl.Contains(".") && !tempUrl.StartsWith("/") && !tempUrl.Contains(":"))
//        {
//            if (Uri.TryCreate("https://" + tempUrl.TrimStart('/'), UriKind.Absolute, out Uri uriWithScheme) &&
//                (uriWithScheme.Scheme == Uri.UriSchemeHttps || uriWithScheme.Scheme == Uri.UriSchemeHttp))
//            {
//                tempUrl = uriWithScheme.ToString();
//            }
//        }
//        else if (Uri.TryCreate(tempUrl, UriKind.Absolute, out Uri parsedUri) &&
//                 (parsedUri.Scheme == Uri.UriSchemeHttps || parsedUri.Scheme == Uri.UriSchemeHttp))
//        {
//            tempUrl = parsedUri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
//        }

//        if (tempUrl.EndsWith("/") && tempUrl.Count(c => c == '/') > 2)
//        {
//            tempUrl = tempUrl.TrimEnd('/');
//        }
//        return tempUrl;
//    }

//    public async Task CloseBrowserAsync()
//    {
//        if (_page != null && !_page.IsClosed)
//        {
//            try { await _page.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd zamykania strony: {ex.Message}"); }
//            _page = null;
//        }
//        if (_browser != null && _browser.IsConnected)
//        {
//            try { await _browser.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd zamykania przeglądarki: {ex.Message}"); }
//            _browser = null;
//        }
//        Console.WriteLine("Przeglądarka i strona zostały zamknięte (lub już były).");
//    }
//}





// STARA METODA 



//using PuppeteerSharp;

//public class GoogleScraper
//{
//    private IBrowser _browser;
//    private IPage _page;

//    public async Task InitializeBrowserAsync()
//    {
//        var browserFetcher = new BrowserFetcher();
//        await browserFetcher.DownloadAsync();

//        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
//        {
//            Headless = false,
//            Args = new[]
//            {
//                "--no-sandbox",
//                "--disable-setuid-sandbox",
//                "--disable-gpu",
//                "--disable-blink-features=AutomationControlled",
//                "--disable-software-rasterizer",
//                "--disable-extensions",
//                "--disable-dev-shm-usage",
//                "--disable-features=IsolateOrigins,site-per-process",
//                "--disable-infobars",
//                "--use-gl=swiftshader",
//                "--disable-webgl",
//                "--ignore-gpu-blocklist"
//            }
//        });

//        _page = await _browser.NewPageAsync();
//        await _page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
//        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
//        {
//            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
//        });
//    }

//    public async Task InitializeAndSearchAsync(string title, string googleMiG)
//    {
//        if (string.IsNullOrEmpty(googleMiG))
//        {
//            throw new ArgumentException("GoogleMiG cannot be null or empty.", nameof(googleMiG));
//        }

//        Console.WriteLine("Navigating to Google Shopping with product title...");

//        int retries = 0;
//        const int maxRetries = 3;

//        while (retries < maxRetries)
//        {
//            try
//            {
//                if (_browser == null || _page == null)
//                {
//                    await InitializeBrowserAsync();
//                }

//                var encodedTitle = System.Web.HttpUtility.UrlEncode(title);
//                var url = $"https://www.google.com/search?gl=pl&tbm=shop&tbs=merchagg:{googleMiG}&q={encodedTitle}";

//                await _page.GoToAsync(url, new NavigationOptions
//                {
//                    Timeout = 60000,
//                    WaitUntil = new[] { WaitUntilNavigation.Load }
//                });

//                var currentUrl = _page.Url;
//                if (currentUrl.Contains("/sorry/"))
//                {
//                    Console.WriteLine("Natrafiono na stronę CAPTCHA. Oczekiwanie 18 sekund na rozwiązanie...");
//                    await Task.Delay(18000);
//                    currentUrl = _page.Url;


//                    if (currentUrl.Contains("/sorry/"))
//                    {
//                        Console.WriteLine("CAPTCHA nie została rozwiązana. Restartowanie przeglądarki...");
//                        await CloseBrowserAsync();
//                        retries++;
//                        continue;
//                    }
//                    else
//                    {
//                        Console.WriteLine("CAPTCHA została rozwiązana. Kontynuuję...");
//                    }
//                }

//                // Próbujemy kliknąć „Odrzuć wszystko”, jeśli jest dostępny
//                var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                }

//                Console.WriteLine("Strona załadowana pomyślnie.");
//                break;
//            }
//            catch (TimeoutException ex)
//            {
//                Console.WriteLine($"Błąd: Przekroczono czas podczas ładowania strony Google Shopping: {ex.Message}");
//                await CloseBrowserAsync();
//                retries++;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Błąd: {ex.Message}");
//                await CloseBrowserAsync();
//                retries++;
//            }
//        }

//        if (retries == maxRetries)
//        {
//            throw new Exception("Osiągnięto maksymalną liczbę prób nawigacji do Google Shopping.");
//        }
//    }

//    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
//    {
//        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
//        Console.WriteLine("Wyszukiwanie pasujących URL produktów na stronie wyników...");

//        try
//        {
//            await _page.WaitForSelectorAsync("div.sh-dgr__grid-result", new WaitForSelectorOptions { Timeout = 500 });
//            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__grid-result");
//            Console.WriteLine($"Znaleziono {productContainers.Length} boxów produktowych na stronie wyników.");

//            foreach (var container in productContainers)
//            {
//                string googleProductUrl = null;
//                string storeUrl = null;

//                try
//                {
//                    // Pobierz i wyczyść URL z Google Shopping
//                    var googleProductLinkElement = await container.QuerySelectorAsync("a[href^='/shopping/product/']");
//                    if (googleProductLinkElement != null)
//                    {
//                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
//                        Console.WriteLine($"Znaleziono URL Google Shopping: {googleProductUrl}");
//                    }

//                    // Pobierz URL sklepu
//                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
//                    if (storeLinkElement != null)
//                    {
//                        var googleRedirectUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        storeUrl = ExtractStoreUrlFromGoogleRedirect(googleRedirectUrl);
//                        Console.WriteLine($"Znaleziono URL sklepu: {storeUrl}");

//                        foreach (var searchUrl in searchUrls)
//                        {
//                            if (!string.IsNullOrEmpty(storeUrl) && !string.IsNullOrEmpty(searchUrl))
//                            {
//                                // Sprawdź, czy przekazany URL jest zawarty w znalezionym URL sklepu
//                                if (storeUrl.Contains(searchUrl, StringComparison.OrdinalIgnoreCase))
//                                {
//                                    if (!string.IsNullOrEmpty(googleProductUrl))
//                                    {
//                                        matchedUrls.Add((searchUrl, googleProductUrl));
//                                        Console.WriteLine($"Dopasowano URL produktu w Google: {googleProductUrl} do URL sklepu: {searchUrl}");
//                                    }
//                                }
//                            }
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Błąd podczas przetwarzania kontenera produktu: {ex.Message}");
//                }
//            }

//            Console.WriteLine($"Łączna liczba dopasowanych URL: {matchedUrls.Count}");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd podczas wyszukiwania URL produktów: {ex.Message}");
//        }

//        return matchedUrls;
//    }

//    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
//    {
//        try
//        {
//            var uri = new Uri(googleRedirectUrl);
//            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
//            var storeUrlEncoded = queryParams["url"];

//            if (!string.IsNullOrEmpty(storeUrlEncoded))
//            {
//                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
//                return storeUrl; // Zwracamy URL sklepu bez usuwania parametrów
//            }
//            else
//            {
//                return googleRedirectUrl; // W razie braku parametru 'url'
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd podczas ekstrakcji URL sklepu: {ex.Message}");
//            return googleRedirectUrl;
//        }
//    }

//    private string CleanGoogleUrl(string url)
//    {
//        int questionMarkIndex = url.IndexOf("?");
//        string cleanedUrl = questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;

//        if (!cleanedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
//        {
//            cleanedUrl = "https://www.google.com" + cleanedUrl;
//        }

//        return cleanedUrl;
//    }

//    public async Task CloseBrowserAsync()
//    {
//        if (_page != null && !_page.IsClosed)
//        {
//            await _page.CloseAsync();
//        }
//        if (_browser != null)
//        {
//            await _browser.CloseAsync();
//        }
//        _browser = null;
//        _page = null;
//    }
//}
