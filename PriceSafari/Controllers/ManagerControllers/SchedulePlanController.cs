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


        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Najpierw próbujemy pobrać istniejący plan (ze wszystkimi powiązanymi danymi)
            var plan = await _context.SchedulePlans
                .AsSplitQuery() // dzieli ładowanie Include’ów na kilka zapytań
                .Include(sp => sp.Monday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Friday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .FirstOrDefaultAsync();

            // Jeśli nie ma planu w bazie – tworzymy nowy
            if (plan == null)
            {
                var newPlan = new SchedulePlan
                {
                    Monday = new DayDetail(),
                    Tuesday = new DayDetail(),
                    Wednesday = new DayDetail(),
                    Thursday = new DayDetail(),
                    Friday = new DayDetail(),
                    Saturday = new DayDetail(),
                    Sunday = new DayDetail()
                };

                _context.SchedulePlans.Add(newPlan);
                await _context.SaveChangesAsync(); // Zapis do bazy

                // Na nowo wczytujemy (tym razem już z ID) i wypełniamy powiązania,
                // aby przekazać gotowy obiekt do widoku
                plan = await _context.SchedulePlans
                    .AsSplitQuery()
                    .Include(sp => sp.Monday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Friday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .FirstOrDefaultAsync();
            }

            // Zwracamy widok z załadowanym planem
            return View("~/Views/ManagerPanel/SchedulePlan/Index.cshtml", plan);
        }


        // ========== Tworzenie Zadania ==========

        [HttpGet]
        public IActionResult AddTask(int dayDetailId, int hour, int min)
        {
            // Ustal domyślny StartTime
            var h = Math.Clamp(hour, 0, 23);
            var m = Math.Clamp(min, 0, 59);

            var allStores = _context.Stores.ToList();

            var vm = new AddTaskViewModel
            {
                SessionName = "",
                StartTime = $"{h:00}:{m:00}",   // np. "13:40"
                EndTime = "",
                Stores = allStores
                    .Select(s => new StoreCheckboxItem
                    {
                        StoreId = s.StoreId,
                        StoreName = s.StoreName,
                        IsSelected = false
                    }).ToList()
            };

            ViewBag.DayDetailId = dayDetailId;
            return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> AddTask(int dayDetailId, AddTaskViewModel model)
        {
            // Parsowanie StartTime
            if (!TimeSpan.TryParse(model.StartTime, out var startTs))
            {
                ModelState.AddModelError("StartTime", "Nieprawidłowy format godziny (HH:mm).");
            }
            // Parsowanie EndTime
            if (!TimeSpan.TryParse(model.EndTime, out var endTs))
            {
                ModelState.AddModelError("EndTime", "Nieprawidłowy format godziny (HH:mm).");
            }
            // Walidacja
            if (endTs <= startTs)
            {
                ModelState.AddModelError("EndTime", "Godzina końca musi być późniejsza niż start.");
            }

            if (!ModelState.IsValid)
            {
                // Odtwarzamy listę sklepów
                var allStores = _context.Stores.ToList();
                model.Stores = allStores.Select(s =>
                {
                    var existed = model.Stores.FirstOrDefault(x => x.StoreId == s.StoreId);
                    return new StoreCheckboxItem
                    {
                        StoreId = s.StoreId,
                        StoreName = s.StoreName,
                        IsSelected = existed?.IsSelected ?? false
                    };
                }).ToList();

                ViewBag.DayDetailId = dayDetailId;
                return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", model);
            }

            // Sprawdzenie kolizji (opcjonalnie)
            var dayDetail = await _context.DayDetails
                .Include(d => d.Tasks)
                .FirstOrDefaultAsync(d => d.Id == dayDetailId);

            if (dayDetail == null)
            {
                return RedirectToAction("Index");
            }

            bool collision = dayDetail.Tasks.Any(t =>
                // [startTs, endTs) vs [t.StartTime, t.EndTime)
                (startTs < t.EndTime) && (endTs > t.StartTime)
            );
            if (collision)
            {
                ModelState.AddModelError("", "Przedział czasowy jest już zajęty.");

                // Odtwarzamy checkboxy
                var allStores = _context.Stores.ToList();
                model.Stores = allStores.Select(s =>
                {
                    var existed = model.Stores.FirstOrDefault(x => x.StoreId == s.StoreId);
                    return new StoreCheckboxItem
                    {
                        StoreId = s.StoreId,
                        StoreName = s.StoreName,
                        IsSelected = existed?.IsSelected ?? false
                    };
                }).ToList();

                ViewBag.DayDetailId = dayDetailId;
                return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", model);
            }

            // Tworzymy zadanie
            var newTask = new ScheduleTask
            {
                SessionName = model.SessionName,
                StartTime = startTs,
                EndTime = endTs,
                BaseEnabled = model.BaseEnabled,
                UrlEnabled = model.UrlEnabled,
                GoogleEnabled = model.GoogleEnabled,
                CeneoEnabled = model.CeneoEnabled,
                AleBaseEnabled = model.AleBaseEnabled,
                DayDetailId = dayDetailId
            };
            _context.ScheduleTasks.Add(newTask);
            await _context.SaveChangesAsync();

            // Przypisywanie sklepów
            var selectedStoreIds = model.Stores
                .Where(x => x.IsSelected)
                .Select(x => x.StoreId)
                .ToList();
            foreach (var sid in selectedStoreIds)
            {
                var rel = new ScheduleTaskStore
                {
                    ScheduleTaskId = newTask.Id,
                    StoreId = sid
                };
                _context.ScheduleTaskStores.Add(rel);
            }
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ========== Edycja Zadania ==========

        [HttpGet]
        public async Task<IActionResult> EditTask(int taskId)
        {
            var task = await _context.ScheduleTasks
                .Include(t => t.TaskStores)
                .ThenInclude(ts => ts.Store)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null) return NotFound();

            // Przygotowujemy ViewModel podobny do AddTaskViewModel
            var allStores = _context.Stores.ToList();

            var vm = new AddTaskViewModel
            {
                SessionName = task.SessionName,
                StartTime = task.StartTime.ToString(@"hh\:mm"),
                EndTime = task.EndTime.ToString(@"hh\:mm"),
                BaseEnabled = task.BaseEnabled,
                UrlEnabled = task.UrlEnabled,
                GoogleEnabled = task.GoogleEnabled,
                CeneoEnabled = task.CeneoEnabled,
                AleBaseEnabled = task.AleBaseEnabled,
                Stores = allStores.Select(s => new StoreCheckboxItem
                {
                    StoreId = s.StoreId,
                    StoreName = s.StoreName,
                    IsSelected = task.TaskStores.Any(ts => ts.StoreId == s.StoreId)
                }).ToList()
            };

            ViewBag.TaskId = taskId;
            return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> EditTask(int taskId, AddTaskViewModel model)
        {
            // Walidacja Start/End Time
            if (!TimeSpan.TryParse(model.StartTime, out var startTs))
            {
                ModelState.AddModelError("StartTime", "Błędny format godziny.");
            }
            if (!TimeSpan.TryParse(model.EndTime, out var endTs))
            {
                ModelState.AddModelError("EndTime", "Błędny format godziny.");
            }
            if (endTs <= startTs)
            {
                ModelState.AddModelError("EndTime", "Godzina końca musi być po starcie.");
            }

            if (!ModelState.IsValid)
            {
                // Odtwarzamy checkboxy
                var allStores = _context.Stores.ToList();
                model.Stores = allStores.Select(s => new StoreCheckboxItem
                {
                    StoreId = s.StoreId,
                    StoreName = s.StoreName,
                    IsSelected = model.Stores.Any(x => x.StoreId == s.StoreId && x.IsSelected)
                }).ToList();

                ViewBag.TaskId = taskId;
                return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", model);
            }

            var task = await _context.ScheduleTasks
                .Include(t => t.DayDetail)
                .Include(t => t.TaskStores)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null) return NotFound();

            // Sprawdzenie kolizji w DayDetail
            var dayDetail = await _context.DayDetails
                .Include(d => d.Tasks)
                .FirstOrDefaultAsync(d => d.Id == task.DayDetailId);

            if (dayDetail == null) return NotFound();

            bool collision = dayDetail.Tasks
                .Where(x => x.Id != taskId) // pomijamy edytowany task
                .Any(t =>
                    (startTs < t.EndTime) && (endTs > t.StartTime)
                );
            if (collision)
            {
                ModelState.AddModelError("", "Przedział czasowy już zajęty.");

                // Odtwarzamy checkboxy
                var allStores = _context.Stores.ToList();
                model.Stores = allStores.Select(s => new StoreCheckboxItem
                {
                    StoreId = s.StoreId,
                    StoreName = s.StoreName,
                    IsSelected = model.Stores.Any(x => x.StoreId == s.StoreId && x.IsSelected)
                }).ToList();

                ViewBag.TaskId = taskId;
                return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", model);
            }

            // Aktualizujemy
            task.SessionName = model.SessionName;
            task.StartTime = startTs;
            task.EndTime = endTs;
            task.BaseEnabled = model.BaseEnabled;
            task.UrlEnabled = model.UrlEnabled;
            task.GoogleEnabled = model.GoogleEnabled;
            task.CeneoEnabled = model.CeneoEnabled;
            task.AleBaseEnabled = model.AleBaseEnabled;

            // Aktualizacja sklepów (M:N)
            // 1) Usuwamy te, których już nie zaznaczono
            foreach (var existingRel in task.TaskStores.ToList())
            {
                if (!model.Stores.Any(s => s.IsSelected && s.StoreId == existingRel.StoreId))
                {
                    _context.ScheduleTaskStores.Remove(existingRel);
                }
            }
            // 2) Dodajemy te, których wcześniej nie było
            var newSelected = model.Stores
                .Where(x => x.IsSelected)
                .Select(x => x.StoreId)
                .ToList();
            foreach (var sid in newSelected)
            {
                bool alreadyExists = task.TaskStores.Any(ts => ts.StoreId == sid);
                if (!alreadyExists)
                {
                    _context.ScheduleTaskStores.Add(new ScheduleTaskStore
                    {
                        ScheduleTaskId = task.Id,
                        StoreId = sid
                    });
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        // ========== Usunięcie Zadania ==========

        [HttpPost]
        public async Task<IActionResult> DeleteTask(int taskId)
        {
            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null)
                return NotFound();

            _context.ScheduleTasks.Remove(task);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}
