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

        // 1. Pobieramy szablony z bazy danych
        var templates = await _context.EmailTemplates.ToListAsync();

        // 2. Pobieramy pierwszy domyślny szablon (jeśli istnieje)
        var defaultTemplate = templates.FirstOrDefault();

        var sendEmailViewModel = new SendEmailViewModel
        {
            Clients = clients,
            SelectedClientIds = selectedClientIds,
            AvailableTemplates = templates, // Przekazujemy listę do dropdowna w widoku

            // Ustawiamy wartości domyślne z pierwszego szablonu w bazie
            SelectedMailType = defaultTemplate?.Id ?? 0,
            EmailSubject = defaultTemplate?.Subject ?? "",
            EmailContent = defaultTemplate?.Content ?? ""
        };

        return View("~/Views/ManagerPanel/ClientProfiles/PrepareEmails.cshtml", sendEmailViewModel);
    }

    [HttpPost]
    public async Task<IActionResult> ChangeMailTypeAjax(int mailType, List<int> selectedClientIds)
    {
        // 1. Szukamy szablonu w bazie po ID (mailType to teraz ID szablonu)
        var template = await _context.EmailTemplates.FindAsync(mailType);

        if (template != null)
        {
            // Zwracamy treść oraz temat z bazy
            return Json(new { content = template.Content, subject = template.Subject });
        }

        return Json(new { content = "", subject = "" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSendEmails(SendEmailViewModel model)
    {
        if (model.SelectedClientIds == null || !model.SelectedClientIds.Any())
        {
            ModelState.AddModelError("", "Nie wybrano żadnych klientów.");
            // Musimy ponownie załadować szablony, jeśli wracamy do widoku z błędem
            model.AvailableTemplates = await _context.EmailTemplates.ToListAsync();
            return View("PrepareEmails", model);
        }

        if (string.IsNullOrWhiteSpace(model.EmailSubject))
        {
            ModelState.AddModelError("", "Temat emaila nie może być pusty.");
            model.AvailableTemplates = await _context.EmailTemplates.ToListAsync();
            return View("PrepareEmails", model);
        }

        // 1. Bierzemy treść BEZPOŚREDNIO z edytora (model.EmailContent)
        // Usunęliśmy stary switch/case i hardcodowane metody.
        string baseContent = model.EmailContent;

        if (string.IsNullOrWhiteSpace(baseContent))
        {
            ModelState.AddModelError("", "Treść wiadomości jest pusta.");
            model.AvailableTemplates = await _context.EmailTemplates.ToListAsync();
            return View("PrepareEmails", model);
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

                if (client.CeneoProfileProductCount.HasValue)
                {
                    personalizedContent = personalizedContent.Replace("{ProductCount}", client.CeneoProfileProductCount.ToString());
                }
                else
                {
                    personalizedContent = personalizedContent.Replace("{ProductCount}", "");
                }

                // Stopka zostaje jako metoda pomocnicza (poniżej), 
                // chyba że też chcesz ją przenieść do treści w bazie danych.
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