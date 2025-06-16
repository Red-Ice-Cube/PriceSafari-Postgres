using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Text.RegularExpressions;

[Authorize(Roles = "Manager, Admin")]
public class ClientProfileController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly UserManager<PriceSafariUser> _userManager;
    private readonly ILogger<ClientProfileController> _logger;
    private readonly IEmailSender _emailSender;

    public ClientProfileController(PriceSafariContext context, UserManager<PriceSafariUser> userManager, ILogger<ClientProfileController> logger, IEmailSender emailSender)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _emailSender = emailSender;
    }
    public IActionResult Index()
    {
        var clientProfiles = _context.ClientProfiles
            .Include(cp => cp.CreatedByUser)
            .ToList();

        return View("~/Views/ManagerPanel/ClientProfiles/Index.cshtml", clientProfiles);
    }

    public IActionResult Create()
    {

        return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml", new ClientProfile { Status = ClientStatus.Nowy, Source = ClientSource.Google });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClientProfile model)
    {
        var user = await _userManager.GetUserAsync(User);

        model.CreatedByUserId = user.Id;
        model.CreationDate = DateTime.Now;
        model.Status = ClientStatus.Nowy;

        ModelState.Remove("CreatedByUserId");
        ModelState.Remove("CreatedByUser");
        ModelState.Remove("Status");

        if (model.Source == ClientSource.Google)
        {
            ModelState.Remove("CeneoProfileUrl");
            ModelState.Remove("CeneoProfileTelephone");
            ModelState.Remove("CeneoProfileProductCount");
        }

        if (string.IsNullOrWhiteSpace(model.CeneoProfileName))
        {
            ModelState.AddModelError("CeneoProfileName", "Nazwa firmy jest wymagana.");
        }

        if (string.IsNullOrWhiteSpace(model.CeneoProfileEmail))
        {
            ModelState.AddModelError("CeneoProfileEmail", "Email jest wymagany.");
        }

        if (model.Source == ClientSource.Ceneo && string.IsNullOrWhiteSpace(model.CeneoProfileUrl))
        {
            ModelState.AddModelError("CeneoProfileUrl", "URL Ceneo jest wymagany dla klientów z Ceneo.");
        }

        if (ModelState.IsValid)
        {

            bool exists = false;
            if (model.Source == ClientSource.Ceneo)
            {
                exists = await _context.ClientProfiles
                    .AnyAsync(cp => cp.Source == ClientSource.Ceneo && cp.CeneoProfileUrl == model.CeneoProfileUrl);
                if (exists)
                {
                    ModelState.AddModelError("CeneoProfileUrl", "Klient z tym URL już istnieje.");
                }
            }
            else if (model.Source == ClientSource.Google)
            {

                exists = await _context.ClientProfiles
                    .AnyAsync(cp => cp.Source == ClientSource.Google && cp.CeneoProfileName == model.CeneoProfileName && cp.CeneoProfileEmail == model.CeneoProfileEmail);
                if (exists)
                {
                    ModelState.AddModelError("CeneoProfileName", "Klient z tą nazwą firmy i adresem e-mail już istnieje.");
                }
            }

            if (exists)
            {
                return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml", model);
            }

            try
            {
                _context.ClientProfiles.Add(model);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Profil klienta został dodany pomyślnie.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas zapisywania danych nowego klienta.");
                ModelState.AddModelError("", "Wystąpił błąd podczas zapisywania danych. Skontaktuj się z administratorem.");
            }
        }
        else
        {
            _logger.LogWarning("ModelState is invalid: {ModelStateErrors}",
                string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
        }

        return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml", model);
    }

    [HttpGet]
    public JsonResult CheckUrlExists(string url)
    {

        var exists = _context.ClientProfiles.Any(cp => cp.Source == ClientSource.Ceneo && cp.CeneoProfileUrl == url);
        return Json(new { exists });
    }

    [HttpGet]
    public async Task<JsonResult> CheckGoogleCompanyNameExists(string companyName)
    {
        var cleanedCompanyName = CleanCompanyName(companyName);
        var exists = await _context.ClientProfiles.AnyAsync(cp => cp.Source == ClientSource.Google && cp.CeneoProfileName == cleanedCompanyName);
        return Json(new { exists, cleanedName = cleanedCompanyName });
    }

    private string CleanCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return companyName;
        }

        string cleanedName = companyName.Trim();

        try
        {
            if (!cleanedName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !cleanedName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {

                cleanedName = "http://" + cleanedName;
            }
            var uri = new Uri(cleanedName);
            string host = uri.Host;

            host = Regex.Replace(host, @"^www\.", "", RegexOptions.IgnoreCase);

            return host.ToLowerInvariant();
        }
        catch (UriFormatException)
        {

            cleanedName = Regex.Replace(cleanedName, @"^(https?:\/\/)?(www\.)?", "", RegexOptions.IgnoreCase);

            cleanedName = cleanedName.Split('/')[0];

            cleanedName = cleanedName.Split('?')[0];
            cleanedName = cleanedName.Split('#')[0];

            cleanedName = Regex.Replace(cleanedName, @"[^a-zA-Z0-9\.-]", "");

            return cleanedName.ToLowerInvariant();
        }
    }
    public async Task<IActionResult> Edit(int id)
    {
        var clientProfile = await _context.ClientProfiles
            .Include(cp => cp.CreatedByUser)
            .FirstOrDefaultAsync(cp => cp.ClientProfileId == id);

        if (clientProfile == null)
        {
            return NotFound();
        }

        return View("~/Views/ManagerPanel/ClientProfiles/Edit.cshtml", clientProfile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ClientProfile model)
    {
        var existingProfile = await _context.ClientProfiles.FindAsync(id);

        if (existingProfile == null)
        {
            return NotFound();
        }

        ModelState.Remove("CreatedByUserId");
        ModelState.Remove("CreatedByUser");

        if (ModelState.IsValid)
        {

            bool urlExists = await _context.ClientProfiles
                .AnyAsync(cp => cp.CeneoProfileUrl == model.CeneoProfileUrl && cp.ClientProfileId != id);

            if (urlExists)
            {
                ModelState.AddModelError("CeneoProfileUrl", "Klient z tym URL już istnieje.");
                return View("~/Views/ManagerPanel/ClientProfiles/Edit.cshtml", model);
            }

            try
            {

                existingProfile.CeneoProfileName = model.CeneoProfileName;
                existingProfile.CeneoProfileEmail = model.CeneoProfileEmail;
                existingProfile.CeneoProfileTelephone = model.CeneoProfileTelephone;
                existingProfile.CeneoProfileUrl = model.CeneoProfileUrl;
                existingProfile.CeneoProfileProductCount = model.CeneoProfileProductCount;
                existingProfile.Status = model.Status;
                existingProfile.ScheduledMeetingDate = model.ScheduledMeetingDate;

                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Wystąpił błąd podczas zapisywania danych. Skontaktuj się z administratorem.");
            }
        }
        else
        {
            _logger.LogWarning("ModelState is invalid: {ModelStateErrors}",
                string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
        }

        return View("~/Views/ManagerPanel/ClientProfiles/Edit.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepareEmails(SelectedClientIdsModel model)
    {
        if (model == null || model.SelectedClientIds == null || !model.SelectedClientIds.Any())
        {
            return BadRequest("Nie wybrano żadnych klientów.");
        }

        var selectedClientIds = model.SelectedClientIds;

        var allClients = await _context.ClientProfiles.ToListAsync();
        var clients = allClients.Where(cp => selectedClientIds.Contains(cp.ClientProfileId)).ToList();

        if (!clients.Any())
        {
            return BadRequest("Nie znaleziono wybranych klientów.");
        }

        var sendEmailViewModel = new SendEmailViewModel
        {
            Clients = clients,
            SelectedClientIds = selectedClientIds,
            EmailSubject = "",
            EmailContent = GetEmailContent1(),
            SelectedMailType = 1
        };

        return View("~/Views/ManagerPanel/ClientProfiles/PrepareEmails.cshtml", sendEmailViewModel);
    }

    [HttpPost]
    public IActionResult ChangeMailTypeAjax(int mailType, List<int> selectedClientIds)
    {

        string baseContent;
        switch (mailType)
        {
            case 1:
                baseContent = GetEmailContent1();
                break;
            case 2:
                baseContent = GetEmailContent2();
                break;
            case 3:
                baseContent = GetEmailContent3();
                break;
            default:
                baseContent = GetEmailContent1();
                break;
        }

        return Json(new { content = baseContent });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSendEmails(SendEmailViewModel model)
    {
        if (model.SelectedClientIds == null || !model.SelectedClientIds.Any())
        {
            ModelState.AddModelError("", "Nie wybrano żadnych klientów.");
            return View("PrepareEmails", model);
        }

        if (string.IsNullOrWhiteSpace(model.EmailSubject))
        {
            ModelState.AddModelError("", "Temat emaila nie może być pusty.");
            return View("PrepareEmails", model);
        }

        string baseContent;
        switch (model.SelectedMailType)
        {
            case 1:

                baseContent = GetEmailContent1();
                break;
            case 2:

                baseContent = GetEmailContent2();
                break;
            case 3:

                baseContent = GetEmailContent3();
                break;
            default:

                baseContent = GetEmailContent1();
                break;
        }

        var allClients = await _context.ClientProfiles.ToListAsync();
        var clientsToEmail = allClients
            .Where(cp => model.SelectedClientIds.Contains(cp.ClientProfileId))
            .ToList();

        foreach (var client in clientsToEmail)
        {
            try
            {

                var personalizedContent = baseContent
                    .Replace("{ClientName}", client.CeneoProfileName);

                if (model.SelectedMailType == 1 || model.SelectedMailType == 3)
                {
                    personalizedContent = personalizedContent.Replace("{ProductCount}", client.CeneoProfileProductCount.ToString());
                }
                else
                {

                    personalizedContent = personalizedContent.Replace("{ProductCount}", "");
                }

                var emailBody = personalizedContent + GetEmailFooter();

                var emailAddresses = client.CeneoProfileEmail
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToList();

                foreach (var email in emailAddresses)
                {
                    await _emailSender.SendEmailAsync(email, model.EmailSubject, emailBody);
                }

                client.Status = ClientStatus.Mail;
                client.EmailSentCount += 1;
                client.LastEmailSentDate = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania maila do {Email}", client.CeneoProfileEmail);

            }
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Wiadomości zostały wysłane pomyślnie.";
        return RedirectToAction("Index");
    }

    private string GetEmailContent1()
    {
        return @"
        <html>
          <head>
            <meta charset=""UTF-8""/>
            <style>
                body {
                    font-size: 17px; 
                    font-family: Arial, sans-serif;
                    line-height: 1.5; 
                    margin: 0;
                    padding: 0;
                }
                p {
                    margin: 0 0 0px 0; 
                }
                ul {
                    margin: 0 0 10px 20px;
                    padding: 0;
                }
                li {
                    margin: 5px 0; 
                }
                img {
                    max-width: 100%; 
                    height: auto;
                }
            </style>
            </head>
            <body>

            <p>Dzień dobry,</p>

            <p>Monitorujemy ceny produktów na Google Shopping i Ceneo.</p>
            <p>Spojrzenie na cały rynek z jednego panelu pozwoli Państwu strategicznie zarządzać cenami własnych produktów, maksymalizować zyski i wyprzedzać konkurentów.</p>
            <p>Na samym Ceneo zaindeksowaliśmy <strong>{ProductCount}</strong> produktów dostępnych w Państwa sklepie, które moglibyśmy zacząć monitorować.</p>
            <br>
            <p>Dzięki śledzeniu cen, możemy szybko sprawdzić:</p>

            <ul>
                <li>Kto ma najlepszą cenę na danym produkcie?</li>
                <li>Gdzie w rankingu cenowym znajduje się Państwa oferta?</li>
                <li>Kto i gdzie promuje oferty?</li>
                <li>Jak szeroka jest konkurencja?</li>
                <li>Jaki czas wysyłki oferują poszczególni konkurenci?</li>
                <li>Które ceny produktów można podnieść, aby maksymalizować zyski, wiedząc, że są najtańszymi ofertami na całym rynku?</li>
                <li>Na jakich produktach wybrany konkurent zmienił wczoraj cenę?</li>
            </ul>

            <p>Funkcja Co-Pilota cenowego pozwala masowo wprowdzać zmiany, symulować wyniki i eksportować nowe ceny dla setek produktów kilkoma kliknięciami.</p>
            <br>

            <p>Panel PriceSafari prezentuje aktualną sytuację rynkową ofert z Państwa sklepu na tle konkurencji.</p> 
            <p>Dzięki zaawansowanym filtrom można precyzyjnie wybrać kanały oraz sklepy, z którymi chcą Państwo konkurować cenowo – i wykluczyć te, z którymi nie.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>Po kliknięciu w wybrany produkt, uzyskujemy podgląd wszystkich sklepów sprzedających dokładnie ten sam produkt w Polsce.</p>
            <p>Możemy sprawdzić, kto oferuje najniższą cenę, jakie są koszty dostawy oraz dostępność towaru.</p>
            <p>Dodatkowo zobaczymy, jak nasza oferta wypada na tle konkurencji – w zależności od źródła danych, takiego jak Ceneo czy Google Shopping.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>Od momentu rozpoczęcia analizy cenowej zaczynamy gromadzić dane o cenach i zapisywać historię 30 ostatnich analiz. </p>
            <p>Dzięki temu można śledzić, jak zmieniała się cena oraz dostępność danego produktu w czasie.</p>
            <p>Pozwala to m.in. sprawdzić, którzy z konkurentów modyfikują ceny w weekendy.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>PriceSafari umożliwia tworzenie zaawansowanych presetów widoków z danymi, dopasowanych do konkretnych potrzeb.</p>
            <p>Czasami nie chcemy konkurować z własnym sklepem czy np. z Amazonem – dlatego możemy łatwo wykluczyć wybrane źródła danych i skupić się tylko na tych konkurentach, którzy naprawdę nas interesują.</p>
            <br>
            <img src=""https:
            <br>

            <p>Do programu możemy zaimportować ceny zakupu towarów oraz skonfigurować wiele funkcji, które pozwalają precyzyjnie zarządzać zmianami cen w naszych ofertach.</p>
            <p>Przykładowo, możemy ustawić minimalną marżę na poziomie 8%, przeprowadzić symulację cenową dla wybranej grupy produktów, a następnie wyeksportować nowe ceny do pliku CSV lub XLSX.</p>
            <p>Potem wystarczy już tylko zaimportować je do sklepu internetowego i obserwować, jak rośnie sprzedaż – a cały proces, zamiast zajmować godziny żmudnej pracy, trwa dosłownie kilka minut.</p>
            <br>
            <img src=""https:

            <p>Oferujemy bezpłatne konto demo, na którym mogą Państwo przetestować nasz program na 1000 własnych produktów. Wystarczy przesłać feed produktowy z Państwa sklepu zawierający produkty, które mamy monitorować.</p>
            <p>Na tej podstawie przygotujemy konto, a podczas krótkiego spotkania online pokażemy, jak korzystać z Panelu PriceSafari w praktyce i przekażemy dostępy do konta.</p>
            <p>Jeśli są Państwo zainteresowani, prosimy o odpowiedź na tego maila – za Państwa zgodą z przyjemnością prześlemy szczegółową ofertę oraz przygotujemy darmowe konto demo.</p>
            <br>
            <br>
            <br>
        </body>
        </html>
        ";
    }

    private string GetEmailContent2()
    {
        return @"
        <html>
        <head>
            <meta charset=""UTF-8""/>
            <style>
                body {
                    font-size: 17px; 
                    font-family: Arial, sans-serif;
                    line-height: 1.5; 
                    margin: 0;
                    padding: 0;
                }
                p {
                    margin: 0 0 0px 0; 
                }
                ul {
                    margin: 0 0 10px 20px;
                    padding: 0;
                }
                li {
                    margin: 5px 0; 
                }
                img {
                    max-width: 100%; 
                    height: auto;
                }
            </style>
            </head>
            <body>

            <p>Dzień dobry,</p>

            <p>Monitorujemy ceny produktów na Google Shopping i Ceneo.</p>
            <p>Spojrzenie na cały rynek z jednego panelu pozwoli Państwu strategicznie zarządzać cenami własnych produktów, maksymalizować zyski i wyprzedzać konkurentów.</p>

            <br>
            <p>Dzięki śledzeniu cen, możemy szybko sprawdzić:</p>

            <ul>
                <li>Kto ma najlepszą cenę na danym produkcie?</li>
                <li>Gdzie w rankingu cenowym znajduje się Państwa oferta?</li>
                <li>Kto i gdzie promuje oferty?</li>
                <li>Jak szeroka jest konkurencja?</li>
                <li>Jaki czas wysyłki oferują poszczególni konkurenci?</li>
                <li>Które ceny produktów można podnieść, aby maksymalizować zyski, wiedząc, że są najtańszymi ofertami na całym rynku?</li>
                <li>Na jakich produktach wybrany konkurent zmienił wczoraj cenę?</li>
            </ul>

            <p>Funkcja Co-Pilota cenowego pozwala masowo wprowdzać zmiany, symulować wyniki i eksportować nowe ceny dla setek produktów kilkoma kliknięciami.</p>
            <br>

            <p>Panel PriceSafari prezentuje aktualną sytuację rynkową ofert z Państwa sklepu na tle konkurencji.</p> 
            <p>Dzięki zaawansowanym filtrom można precyzyjnie wybrać kanały oraz sklepy, z którymi chcą Państwo konkurować cenowo – i wykluczyć te, z którymi nie.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>Po kliknięciu w wybrany produkt, uzyskujemy podgląd wszystkich sklepów sprzedających dokładnie ten sam produkt w Polsce.</p>
            <p>Możemy sprawdzić, kto oferuje najniższą cenę, jakie są koszty dostawy oraz dostępność towaru.</p>
            <p>Dodatkowo zobaczymy, jak nasza oferta wypada na tle konkurencji – w zależności od źródła danych, takiego jak Ceneo czy Google Shopping.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>Od momentu rozpoczęcia analizy cenowej zaczynamy gromadzić dane o cenach i zapisywać historię 30 ostatnich analiz. </p>
            <p>Dzięki temu można śledzić, jak zmieniała się cena oraz dostępność danego produktu w czasie.</p>
            <p>Pozwala to m.in. sprawdzić, którzy z konkurentów modyfikują ceny w weekendy.</p>
            <br>
            <img src=""https:
            <br>
            <br>
            <br>
            <p>PriceSafari umożliwia tworzenie zaawansowanych presetów widoków z danymi, dopasowanych do konkretnych potrzeb.</p>
            <p>Czasami nie chcemy konkurować z własnym sklepem czy np. z Amazonem – dlatego możemy łatwo wykluczyć wybrane źródła danych i skupić się tylko na tych konkurentach, którzy naprawdę nas interesują.</p>
            <br>
            <img src=""https:
            <br>

            <p>Do programu możemy zaimportować ceny zakupu towarów oraz skonfigurować wiele funkcji, które pozwalają precyzyjnie zarządzać zmianami cen w naszych ofertach.</p>
            <p>Przykładowo, możemy ustawić minimalną marżę na poziomie 8%, przeprowadzić symulację cenową dla wybranej grupy produktów, a następnie wyeksportować nowe ceny do pliku CSV lub XLSX.</p>
            <p>Potem wystarczy już tylko zaimportować je do sklepu internetowego i obserwować, jak rośnie sprzedaż – a cały proces, zamiast zajmować godziny żmudnej pracy, trwa dosłownie kilka minut.</p>
            <br>
            <img src=""https:

            <p>Oferujemy bezpłatne konto demo, na którym mogą Państwo przetestować nasz program na 1000 własnych produktów. Wystarczy przesłać feed produktowy z Państwa sklepu zawierający produkty, które mamy monitorować.</p>
            <p>Na tej podstawie przygotujemy konto, a podczas krótkiego spotkania online pokażemy, jak korzystać z Panelu PriceSafari w praktyce i przekażemy dostępy do konta.</p>
            <p>Jeśli są Państwo zainteresowani, prosimy o odpowiedź na tego maila – za Państwa zgodą z przyjemnością prześlemy szczegółową ofertę oraz przygotujemy darmowe konto demo.</p>
            <br>
            <br>
            <br>
        </body>
        </html>
        ";
    }

    private string GetEmailContent3()
    {
        return @"
        <div style=""font-family: Arial, Helvetica, sans-serif; color: #333333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 5px; overflow: hidden;"">
            <div style=""background-color: #222222; color: #ffffff; padding: 20px; text-align: center;"">
                <h1 style=""margin: 0; font-size: 24px;"">Monitoring cen z PriceSafari</h1>
            </div>
            <div style=""padding: 20px 30px; line-height: 1.8;"">
                <p>Dzień dobry,</p>
                <p>Prowadzimy monitoring cen produktów na Google Shopping i Ceneo.</p>
                <p>Spojrzenie na cały rynek z jednego panelu pozwoli Państwu strategicznie zarządzać cenami własnych produktów, maksymalizować zyski i wyprzedzać konkurentów.</p>
                
                <h3 style=""color: #222222; border-bottom: 2px solid #f0f0f0; padding-bottom: 5px; margin-top: 25px;"">Monitoring konkurencji z PriceSafari pozwoli szybko sprawdzić:</h3>
                
                <ul style=""list-style-type: none; padding-left: 0;"">
                    <li style=""padding-bottom: 8px;"">✅ Kto ma najlepszą cenę na danym produkcie?</li>
                    <li style=""padding-bottom: 8px;"">✅ Gdzie w rankingu cenowym znajduje się Państwa oferta?</li>
                    <li style=""padding-bottom: 8px;"">✅ Kto i gdzie promuje oferty?</li>
                    <li style=""padding-bottom: 8px;"">✅ Jak szeroka jest konkurencja?</li>
                    <li style=""padding-bottom: 8px;"">✅ Jaki czas wysyłki oferują poszczególni konkurenci?</li>
                    <li style=""padding-bottom: 8px;"">✅ Które ceny produktów można podnieść, aby maksymalizować zyski?</li>
                    <li style=""padding-bottom: 8px;"">✅ Na jakich produktach wybrany konkurent zmienił wczoraj cenę?</li>
                </ul>

                <div style=""background-color: #f2f9ff; border-left: 4px solid #41C7C7; padding: 15px; margin: 25px 0;"">
                    <p style=""margin: 0; font-weight: bold;"">Co-Pilot Cenowy</p>
                    <p style=""margin: 5px 0 0 0;"">Funkcja ta pozwala masowo wprowadzać zmiany, symulować wyniki i eksportować nowe ceny dla setek produktów kilkoma kliknięciami.</p>
                </div>

                <p>Oferujemy <strong>bezpłatne konto przez 7 dni</strong>. Mogą Państwo przetestować nasz program na <strong>1000 własnych produktów</strong>.</p>
                
                <div style=""text-align: center; margin: 30px 0;"">
                    <a href=""https://pricesafari.pl/Home/Pricing"" style=""background-color: #41C7C7; color: #ffffff; padding: 15px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; font-size: 16px; display: inline-block;"">
                       Szybka wycena online
                    </a>
                </div>
                
                <p style=""text-align: center; font-size: 14px; color: #7f8c8d;"">
             Do uruchomienia konta testowego, potrzebujemy jedynie feedu produktowego z Państwa produktami. Następnie, po rozmowie online, przygotujemy dla Państwa konto demo.
                </p>

                <p style=""text-align: center; margin-top: 30px;"">
                    Więcej o PriceSafari 👉 <a href=""https://pricesafari.pl/"" style=""color: #41C7C7; text-decoration: none;"">https://pricesafari.pl/</a>
                </p>
            </div>
        </div>
    ";
    }

    private string GetEmailFooter()
    {
        return @"
    <p>
        Pozdrawiamy,<br />
        <strong>Zespół PriceSafari</strong>
    </p>
    <p>
        Tel.: +48 791 855 755<br />
        <a href=""mailto:biuro@pricesafari.pl"">biuro@pricesafari.pl</a><br />
        <a href=""https:
    </p>
    <p>
        <a href=""https:
            <img src=""cid:signatureImage"" alt=""PriceSafari - Monitoring cen online"" style=""cursor: pointer;"" />
        </a>
    </p>
    <p>
        <strong>Heated Box Polska sp. z o. o.</strong><br />
        Wojciecha Korfantego 16<br />
        42-202 Częstochowa<br />
        NIP 9492247951 &nbsp;&nbsp; REGON 388799620 &nbsp;&nbsp; KRS 0000897972
    </p>
    <p style=""font-size: small; color: gray;"">
        Heated Box Sp. z o.o., z siedzibą w Częstochowie, ul. Wojciecha Korfantego 16, 42-202 Częstochowa, wpisana do rejestru przedsiębiorców Krajowego Rejestru Sądowego prowadzonego przez Sąd Rejonowy w Częstochowie, Wydział Gospodarczy KRS, pod numerem KRS 0000897972, o kapitale zakładowym 100 000,00 zł.<br /><br />

        Treść niniejszej wiadomości może być poufna i objęta zakazem jej ujawniania. Jeśli czytelnik niniejszej wiadomości nie jest jej zamierzonym adresatem, pracownikiem lub pośrednikiem upoważnionym do jej przekazania adresatowi, niniejszym informujemy, że wszelkie rozprowadzanie, dystrybucja lub powielanie niniejszej wiadomości jest zabronione. Jeżeli otrzymałeś tę wiadomość omyłkowo, prosimy bezzwłocznie zawiadomić nadawcę, wysyłając odpowiedź na niniejszą wiadomość, i usunąć ją z poczty.<br /><br />

        Niniejsza informacja została przesłana przez Heated Box Sp. z o.o. z siedzibą w Częstochowie w oparciu o dane dostępne publicznie i nie stanowi oferty marketingowej w rozumieniu art. 66 Kodeksu cywilnego.<br /><br />

        W przypadku zainteresowania podjęciem współpracy, przesyłając odpowiedź na ten e-mail, wyrażacie Państwo zgodę na włączenie danych Państwa firmy oraz osób kontaktowych, działających w jej imieniu, do zbioru danych, których administratorem jest Heated Box Sp. z o.o., w celu kontynuacji kontaktu z Państwem – w tym wysyłania materiałów o charakterze marketingowym. Dane będą wykorzystywane w celach marketingu usług własnych oraz usług podmiotów współpracujących z Heated Box Sp. z o.o. Dane mogą być powierzane lub przekazywane podmiotom współpracującym z Heated Box Sp. z o.o. w celach marketingowych.<br /><br />

        W każdym momencie możecie Państwo skontaktować się z administratorem pod adresem biuro@pricesafari.pl w celu uzyskania informacji o zakresie przetwarzanych danych, dokonania ich sprostowania lub uzupełnienia, jak również zażądania zaprzestania ich przetwarzania. Szersze informacje o sposobie przetwarzania danych przez Heated Box Sp. z o.o. oraz o Państwa uprawnieniach znajdziecie Państwo na stronie internetowej: <a href=""https:
    </p>";
    }

}