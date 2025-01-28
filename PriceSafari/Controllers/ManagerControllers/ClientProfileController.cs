using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

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
        return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClientProfile model)
    {
        var user = await _userManager.GetUserAsync(User);

        model.CreatedByUserId = user.Id;
        model.CreationDate = DateTime.Now;

        // Remove these from ModelState to avoid validation errors
        ModelState.Remove("CreatedByUserId");
        ModelState.Remove("CreatedByUser");

        if (ModelState.IsValid)
        {
            // Check if URL already exists
            bool urlExists = await _context.ClientProfiles
                .AnyAsync(cp => cp.CeneoProfileUrl == model.CeneoProfileUrl);

            if (urlExists)
            {
                ModelState.AddModelError("CeneoProfileUrl", "Klient z tym URL już istnieje."); // "A client with this URL already exists."
                return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml", model);
            }

            try
            {
                _context.ClientProfiles.Add(model);
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

        return View("~/Views/ManagerPanel/ClientProfiles/Create.cshtml", model);
    }



    [HttpGet]
    public JsonResult CheckUrlExists(string url)
    {
        var exists = _context.ClientProfiles.Any(cp => cp.CeneoProfileUrl == url);
        return Json(new { exists });
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

        // Remove these from ModelState to avoid validation errors
        ModelState.Remove("CreatedByUserId");
        ModelState.Remove("CreatedByUser");

        if (ModelState.IsValid)
        {
            // Check if URL already exists in another record
            bool urlExists = await _context.ClientProfiles
                .AnyAsync(cp => cp.CeneoProfileUrl == model.CeneoProfileUrl && cp.ClientProfileId != id);

            if (urlExists)
            {
                ModelState.AddModelError("CeneoProfileUrl", "Klient z tym URL już istnieje.");
                return View("~/Views/ManagerPanel/ClientProfiles/Edit.cshtml", model);
            }

            try
            {
                // Update the existing profile with new values
                existingProfile.CeneoProfileName = model.CeneoProfileName;
                existingProfile.CeneoProfileEmail = model.CeneoProfileEmail; // Upewnij się, że to pole jest aktualizowane
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

        // ZWRACAMY PEŁNY WIDOK zamiast partiala!
        return View("~/Views/ManagerPanel/ClientProfiles/PrepareEmails.cshtml", sendEmailViewModel);
    }


    [HttpPost]
    public IActionResult ChangeMailTypeAjax(int mailType, List<int> selectedClientIds)
    {
        // Zdecyduj, czy musisz odczytywać Klientów z bazy,
        // czy wystarczy Ci tylko nowa treść maila
        // Na początek – tylko wypełnimy treść.

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

        // Możesz skorzystać z istniejącego modelu, np. SendEmailViewModel
        // ale nie musisz tworzyć całego widoku, wystarczy np. zwrócić JSON-a:
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

        // Tutaj dynamicznie pobieramy treść w zależności od wybranego maila
        string baseContent;
        switch (model.SelectedMailType)
        {
            case 1:
                // Mail #1 - np. „standardowy” z {ProductCount}
                baseContent = GetEmailContent1();
                break;
            case 2:
                // Mail #2 - inny; np. nie używamy {ProductCount}
                baseContent = GetEmailContent2();
                break;
            case 3:
                // Mail #3 - jeszcze inny
                baseContent = GetEmailContent3();
                break;
            default:
                // Domyślnie mail #1
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

                // Update client status and email tracking properties
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
            <p>Dzień dobry,</p>

            <p>Monitorujemy ceny produktów na Google Shopping, Ceneo i Allegro.</p>

            <p>Na samym Ceneo zaindeksowaliśmy <strong>{ProductCount}</strong> produktów dostępnych w Państwa sklepie, które moglibyśmy zacząć monitorować.</p>

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

            <p>Panel PriceSafari z danymi jednej z zaprzyjaźnionych firm:</p>
            <img src=""https://pricesafari.pl/mail/Panel_PriceSafari.png"" alt=""Panel_PriceSafari"" style=""width: 1400px; height: auto;"" />

            <p>Rozkład cen:</p>
            <img src=""https://pricesafari.pl/mail/Ranking_PriceSafari.png"" alt=""Ranking_PriceSafari"" style=""width: 1400px; height: auto;"" />

            <p>Ceny w czasie:</p>
            <img src=""https://pricesafari.pl/mail/Wykres_PriceSafari.png"" alt=""Wykres_PriceSafari"" style=""width: 1400px; height: auto;"" />

            <p><strong>Raporty cenowe w Europie</strong></p>
            <p>
                Posiadamy też oczywiście możliwość śledzenia ofert na googlu w 16 krajach w EU. 
                Zbieramy wtedy dane ze wszystkich porównywarek cenowych takich jak Zbozi.cz, Heureca.cz czy Idealo i Allegro. 
                Jest to już inna część programu, gdzie nie zbieramy cen każdego dnia tylko robimy taki raport na zlecenie dla wybranych produktów i rynków.
                Nasi klienci wykorzystują takie raporty głównie przed składaniem zamówień u dystrybutorów, ponieważ widać wtedy od razu, czy oferowane ceny są dobre. 
                Zdarzają się często takie przypadki, że np. w Rumunii czy w Czechach możemy znaleźć produkt w o wiele niższej cenie, 
                co może posłużyć jako podstawa do dalszych negocjacji czy zamówienia towaru zza granicy.
            </p>
            <p>
                Raporty to nie pliki excel (jak w konkurencyjnych rozwiązaniach), tylko panel, który automatycznie przelicza ceny 
                z różnych walut po kursach NBP na PLN.
            </p>

            <img src=""https://pricesafari.pl/mail/Eu_PriceSafari.png"" alt=""Eu_PriceSafari"" style=""width: 1400px; height: auto;"" />

            <p>
                Raporty można też wykorzystać w drugą stronę i przeanalizować np. czeski rynek, odkryć na jakich produktach jesteśmy 
                tańsi o 30% od najtańszego Czecha i wejść z ofertą na Allegro.cz.
            </p>
            <img src=""https://pricesafari.pl/mail/Czechy_PriceSafari.png"" alt=""Czechy_PriceSafari"" style=""width: 1400px; height: auto;"" />
            <img src=""https://pricesafari.pl/mail/Produkt_Czechy_PriceSafari.png"" alt=""Produkt_Czechy_PriceSafari"" style=""width: 1400px; height: auto;"" />

            <p>Zapraszamy do kontaktu. Oferujemy bezpłatne konto demo, na którym mogą Państwo przetestować nasz program na 500 własnych produktach.</p>
            <p><strong>Konto demo przygotujemy w kilka godzin.</strong></p>
            ";
    }

    private string GetEmailContent2()
    {
        return @"
        <p>To jest Mail #2 – nie używamy {ProductCount} tutaj.</p>
        <p>Moja alternatywna treść nr 2.</p>
        ...
    ";
    }

    private string GetEmailContent3()
    {
        return @"
        <p>To jest Mail #3 – inny wariant. Możemy też używać {ProductCount}, jeśli chcemy.</p>
        ...
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
        <a href=""https://www.pricesafari.pl"">www.pricesafari.pl</a>
    </p>
    <p>
        <a href=""https://www.pricesafari.pl"">
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

        W każdym momencie możecie Państwo skontaktować się z administratorem pod adresem biuro@pricesafari.pl w celu uzyskania informacji o zakresie przetwarzanych danych, dokonania ich sprostowania lub uzupełnienia, jak również zażądania zaprzestania ich przetwarzania. Szersze informacje o sposobie przetwarzania danych przez Heated Box Sp. z o.o. oraz o Państwa uprawnieniach znajdziecie Państwo na stronie internetowej: <a href=""https://pricesafari.pl/Home/PrivacyPolicy"">https://pricesafari.pl/Home/PrivacyPolicy</a>
    </p>";
    }



}
