using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
    public class StoreAssignmentController : Controller
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly PriceSafariContext _context;
        private readonly ILogger<StoreAssignmentController> _logger;

        public StoreAssignmentController(UserManager<PriceSafariUser> userManager, RoleManager<IdentityRole> roleManager, PriceSafariContext context, ILogger<StoreAssignmentController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> UserStore()
        {
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync("Member");
            var users = usersInMemberRole.ToList();

            var model = users.Select(user => new UserStoresViewModel
            {
                UserId = user.Id,
                FullName = $"{user.PartnerName} {user.PartnerSurname}",
                Stores = _context.UserStores
                    .Where(us => us.UserId == user.Id)
                    .Select(us => us.StoreClass)
                    .ToList(),
                AccesToViewSafari = user.AccesToViewSafari,
                AccesToCreateSafari = user.AccesToCreateSafari,
                AccesToViewMargin = user.AccesToViewMargin,
                AccesToSetMargin = user.AccesToSetMargin
            }).ToList();

            return View("~/Views/ManagerPanel/Affiliates/UserStore.cshtml", model);
        }


        [HttpGet]
        public async Task<IActionResult> AssignStores(string userId)
        {
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync("Member");
            var users = usersInMemberRole.ToList();
            var stores = await _context.Stores.ToListAsync();

            var model = new AssignStoresViewModel
            {
                Users = users,
                Stores = stores,
                SelectedUserId = userId
            };

            if (!string.IsNullOrEmpty(userId))
            {
                var selectedUserStores = _context.UserStores
                    .Where(us => us.UserId == userId)
                    .Select(us => us.StoreId)
                    .ToList();

                model.SelectedStoreIds = selectedUserStores;

                // Retrieve the user's current permission settings
                var selectedUser = await _userManager.FindByIdAsync(userId);
                if (selectedUser != null)
                {
                    model.AccesToViewSafari = selectedUser.AccesToViewSafari;
                    model.AccesToCreateSafari = selectedUser.AccesToCreateSafari;
                    model.AccesToViewMargin = selectedUser.AccesToViewMargin;
                    model.AccesToSetMargin = selectedUser.AccesToSetMargin;
                }
            }

            return View("~/Views/ManagerPanel/Affiliates/AssignStores.cshtml", model);
        }


        [HttpPost]
        public async Task<IActionResult> AssignStores(AssignStoresViewModel model)
        {
            _logger.LogInformation("POST AssignStores action triggered");

            if (ModelState.IsValid)
            {
                _logger.LogInformation("Model state is valid");

                // Update user stores
                var userStores = _context.UserStores.Where(us => us.UserId == model.SelectedUserId).ToList();

                foreach (var userStore in userStores)
                {
                    if (!model.SelectedStoreIds.Contains(userStore.StoreId))
                    {
                        _context.UserStores.Remove(userStore);
                    }
                }

                foreach (var storeId in model.SelectedStoreIds)
                {
                    if (!userStores.Any(us => us.StoreId == storeId))
                    {
                        _context.UserStores.Add(new PriceSafariUserStore { UserId = model.SelectedUserId, StoreId = storeId });
                    }
                }

                // Update user permissions
                var user = await _userManager.FindByIdAsync(model.SelectedUserId);
                if (user != null)
                {
                    user.AccesToViewSafari = model.AccesToViewSafari;
                    user.AccesToCreateSafari = model.AccesToCreateSafari;
                    user.AccesToViewMargin = model.AccesToViewMargin;
                    user.AccesToSetMargin = model.AccesToSetMargin;

                    var result = await _userManager.UpdateAsync(user);
                    if (!result.Succeeded)
                    {
                        _logger.LogError("Failed to update user properties");
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        return View("~/Views/ManagerPanel/Affiliates/AssignStores.cshtml", model);
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Stores and permissions assigned to user successfully");
                return RedirectToAction("UserStore");
            }
            else
            {
                _logger.LogWarning("Model state is invalid");
                foreach (var modelState in ModelState)
                {
                    foreach (var error in modelState.Value.Errors)
                    {
                        _logger.LogError($"Key: {modelState.Key}, Error: {error.ErrorMessage}, Exception: {error.Exception}");
                    }
                }
            }

            // Re-populate users and stores for the view
            var usersInMemberRole = await _userManager.GetUsersInRoleAsync("Member");
            model.Users = usersInMemberRole.ToList();
            model.Stores = await _context.Stores.ToListAsync();

            return View("~/Views/ManagerPanel/Affiliates/AssignStores.cshtml", model);
        }


    }
}
