using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Models;

namespace PriceSafari.VSA.MassExporter
{
    [Authorize(Roles = "Admin, Manager, Member")]
    [Route("api/[controller]")]
    [ApiController]
    public class MassExportController : ControllerBase
    {
        private readonly IMassExportService _exportService;
        private readonly UserManager<PriceSafariUser> _userManager;

        public MassExportController(IMassExportService exportService, UserManager<PriceSafariUser> userManager)
        {
            _exportService = exportService;
            _userManager = userManager;
        }

        [HttpPost("ExportMultiScraps")]
        public async Task<IActionResult> ExportMultiScraps([FromQuery] int storeId, [FromBody] ExportMultiRequest request)
        {
            if (request?.ScrapIds == null || !request.ScrapIds.Any())
                return BadRequest("Nie wybrano żadnych analiz.");

            if (request.ScrapIds.Count > 30)
                return BadRequest("Maksymalnie 30 analiz.");

            var userId = _userManager.GetUserId(User);

            try
            {
                var (fileContent, fileName, contentType) = await _exportService.GenerateExportAsync(storeId, request, userId);
                return File(fileContent, contentType, fileName);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("GetAvailableScraps")]
        public async Task<IActionResult> GetAvailableScraps([FromQuery] int storeId, [FromQuery] string sourceType = "comparison")
        {
            var userId = _userManager.GetUserId(User);

            try
            {
                var result = await _exportService.GetAvailableScrapsAsync(storeId, userId, sourceType);
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}