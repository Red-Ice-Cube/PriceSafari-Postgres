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
                    .ToList()
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
            }

            return View("~/Views/ManagerPanel/Affiliates/AssignStores.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignStores(string SelectedUserId, List<int> SelectedStoreIds)
        {
            _logger.LogInformation("POST AssignStores action triggered");

            if (ModelState.IsValid)
            {
                _logger.LogInformation("Model state is valid");

                var userStores = _context.UserStores.Where(us => us.UserId == SelectedUserId).ToList();

                foreach (var userStore in userStores)
                {
                    if (!SelectedStoreIds.Contains(userStore.StoreId))
                    {
                        _context.UserStores.Remove(userStore);
                    }
                }

                foreach (var storeId in SelectedStoreIds)
                {
                    if (!userStores.Any(us => us.StoreId == storeId))
                    {
                        _context.UserStores.Add(new PriceSafariUserStore { UserId = SelectedUserId, StoreId = storeId });
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Stores assigned to users successfully");
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

            var usersInMemberRole = await _userManager.GetUsersInRoleAsync("Member");
            var users = usersInMemberRole.ToList();
            var stores = await _context.Stores.ToListAsync();

            var viewModel = new AssignStoresViewModel
            {
                Users = users,
                Stores = stores,
                SelectedUserId = SelectedUserId,
                SelectedStoreIds = SelectedStoreIds
            };

            return View("~/Views/ManagerPanel/Affiliates/AssignStores.cshtml", viewModel);
        }

    }
}
