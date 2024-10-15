using Microsoft.AspNetCore.Mvc;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Route("keepalive")]
    public class KeepAliveController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return Ok("Alive");
        }
    
    }
}
