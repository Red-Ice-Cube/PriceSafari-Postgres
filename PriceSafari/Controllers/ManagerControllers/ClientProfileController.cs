using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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

    public ClientProfileController(PriceSafariContext context, UserManager<PriceSafariUser> userManager, ILogger<ClientProfileController> logger)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
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


}
