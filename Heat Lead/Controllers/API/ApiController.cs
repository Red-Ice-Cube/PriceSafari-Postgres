using Ganss.Xss;
using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Heat_Lead.Controllers.API
{
    [ApiController]
    [Route("api/settings")]
    public class ApiController : Controller
    {
        private readonly Heat_LeadContext _context;

        public ApiController(Heat_LeadContext context)
        {
            _context = context;
        }

       

        [HttpGet("Fp-col")]
        public async Task<ActionResult<bool>> CollectFingerPrintSettings()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings != null)
            {
                return Ok(settings.CollectFingerPrint);
            }

            return NotFound();
        }

        [HttpGet("JS-CAN-get")]
        public async Task<IActionResult> GetAffiliateLinkUrl(string secCan)
        {
            var canvasJs = await _context.CanvasJS
                .Include(c => c.Style)
                .Include(c => c.AffiliateLink)
                .FirstOrDefaultAsync(c => c.SecCan == secCan);

            if (canvasJs == null || canvasJs.AffiliateLink == null)
                return NotFound();

            var defaultStyle = new
            {
                buttonColor = "#0E7E87",
                buttonTextColor = "#FFFFFF",
                buttonText = "Sprawdź w sklepie",

                frameColor = "#E8E8E8",
                frameTextColor = "#000000",
                frameText = canvasJs.ProductName ?? "Nazwa produktu",

                productImage = canvasJs.ProductImage ?? "default-image.png",
                extraText = canvasJs.ProductPrice?.ToString("C") ?? "Cena nieznana",
                extraTextColor = "#00B012"
            };

            var result = new
            {
                url = canvasJs.AffiliateLink.AffiliateURL,
                style = canvasJs.Style != null ? new
                {
                    buttonColor = canvasJs.Style.ButtonColor,
                    buttonTextColor = canvasJs.Style.ButtonTextColor,
                    buttonText = canvasJs.Style.ButtonText,

                    frameColor = canvasJs.Style.FrameColor,
                    frameTextColor = canvasJs.Style.FrameTextColor,
                    frameText = canvasJs.ProductName,

                    productImage = canvasJs.ProductImage,
                    extraText = canvasJs.ProductPrice?.ToString("C"),
                    extraTextColor = canvasJs.Style.ExtraTextColor
                } : defaultStyle
            };

            return Ok(result);
        }
    }

}
