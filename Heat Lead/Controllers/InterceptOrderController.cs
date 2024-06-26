using Ganss.Xss;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Heat_Lead.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InterceptOrderController : ControllerBase
    {
        private readonly Heat_LeadContext _context;
        private readonly HtmlSanitizer _sanitizer;
        private const string ApiKey = "jefo33RPPjdeojpq0582ojajfwp4j29I3MDAQfje3i";

        public InterceptOrderController(Heat_LeadContext context)
        {
            _context = context;
            _sanitizer = new HtmlSanitizer();
        }

        [HttpPost]
        public async Task<IActionResult> PostOrder([FromBody] InterceptOrderRequest request, [FromHeader(Name = "Authorization")] string authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ") || authorization.Substring("Bearer ".Length).Trim() != ApiKey)
            {
                return Unauthorized("Invalid or missing API key.");
            }

            var hltt = _sanitizer.Sanitize(request.HLTT);
            var orderKey = _sanitizer.Sanitize(request.OrderKey);
            var orderId = _sanitizer.Sanitize(request.OrderId);

            var affiliateLinkClick = await _context.AffiliateLinkClick
                .FirstOrDefaultAsync(a => a.HLTT == hltt);

            if (affiliateLinkClick == null)
            {
                return BadRequest("No matching session token found. Order cannot be processed.");
            }

            if (affiliateLinkClick.OrdersLeft <= 0)
            {
                return BadRequest("No more orders can be processed on this token.");
            }

            affiliateLinkClick.OrdersLeft--;

            var interceptOrder = new InterceptOrder
            {
                OrderId = orderId,
                OrderKey = orderKey,
                HLTT = hltt,
                AffiliateLinkId = affiliateLinkClick.AffiliateLinkId,
                OrderDateTime = DateTime.Now
            };

            try
            {
                _context.InterceptOrders.Add(interceptOrder);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Order has been successfully processed." });
            }
            catch (DbUpdateException ex)
            {
                return BadRequest("An error occurred while processing the order: " + ex.Message);
            }
        }
    }

    public class InterceptOrderRequest
    {
        public string OrderId { get; set; }
        public string OrderKey { get; set; }
        public string HLTT { get; set; }
    }
}