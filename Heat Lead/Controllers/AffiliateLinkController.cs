using Azure;
using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using Ganss.Xss;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Heat_Lead.Controllers
{
    public class AffiliateLinkController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;
        private readonly IConfiguration _configuration;
        private const string ApiKey = "jefo33RPPjdeojpq0582ojajfwp4j29I3MDAQfje3i";

        public AffiliateLinkController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager, IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        // GET: AffiliateLink
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var links = await _context.AffiliateLink
                .Where(a => a.UserId == user.Id)
                .Include(a => a.Product)
                .Include(a => a.Store)
                .ToListAsync();

            var startDate = DateTime.Now.AddHours(-24);
            var clicks = await _context.AffiliateLinkClick
                .Where(c => c.AffiliateLink.UserId == user.Id && c.ClickTime >= startDate)
                .Include(c => c.AffiliateLink)
                .ToListAsync();

            var clickCounts = clicks
                .GroupBy(c => new
                {
                    c.ClickTime.Year,
                    c.ClickTime.Month,
                    c.ClickTime.Day,
                    c.ClickTime.Hour
                })
                .Select(g => new ClickCountData
                {
                    Date = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, 0, 0),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            var model = new DashboardViewModel
            {
                AffiliateLink = links,
                ClickCount = clickCounts
            };

            if (model.AffiliateLink == null || model.ClickCount == null)
            {
            }

            return RedirectToAction("Index", "Panel");
        }

        [HttpPost("api/link-loaded")]
        [AllowAnonymous]
        public async Task<IActionResult> RecordLinkLoad([FromBody] HeatLeadRequest request, [FromHeader(Name = "Authorization")] string authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ") || authorization.Substring("Bearer ".Length).Trim() != ApiKey)
            {
                return Unauthorized("Invalid or missing API key.");
            }
            var link = await _context.AffiliateLink.FirstOrDefaultAsync(l => l.HeatLeadTrackingCode == request.Heatlead);

            if (link == null)
            {
                return NotFound("Link not found");
            }

            var maxOrders = await _context.Settings.FirstOrDefaultAsync();
            if (maxOrders == null)
            {
                return BadRequest("Settings not found");
            }

            link.ClickCount++;
            var hltt = GenerateHLTT();

            var click = new AffiliateLinkClick
            {
                AffiliateLinkId = link.AffiliateLinkId,
                ClickTime = DateTime.Now,
                HLTT = hltt,
                OrdersLeft = maxOrders.OrderPerClick
            };

            _context.AffiliateLinkClick.Add(click);
            await _context.SaveChangesAsync();

            return Ok(new { TTL = maxOrders.TTL, HLTT = hltt });
        }

        private string GenerateHLTT()
        {
            const string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            const int tokenLength = 24;
            var random = new Random();
            var token = new string(Enumerable.Repeat(characters, tokenLength)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            return token;
        }

        public class HeatLeadRequest
        {
            private string _heatlead;

            public string Heatlead
            {
                get => _heatlead;
                set => _heatlead = new HtmlSanitizer().Sanitize(value);
            }
        }

        [HttpPost("api/fingerprint")]
        [AllowAnonymous]
        public async Task<IActionResult> RecordFingerprint([FromBody] FingerprintDataRequest request)
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null || !settings.CollectFingerPrint)
            {
                return StatusCode(403, "Fingerprint collection is currently disabled.");
            }

            if (string.IsNullOrEmpty(request.HeatLeadTrackingCode) || string.IsNullOrEmpty(request.WebGLFingerprint))
            {
                return BadRequest("Necessary data missing in the request.");
            }

            try
            {
                var fingerprint = new FingerprintData
                {
                    CanvasFingerprint = request.CanvasFingerprint,
                    WebGLFingerprint = request.WebGLFingerprint,
                    WebRTCFingerprint = request.WebRTCFingerprint,
                    HeatLeadTrackingCode = request.HeatLeadTrackingCode,
                    ScreenWidth = request.ScreenWidth,
                    ScreenHeight = request.ScreenHeight,
                    PixelCount = request.PixelCount,
                    ColorDepth = request.ColorDepth,
                    DevicePixelRatio = request.DevicePixelRatio,
                    TouchSupport = request.TouchSupport,
                    MaxTouchPoints = request.MaxTouchPoints,
                    BrowserLanguage = request.BrowserLanguage,
                    TimeZone = request.TimeZone,
                    Platform = request.Platform,
                    HLTT = request.HLTT,
                    UserAgent = request.UserAgent,
                    BrowserPlugins = request.BrowserPlugins,
                    HasSessionStorage = request.HasSessionStorage,
                    HasLocalStorage = request.HasLocalStorage,
                    HasIndexedDB = request.HasIndexedDB,
                    HasAddBehavior = request.HasAddBehavior,
                    HasOpenDatabase = request.HasOpenDatabase,
                    LogicalProcessors = request.LogicalProcessors,
                    DeviceMemory = request.DeviceMemory,
                    DetectedFonts = request.DetectedFonts,
                    CameraInfo = request.CameraInfo,
                    CaptureTime = DateTime.Now
                };

                _context.FingerprintData.Add(fingerprint);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Fingerprint data recorded successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Internal Server Error: " + ex.Message);
            }
        }

        public class FingerprintDataRequest
        {
            public string HLTT { get; set; }
            public string HeatLeadTrackingCode { get; set; }

            public string CanvasFingerprint { get; set; }
            public string WebGLFingerprint { get; set; }
            public string WebRTCFingerprint { get; set; }
            public string BrowserLanguage { get; set; }
            public string TimeZone { get; set; }
            public string Platform { get; set; }
            public string UserAgent { get; set; }
            public string BrowserPlugins { get; set; }
            public string DetectedFonts { get; set; }
            public string CameraInfo { get; set; }
            public int ScreenWidth { get; set; }
            public int ScreenHeight { get; set; }
            public int PixelCount { get; set; }
            public int ColorDepth { get; set; }
            public int LogicalProcessors { get; set; }
            public int DeviceMemory { get; set; }
            public int MaxTouchPoints { get; set; }
            public float DevicePixelRatio { get; set; }
            public bool TouchSupport { get; set; }
            public bool HasSessionStorage { get; set; }
            public bool HasLocalStorage { get; set; }
            public bool HasIndexedDB { get; set; }
            public bool HasAddBehavior { get; set; }
            public bool HasOpenDatabase { get; set; }
        }
    }
}