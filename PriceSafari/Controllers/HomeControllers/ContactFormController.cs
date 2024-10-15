using Microsoft.AspNetCore.Mvc;

namespace PriceSafari.Controllers.HomeControllers
{
    public class ContactFormController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
