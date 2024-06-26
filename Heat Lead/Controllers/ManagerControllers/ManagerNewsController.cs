using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Heat_Lead.Models.ManagerViewModels.ManagerNewsViewModel;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ManagerNewsController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public ManagerNewsController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Wyświetla listę wiadomości
        public async Task<IActionResult> Index()
        {
            var news = _context.News.ToList();

            var managerNewsIndexViewModel = news.Select(p => new ManagerNewsIndexViewModel
            {
                NewsId = p.NewsId,
                Title = p.Title,
                Message = p.Message,
                GraphicUrl = p.GraphicUrl,
                CreationDate = p.CreationDate,
            }).ToList();

            var model = new ManagerNewsViewModel
            {
                NewsIndex = managerNewsIndexViewModel
            };

            return View("~/Views/ManagerPanel/News/Index.cshtml", model);
        }

        // GET: ManagerNews/Create
        public IActionResult Create()
        {
            return View("~/Views/ManagerPanel/News/Create.cshtml");
        }

        // POST: ManagerNews/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Title,Message,GraphicUrl")] ManagerNewsCreateViewModel createModel)
        {
            if (ModelState.IsValid)
            {
                var news = new News
                {
                    Title = createModel.Title,
                    Message = createModel.Message,
                    GraphicUrl = createModel.GraphicUrl,
                    CreationDate = DateTime.Now // Automatycznie ustawiamy datę utworzenia
                };

                _context.News.Add(news);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/News/Create.cshtml", createModel);
        }

        // GET: ManagerNews/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.News == null)
            {
                return NotFound();
            }

            var news = await _context.News.FindAsync(id);
            if (news == null)
            {
                return NotFound();
            }

            var editViewModel = new ManagerNewsEditViewModel
            {
                NewsId = news.NewsId,
                Title = news.Title,
                Message = news.Message,
                GraphicUrl = news.GraphicUrl,
                CreationDate = news.CreationDate
            };

            return View("~/Views/ManagerPanel/News/Edit.cshtml", editViewModel);
        }

        // POST: ManagerNews/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("NewsId,Title,Message,GraphicUrl,CreationDate")] ManagerNewsEditViewModel editModel)
        {
            if (id != editModel.NewsId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var newsToUpdate = await _context.News.FirstOrDefaultAsync(n => n.NewsId == id);
                if (newsToUpdate == null)
                {
                    return NotFound();
                }

                newsToUpdate.Title = editModel.Title;
                newsToUpdate.Message = editModel.Message;
                newsToUpdate.GraphicUrl = editModel.GraphicUrl;
                // Aktualizuj inne właściwości, jeśli są potrzebne

                try
                {
                    _context.Update(newsToUpdate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.News.Any(e => e.NewsId == id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                return RedirectToAction("Index");
            }

            // Ponowna inicjalizacja widoku, jeśli ModelState nie jest prawidłowy
            return View("~/Views/ManagerPanel/News/Edit.cshtml", editModel);
        }

        // Usuwa wiadomość
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var news = await _context.News
                .FirstOrDefaultAsync(m => m.NewsId == id);
            if (news == null)
            {
                return NotFound();
            }

            _context.News.Remove(news);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}