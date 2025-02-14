using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.Models.ViewModels.SchedulePlanViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
    public class SchedulePlanController : Controller
    {
        private readonly PriceSafariContext _context;

        public SchedulePlanController(PriceSafariContext context)
        {
            _context = context;
        }

        // GET: /SchedulePlan/Index
        // Wyświetla kalendarz (1 plan z 7 dniami).
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Pobieramy PIERWSZY (i jedyny) plan
            var plan = await _context.SchedulePlans
                .Include(sp => sp.Monday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Friday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks)
                .FirstOrDefaultAsync();

            if (plan == null)
            {
                // Jeśli w bazie nie ma planu, możesz przekierować do Create
                return Content("Brak planu w bazie. Utwórz lub zainicjuj plan.");
            }

            // Przekazujemy do widoku
            return View("~/Views/ManagerPanel/SchedulePlan/Index.cshtml", plan);
        }

        // Tworzenie planu (jeden raz) - jeżeli chcesz w panelu
        [HttpPost]
        public async Task<IActionResult> CreatePlan()
        {
            // Tworzymy plan i 7 DayDetail, jeśli w ogóle chcesz to robić z poziomu panelu
            var plan = new SchedulePlan();
            _context.SchedulePlans.Add(plan);
            await _context.SaveChangesAsync();

            var mon = new DayDetail();
            var tue = new DayDetail();
            var wed = new DayDetail();
            var thu = new DayDetail();
            var fri = new DayDetail();
            var sat = new DayDetail();
            var sun = new DayDetail();

            _context.DayDetails.AddRange(mon, tue, wed, thu, fri, sat, sun);
            await _context.SaveChangesAsync();

            plan.MondayId = mon.Id;
            plan.TuesdayId = tue.Id;
            plan.WednesdayId = wed.Id;
            plan.ThursdayId = thu.Id;
            plan.FridayId = fri.Id;
            plan.SaturdayId = sat.Id;
            plan.SundayId = sun.Id;

            _context.SchedulePlans.Update(plan);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // GET: /SchedulePlan/AddTask?dayDetailId=X&hour=Y&min=Z
        [HttpGet]
        public IActionResult AddTask(int dayDetailId, int hour, int min)
        {
            // Walidacja hour i min
            var h = Math.Clamp(hour, 0, 23);
            var m = Math.Clamp(min, 0, 59);

            // Pobieramy sklepy z bazy, by zrobić checkboxy
            var allStores = _context.Stores.ToList();

            var vm = new AddTaskViewModel
            {
                SessionName = "",
                StartTime = $"{h:00}:{m:00}", // domyślna wartość
                Stores = allStores.Select(s => new StoreCheckboxItem
                {
                    StoreId = s.StoreId,
                    StoreName = s.StoreName,
                    IsSelected = false
                }).ToList()
            };

            // Przechowujemy dayDetailId w ViewBag lub w hidden input w widoku
            ViewBag.DayDetailId = dayDetailId;

            return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", vm);
        }

        // POST: /SchedulePlan/AddTask
        [HttpPost]
        public async Task<IActionResult> AddTask(int dayDetailId, AddTaskViewModel model)
        {
            // Parsujemy StartTime
            if (!TimeSpan.TryParse(model.StartTime, out var parsedTime))
            {
                ModelState.AddModelError("StartTime", "Nieprawidłowy format godziny (HH:mm).");
            }

            // Parsujemy CompletedAt (opcjonalnie)
            DateTime? completedAtDate = null;
            if (!string.IsNullOrWhiteSpace(model.CompletedAt))
            {
                if (DateTime.TryParse(model.CompletedAt, out var cdt))
                {
                    completedAtDate = cdt;
                }
                else
                {
                    ModelState.AddModelError("CompletedAt", "Nieprawidłowy format daty/godziny.");
                }
            }

            if (!ModelState.IsValid)
            {
                // Musimy odtworzyć checkboxy sklepów
                var allStores = _context.Stores.ToList();

                // Dla każdego sklepu z bazy - sprawdzamy czy user miał zaznaczone (po ID)
                model.Stores = allStores.Select(dbStore =>
                {
                    var found = model.Stores.FirstOrDefault(x => x.StoreId == dbStore.StoreId);
                    return new StoreCheckboxItem
                    {
                        StoreId = dbStore.StoreId,
                        StoreName = dbStore.StoreName,
                        IsSelected = found?.IsSelected ?? false
                    };
                }).ToList();

                ViewBag.DayDetailId = dayDetailId;
                return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", model);
            }

            // Tworzymy obiekt ScheduleTask
            var newTask = new ScheduleTask
            {
                SessionName = model.SessionName,
                StartTime = parsedTime,
                BaseEnabled = model.BaseEnabled,
                UrlEnabled = model.UrlEnabled,
                GoogleEnabled = model.GoogleEnabled,
                CeneoEnabled = model.CeneoEnabled,
                CompletedAt = completedAtDate,

                DayDetailId = dayDetailId
            };
            _context.ScheduleTasks.Add(newTask);
            await _context.SaveChangesAsync();

            // Przypisanie sklepów
            var selectedStoreIds = model.Stores
                .Where(x => x.IsSelected)
                .Select(x => x.StoreId)
                .ToList();

            foreach (var storeId in selectedStoreIds)
            {
                var rel = new ScheduleTaskStore
                {
                    ScheduleTaskId = newTask.Id,
                    StoreId = storeId
                };
                _context.ScheduleTaskStores.Add(rel);
            }
            await _context.SaveChangesAsync();

            // Przekierowanie do Index (kalendarz)
            return RedirectToAction("Index");
        }

        // GET: /SchedulePlan/EditTask/5
        [HttpGet]
        public async Task<IActionResult> EditTask(int taskId)
        {
            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null)
                return NotFound();

            var vm = new AddTaskViewModel
            {
                // Możesz dać SessionName do edycji, CompletedAt, itp.:
                SessionName = task.SessionName,
                StartTime = task.StartTime.ToString(@"hh\:mm"),
                BaseEnabled = task.BaseEnabled,
                UrlEnabled = task.UrlEnabled,
                GoogleEnabled = task.GoogleEnabled,
                CeneoEnabled = task.CeneoEnabled,
                CompletedAt = task.CompletedAt?.ToString("yyyy-MM-dd HH:mm") // format dowolny

                // Jeśli chcesz edytować sklepy w tym samym formularzu,
                // musisz odtworzyć "Stores" -> w tym przykładzie pominę
            };

            // ewentualnie dayDetailId w ViewBag
            ViewBag.TaskId = taskId;
            return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", vm);
        }

        // POST: /SchedulePlan/EditTask
        [HttpPost]
        public async Task<IActionResult> EditTask(int taskId, AddTaskViewModel model)
        {
            // Walidacja StartTime
            if (!TimeSpan.TryParse(model.StartTime, out var parsedTime))
            {
                ModelState.AddModelError("StartTime", "Nieprawidłowy format godziny (HH:mm).");
            }

            // Parsowanie CompletedAt
            DateTime? completedAtDate = null;
            if (!string.IsNullOrWhiteSpace(model.CompletedAt))
            {
                if (DateTime.TryParse(model.CompletedAt, out var cdt))
                {
                    completedAtDate = cdt;
                }
                else
                {
                    ModelState.AddModelError("CompletedAt", "Nieprawidłowy format daty/godziny.");
                }
            }

            if (!ModelState.IsValid)
            {
                ViewBag.TaskId = taskId;
                return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", model);
            }

            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null)
                return NotFound();

            task.SessionName = model.SessionName;
            task.StartTime = parsedTime;
            task.BaseEnabled = model.BaseEnabled;
            task.UrlEnabled = model.UrlEnabled;
            task.GoogleEnabled = model.GoogleEnabled;
            task.CeneoEnabled = model.CeneoEnabled;
            task.CompletedAt = completedAtDate;

            // Aktualizujemy
            _context.ScheduleTasks.Update(task);
            await _context.SaveChangesAsync();

            // Przekierowanie do Index
            return RedirectToAction("Index");
        }

        // POST: /SchedulePlan/DeleteTask/5
        [HttpPost]
        public async Task<IActionResult> DeleteTask(int taskId)
        {
            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null) return NotFound();

            _context.ScheduleTasks.Remove(task);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // Metoda pomocnicza, jeśli w przyszłości potrzebujesz odszukać plan
        private async Task<SchedulePlan> FindPlanByDayDetailId(int dayDetailId)
        {
            var plan = await _context.SchedulePlans
                .FirstOrDefaultAsync(sp =>
                       sp.MondayId == dayDetailId
                    || sp.TuesdayId == dayDetailId
                    || sp.WednesdayId == dayDetailId
                    || sp.ThursdayId == dayDetailId
                    || sp.FridayId == dayDetailId
                    || sp.SaturdayId == dayDetailId
                    || sp.SundayId == dayDetailId
                );
            return plan;
        }
    }
}
