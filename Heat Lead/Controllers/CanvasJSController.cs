using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace Heat_Lead.Controllers
{
    [Authorize(Roles = "Member")]
    public class CanvasJSController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public CanvasJSController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index(int affiliateLinkId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var canvas = await _context.CanvasJS.FirstOrDefaultAsync(c => c.AffiliateLinkId == affiliateLinkId);
            if (canvas == null)
            {
                await GenerateScript(affiliateLinkId);
                return RedirectToAction(nameof(Index), new { affiliateLinkId });
            }

            var styles = await _context.CanvasJSStyles.Where(s => s.UserId == user.Id).ToListAsync();

            var canvasViewModel = new CanvasViewModel
            {
                AffiliateLinkId = affiliateLinkId,
                ScriptLink = canvas?.ScriptCode,
                Id = canvas?.CanvasJSId ?? 0,
                AvailableStyles = styles
            };

            return View("~/Views/Panel/Canvas/Index.cshtml", canvasViewModel);
        }

        [HttpGet]
        public async Task<IActionResult> CheckScript(int affiliateLinkId)
        {
            var scripts = await _context.CanvasJS.Where(s => s.AffiliateLinkId == affiliateLinkId).ToListAsync();
            if (!scripts.Any())
            {
                return Json(new { exists = false });
            }

            var results = new List<object>();
            foreach (var script in scripts)
            {
                string? styleName = null;
                if (script.StyleId.HasValue)
                {
                    var style = await _context.CanvasJSStyles.FindAsync(script.StyleId.Value);
                    styleName = style?.CanvaStyleName;
                }

                results.Add(new
                {
                    exists = true,
                    scriptLink = script.ScriptCode,
                    canvasJSId = script.CanvasJSId,
                    styleId = script.StyleId,
                    styleName = styleName
                });
            }

            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetStyles(int canvasJSId)
        {
            var canvas = await _context.CanvasJS
                                       .Include(c => c.AffiliateLink)
                                       .FirstOrDefaultAsync(c => c.CanvasJSId == canvasJSId);

            if (canvas == null)
            {
                return NotFound("Nie znaleziono skryptu.");
            }

            var userId = canvas.AffiliateLink.UserId;

            var styles = await _context.CanvasJSStyles
                .Where(s => s.UserId == userId)
                .ToListAsync();

            return Json(styles.Select(s => new
            {
                s.CanvasJSStyleId,
                s.CanvaStyleName
            }));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCanvasStyle(int canvasJSId, int? styleId)
        {
            var canvas = await _context.CanvasJS.FindAsync(canvasJSId);
            if (canvas == null)
            {
                return NotFound("Skrypt nie znaleziony.");
            }

            canvas.StyleId = styleId;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> GenerateScript(int affiliateLinkId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var affiliateLink = await _context.AffiliateLink
                .Include(a => a.Product)
                .FirstOrDefaultAsync(a => a.AffiliateLinkId == affiliateLinkId && a.UserId == user.Id);

            if (affiliateLink == null)
            {
                return NotFound("Nie znaleziono linku afiliacyjnego.");
            }

            var existingScripts = await _context.CanvasJS
                .Where(c => c.AffiliateLinkId == affiliateLinkId).ToListAsync();

            if (existingScripts.Count >= 3)
            {
                return View("Error", new ErrorViewModel { RequestId = "Skrypty już istnieją dla tego linku." });
            }

            foreach (var scriptName in new[] { "checkstorebutton.min.js", "checkstorebuttonframe.min.js", "checkstorebuttonframeplus.min.js" })
            {
                if (!existingScripts.Any(s => s.ScriptName == scriptName))
                {
                    var secureCanvasId = GenerateSecureCanvasId();
                    var canvasJS = new CanvasJS
                    {
                        AffiliateLinkId = affiliateLinkId,
                        SecCan = secureCanvasId,
                        ScriptName = scriptName,
                        ScriptCode = $"<script src=\"https://eksperci.myjki.com/canvas/{scriptName}\" data-canvas-id=\"{secureCanvasId}\"></script>",
                        ProductImage = affiliateLink.Product?.ProductImage,
                        ProductName = affiliateLink.Product?.ProductName,
                        ProductPrice = affiliateLink.Product?.ProductPrice,
                    };
                    _context.CanvasJS.Add(canvasJS);
                }
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        public static string GenerateSecureCanvasId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        [HttpPost]
        public async Task<IActionResult> EditScript(int canvasJSId, int? styleId)
        {
            var canvasJS = await _context.CanvasJS.FindAsync(canvasJSId);
            if (canvasJS == null)
            {
                return Json(new { success = false, message = "Nie znaleziono skryptu." });
            }

            canvasJS.StyleId = styleId;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Styl zaktualizowany." });
        }

        [HttpGet]
        public async Task<IActionResult> ManageStyles(int affiliateLinkId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var canvas = await _context.CanvasJS
                                        .Include(c => c.AffiliateLink)
                                        .FirstOrDefaultAsync(c => c.AffiliateLinkId == affiliateLinkId && c.AffiliateLink.UserId == user.Id);

            if (canvas == null)
            {
                return NotFound("Nie znaleziono odpowiedniego obiektu CanvasJS dla tego użytkownika.");
            }

            var styles = await _context.CanvasJSStyles
                                        .Where(s => s.UserId == user.Id)
                                        .ToListAsync();

            var viewModel = new ManageStylesViewModel
            {
                Styles = styles,
                AffiliateLinkId = affiliateLinkId,
                ProductName = canvas.ProductName,
                ProductPrice = canvas.ProductPrice,
                ProductImage = canvas.ProductImage
            };

            return View("~/Views/Panel/Canvas/ManageStyles.cshtml", viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> CreateStyle(ManageStylesViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not logged in or does not exist.");
            }

            CanvasJSStyle style;
            if (model.StyleId.HasValue && model.StyleId > 0)
            {
                style = await _context.CanvasJSStyles.FirstOrDefaultAsync(s => s.CanvasJSStyleId == model.StyleId);
                if (style == null)
                    return NotFound("Style not found.");
            }
            else
            {
                style = new CanvasJSStyle();
                _context.CanvasJSStyles.Add(style);
            }

            style.CanvaStyleName = model.Name;
            style.ButtonText = model.ButtonText;
            style.ButtonTextColor = model.ButtonTextColor;
            style.ButtonColor = model.ButtonColor;
            style.FrameTextColor = model.FrameTextColor;
            style.FrameColor = model.FrameColor;
            style.ExtraTextColor = model.FrameExtraTextColor;
            style.UserId = user.Id;

            await _context.SaveChangesAsync();
            return RedirectToAction("ManageStyles", new { affiliateLinkId = model.AffiliateLinkId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStyle(int styleId)
        {
            var style = await _context.CanvasJSStyles.FindAsync(styleId);
            if (style == null)
            {
                return Json(new { success = false, message = "Styl nie został znaleziony." });
            }

            var canvases = await _context.CanvasJS.Where(c => c.StyleId == styleId).ToListAsync();
            foreach (var canvas in canvases)
            {
                canvas.StyleId = null;
            }

            _context.CanvasJSStyles.Remove(style);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Styl został usunięty." });
        }
    }
}