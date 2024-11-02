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

                await _emailSender.SendEmailAsync(client.CeneoProfileEmail, model.EmailSubject, emailBody);

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
        return @"Szanowni Państwo,

        Monitorujemy ceny produktów w różnych sklepach i znaleźliśmy {ProductCount} produktów w Państwa sklepie.

        Chcielibyśmy przedstawić naszą ofertę monitorowania cen konkurencji dla tych produktów.

        Prosimy o kontakt, aby omówić szczegóły."
        ;
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
        <img src=""cid:signatureImage"" alt=""Podpis"" />
    </p>
    <p>
        <strong>Heated Box Polska sp. z o. o.</strong><br />
        Wojciecha Korfantego 16<br />
        42-202 Częstochowa<br />
        NIP 9492247951 &nbsp;&nbsp; REGON 388799620 &nbsp;&nbsp; KRS 0000897972
    </p>
    <p>
        <em>Poufność:</em> Treść tej wiadomości jest poufna i prawnie chroniona...
    </p>";
    }



}
