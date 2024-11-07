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
    public async Task<IActionResult> PrepareEmailsAjax([FromBody] SelectedClientIdsModel model)
    {
        if (model == null || model.SelectedClientIds == null || !model.SelectedClientIds.Any())
        {
            return BadRequest("Nie wybrano żadnych klientów.");
        }

        var selectedClientIds = model.SelectedClientIds;

        // Pobierz wszystkich klientów i filtruj w pamięci
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
            EmailSubject = "", // Użytkownik wprowadzi temat w widoku
            EmailContent = GetDefaultEmailContent() // Ustawiamy domyślną treść emaila
        };

        // Określamy pełną ścieżkę do widoku
        return PartialView("~/Views/ManagerPanel/ClientProfiles/PrepareEmails.cshtml", sendEmailViewModel);
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

        // Ensure EmailContent is set
        model.EmailContent = GetDefaultEmailContent();

        // Fetch all clients and filter in-memory
        var allClients = await _context.ClientProfiles.ToListAsync();
        var clientsToEmail = allClients.Where(cp => model.SelectedClientIds.Contains(cp.ClientProfileId)).ToList();

        foreach (var client in clientsToEmail)
        {
            try
            {
                var personalizedContent = model.EmailContent
                    .Replace("{ClientName}", client.CeneoProfileName)
                    .Replace("{ProductCount}", client.CeneoProfileProductCount.ToString());

                var emailBody = personalizedContent + GetEmailFooter();

                // Split the emails by comma or semicolon
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
                // Optionally handle errors
            }
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Wiadomości zostały wysłane pomyślnie.";
        return RedirectToAction("Index");
    }



    private string GetDefaultEmailContent()
    {
        return @"
                <p>Dzień dobry,</p>

                <p>Codziennie monitorujemy ceny produktów w e-sklepach.</p>

                <p>Zaindeksowaliśmy <strong>{ProductCount}</strong> produktów dostępnych w Państwa sklepie, które moglibyśmy zacząć monitorować, dostarczając codziennie aktualne raporty rynkowe.</p>

                <p>Namierzymy konkurentów, ich ceny oraz dostępność towarów. Śledzenie zmian dzień po dniu odkryje ich strategie cenowe, takie jak podwyżki przed weekendami czy nadchodzącym Black Weekiem.</p>

                <p>Jak bardzo mogłaby wzrosnąć efektywność Państwa pracy dzięki dostępowi do informacji:</p>

                <ul>
                    <li>Kto ma najlepszą cenę na danym produkcie?</li>
                    <li>Gdzie w rankingu cenowym znajduje się Państwa oferta?</li>
                    <li>Kto i gdzie promuje oferty?</li>
                    <li>Jak szeroka jest konkurencja?</li>
                    <li>Jaki czas wysyłki oferują poszczególni konkurenci?</li>
                    <li>Które ceny produktów można podnieść, aby maksymalizować zyski, wiedząc, że są najtańszymi ofertami na całym rynku?</li>
                    <li>Na jakich produktach wybrany konkurent zmienił wczoraj cenę?</li>
                </ul>

                <p>Strategiczne ceny gwarantują, że Państwa produkty są tak konkurencyjne, jak powinny, a także zapewniają odpowiedni zysk. Nie chodzi tylko o bycie najtańszym, ale także o bycie najmądrzejszym. 
                   Połączenie spostrzeżeń z wydajnością gwarantuje szybsze i pewniejsze decyzje o zmianach cen.</p>

                <p>PriceSafari to pełny wgląd w rynek z poziomu jednego pulpitu.</p>

                <p>Zapraszamy do kontaktu. Oferujemy bezpłatne konto demo, na którym mogą Państwo przetestować nasz program na 300 własnych produktach.</p>
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
        Tel.: +48 514 188 340<br />
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
