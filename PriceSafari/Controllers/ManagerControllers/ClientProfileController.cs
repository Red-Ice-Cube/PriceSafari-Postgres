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
    public JsonResult CheckUrlExists(string url, int? id)
    {
        var query = _context.ClientProfiles.Where(cp => cp.Source == ClientSource.Ceneo && cp.CeneoProfileUrl == url);
        if (id.HasValue)
        {
            query = query.Where(cp => cp.ClientProfileId != id.Value);
        }
        var exists = query.Any();
        return Json(new { exists });
    }

    [HttpGet]
    public async Task<JsonResult> CheckGoogleCompanyNameExists(string companyName, int? id)
    {
        var cleanedCompanyName = CleanCompanyName(companyName);
        var query = _context.ClientProfiles.Where(cp => cp.Source == ClientSource.Google && cp.CeneoProfileName == cleanedCompanyName);
        if (id.HasValue)
        {
            query = query.Where(cp => cp.ClientProfileId != id.Value);
        }
        var exists = await query.AnyAsync();
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

        if (id != model.ClientProfileId)
        {
            return NotFound();
        }

        ModelState.Remove("CreatedByUserId");
        ModelState.Remove("CreatedByUser");

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

        bool exists = false;
        if (model.Source == ClientSource.Ceneo)
        {
            exists = await _context.ClientProfiles
                .AnyAsync(cp => cp.Source == ClientSource.Ceneo && cp.CeneoProfileUrl == model.CeneoProfileUrl && cp.ClientProfileId != id);
            if (exists)
            {
                ModelState.AddModelError("CeneoProfileUrl", "Klient z tym URL już istnieje.");
            }
        }
        else if (model.Source == ClientSource.Google)
        {
            exists = await _context.ClientProfiles
                .AnyAsync(cp => cp.Source == ClientSource.Google && cp.CeneoProfileName == model.CeneoProfileName && cp.CeneoProfileEmail == model.CeneoProfileEmail && cp.ClientProfileId != id);
            if (exists)
            {
                ModelState.AddModelError("CeneoProfileName", "Klient z tą nazwą firmy i adresem e-mail już istnieje.");
            }
        }

        if (ModelState.IsValid)
        {
            try
            {
                var profileToUpdate = await _context.ClientProfiles.FindAsync(id);
                if (profileToUpdate == null)
                {
                    return NotFound();
                }

                profileToUpdate.Source = model.Source;
                profileToUpdate.CeneoProfileName = model.CeneoProfileName;
                profileToUpdate.CeneoProfileEmail = model.CeneoProfileEmail;
                profileToUpdate.Status = model.Status;
                profileToUpdate.ScheduledMeetingDate = model.ScheduledMeetingDate;

                if (model.Source == ClientSource.Ceneo)
                {
                    profileToUpdate.CeneoProfileUrl = model.CeneoProfileUrl;
                    profileToUpdate.CeneoProfileTelephone = model.CeneoProfileTelephone;
                    profileToUpdate.CeneoProfileProductCount = model.CeneoProfileProductCount;
                }
                else
                {
                    profileToUpdate.CeneoProfileUrl = null;
                    profileToUpdate.CeneoProfileTelephone = null;
                    profileToUpdate.CeneoProfileProductCount = null;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Profil klienta został zaktualizowany pomyślnie.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas aktualizowania profilu klienta.");
                ModelState.AddModelError("", "Wystąpił błąd podczas zapisywania danych. Skontaktuj się z administratorem.");
            }
        }

        _logger.LogWarning("ModelState is invalid on Edit: {ModelStateErrors}",
            string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));

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
        // 1. Walidacja (bez zmian)
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

        // ---------------------------------------------------------
        // ZMIANA 1: Pobieranie treści
        // ---------------------------------------------------------

        // Najpierw próbujemy wziąć to, co przyszło z edytora TinyMCE
        string baseContent = model.EmailContent;

        // Jeśli edytor był pusty (zabezpieczenie), generujemy treść z szablonu jak dawniej
        if (string.IsNullOrWhiteSpace(baseContent))
        {
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
        }

        // Pobieranie klientów (bez zmian)
        var allClients = await _context.ClientProfiles.ToListAsync();
        var clientsToEmail = allClients
            .Where(cp => model.SelectedClientIds.Contains(cp.ClientProfileId))
            .ToList();

        foreach (var client in clientsToEmail)
        {
            try
            {
                // ---------------------------------------------------------
                // ZMIANA 2: Podmiana zmiennych (bardziej elastyczna)
                // ---------------------------------------------------------

                var personalizedContent = baseContent
                    .Replace("{ClientName}", client.CeneoProfileName);

                // Zamiast sprawdzać SelectedMailType, po prostu podstawiamy liczbę.
                // Jeśli w treści (HTML) nie ma znacznika {ProductCount}, ta funkcja nic nie zepsuje.
                // A jeśli dodałeś go ręcznie w TinyMCE, to się ładnie podstawi.
                if (client.CeneoProfileProductCount.HasValue)
                {
                    personalizedContent = personalizedContent.Replace("{ProductCount}", client.CeneoProfileProductCount.ToString());
                }
                else
                {
                    // Jeśli klient nie ma liczby produktów, czyścimy znacznik żeby nie straszył
                    personalizedContent = personalizedContent.Replace("{ProductCount}", "");
                }

                // Doklejenie stopki (bez zmian)
                var emailBody = personalizedContent + GetEmailFooter();

                // Wysyłka (bez zmian)
                var emailAddresses = client.CeneoProfileEmail
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToList();

                foreach (var email in emailAddresses)
                {
                    await _emailSender.SendEmailAsync(email, model.EmailSubject, emailBody);
                }

                // Aktualizacja statusu (bez zmian)
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
        <p>Dzień dobry,</p>
        <p>Czy tracą Państwo czas na ręcznym śledzeniu cen konkurencji na <strong>Google Shopping</strong> i <strong>Ceneo</strong>?</p>
        <p>Nasze narzędzie, PriceSafari, automatyzuje proces zbierania cen i pozwala spojrzeć na cały rynek z jednego panelu.</p> 
        <p>Umożliwia to strategiczne zarządzanie cenami, maksymalizację zysków i wyprzedzenie konkurentów.</p>
        <br>
        <p>Dzięki automatycznemu monitoringowi cen konkurencji szybko sprawdzą Państwo:</p>
        <ul>
            <li>Kto ma najlepszą cenę na danym produkcie?</li>
            <li>Gdzie w rankingu cenowym znajduje się Państwa oferta?</li>
            <li>Kto, gdzie i jak promuje swoje oferty?</li>
            <li>Jak szeroka jest realna konkurencja?</li>
            <li>Jakie ceny i czas wysyłki oferują inni?</li>
            <li>Które ceny można bezpiecznie podnieść, aby zwiększyć zysk?</li>
            <li>Jakie ruchy cenowe wykonał wczoraj wybrany konkurent?</li>
        </ul>

        <p>Już pierwsza analiza potrafi przynieść wymierne korzyści. Z doświadczenia naszych klientów wiemy, że w przypadku <strong>7-13% asortymentu</strong> ceny są na tyle niskie, że można je podnieść — <strong>zwiększając zysk, a jednocześnie nadal pozostając najtańszą ofertą na rynku.</strong></p>
        <p><strong>Czy byliby Państwo zainteresowani bezpłatnym przetestowaniem naszego narzędzia na 500 własnych produktach przez 7 dni?</strong></p>
        <p>Jeśli tak, wystarczy krótka odpowiedź na tę wiadomość, a ja prześlę więcej szczegółów i pokażę, jak możemy pomóc w rozwoju Państwa biznesu.</p>

   
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