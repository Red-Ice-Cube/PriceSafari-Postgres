using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using PriceSafari.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ClientProfileController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public ClientProfileController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpPost]
        public async Task<IActionResult> Create(ClientProfile model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                model.CreatedByUserId = user.Id;
                model.CreationDate = DateTime.Now;

                _context.ClientProfiles.Add(model);
                await _context.SaveChangesAsync();

                return RedirectToAction("Index");
            }

          
            return View("~/Views/ManagerPanel/ClientProfiles/Index.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, ClientStatus status, DateTime? meetingDate = null)
        {
            var clientProfile = await _context.ClientProfiles.FindAsync(id);
            if (clientProfile == null)
            {
                return NotFound();
            }

            clientProfile.Status = status;
            if (status == ClientStatus.UmówionoSpotkanie)
            {
                clientProfile.ScheduledMeetingDate = meetingDate;
            }

            _context.ClientProfiles.Update(clientProfile);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", new { id = clientProfile.ClientProfileId });
        }
    }
}
