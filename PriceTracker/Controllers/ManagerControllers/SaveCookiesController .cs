using Microsoft.AspNetCore.Mvc;
using PuppeteerSharp;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PriceTracker.Controllers
{
    [Route("save-cookies")]
    public class SaveCookiesController : Controller
    {
        [HttpPost]
        public async Task<IActionResult> SaveCookies([FromBody] List<CookieParam> cookies)
        {
            var cookiesJson = System.Text.Json.JsonSerializer.Serialize(cookies);
            await System.IO.File.WriteAllTextAsync("cookies.json", cookiesJson);
            return Ok();
        }
    }
}
