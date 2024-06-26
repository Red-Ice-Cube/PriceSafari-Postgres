using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Heat_Lead.Models.ViewModels.NewsViewModel;

namespace Heat_Lead.Controllers
{
    [Authorize(Roles = "Member")]
    public class NewsController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public NewsController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var news = await _context.News.ToListAsync();

            var newsIndexViewModel = news.Select(p => new NewsIndexViewModel
            {
                NewsId = p.NewsId,
                Title = p.Title,
                Message = p.Message,
                GraphicUrl = p.GraphicUrl,
                CreationDate = p.CreationDate,
            }).ToList();

            var model = new NewsViewModel
            {
                NewsIndex = newsIndexViewModel
            };

            return View("~/Views/Panel/News/Index.cshtml", model);
        }
    }
}