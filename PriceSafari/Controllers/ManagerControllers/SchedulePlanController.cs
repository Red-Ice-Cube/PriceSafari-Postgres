using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
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
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Pobieramy PIERWSZY plan (lub Single jeśli masz pewność, że jest jeden).
            var plan = await _context.SchedulePlans
                .Include(sp => sp.Monday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Friday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks)
                .FirstOrDefaultAsync();

            // Jeżeli brak planów w bazie:
            if (plan == null)
            {
                // Możesz przekierować do Create, albo wyświetlić pusty widok z komunikatem:
                return View("~/Views/ManagerPanel/SchedulePlan/NoPlans.cshtml");
            }

            // Przekazujemy obiekt planu do widoku
            return View("~/Views/ManagerPanel/SchedulePlan/Index.cshtml", plan);
        }


        // GET: /SchedulePlan/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View("~/Views/ManagerPanel/SchedulePlan/Create.cshtml");
        }

        // POST: /SchedulePlan/Create
        [HttpPost]
        public async Task<IActionResult> CreatePost()
        {
            // Tu nie pobieramy nazwy, bo nie mamy planName.
            // Po prostu tworzymy nowy plan z 7 DayDetail.

            // 1) Tworzymy plan
            var plan = new SchedulePlan();
            _context.SchedulePlans.Add(plan);
            await _context.SaveChangesAsync();

            // 2) Tworzymy 7 DayDetail
            var mon = new DayDetail();
            var tue = new DayDetail();
            var wed = new DayDetail();
            var thu = new DayDetail();
            var fri = new DayDetail();
            var sat = new DayDetail();
            var sun = new DayDetail();

            _context.DayDetails.AddRange(mon, tue, wed, thu, fri, sat, sun);
            await _context.SaveChangesAsync();

            // 3) Ustawiamy w planie
            plan.MondayId = mon.Id;
            plan.TuesdayId = tue.Id;
            plan.WednesdayId = wed.Id;
            plan.ThursdayId = thu.Id;
            plan.FridayId = fri.Id;
            plan.SaturdayId = sat.Id;
            plan.SundayId = sun.Id;

            _context.SchedulePlans.Update(plan);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: /SchedulePlan/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // Wczytujemy 7 dni + ich zadania
            var plan = await _context.SchedulePlans
                .Include(sp => sp.Monday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Friday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks)
                .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks)
                .FirstOrDefaultAsync(sp => sp.Id == id);

            if (plan == null)
                return NotFound();

            return View("~/Views/ManagerPanel/SchedulePlan/Edit.cshtml", plan);
        }

        // POST: /SchedulePlan/Delete/5
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.SchedulePlans.FindAsync(id);
            if (plan == null)
                return NotFound();

            _context.SchedulePlans.Remove(plan);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ========================== DODAWANIE / EDYCJA / USUWANIE TASKÓW ==========================


        [HttpPost]
        public async Task<IActionResult> AddTask(AddTaskViewModel model)
        {
            // 1) Walidacja StartTime (jak było)
            if (!TimeSpan.TryParse(model.StartTime, out var parsedTime))
            {
                ModelState.AddModelError("StartTime", "Zły format godziny (HH:mm)");
            }

            // 2) Walidacja CompletedAt (jeśli wypełnione)
            DateTime? completedAtDate = null;
            if (!string.IsNullOrWhiteSpace(model.CompletedAt))
            {
                // Spróbujmy sparsować
                if (DateTime.TryParse(model.CompletedAt, out var parsedDateTime))
                {
                    completedAtDate = parsedDateTime;
                }
                else
                {
                    ModelState.AddModelError("CompletedAt", "Zły format daty/godziny (yyyy-MM-dd HH:mm).");
                }
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/ManagerPanel/SchedulePlan/AddTask.cshtml", model);
            }

            // 3) Tworzymy obiekt
            var newTask = new ScheduleTask
            {
                DayDetailId = model.DayDetailId,
                StartTime = parsedTime,
                BaseEnabled = model.BaseEnabled,
                UrlEnabled = model.UrlEnabled,
                GoogleEnabled = model.GoogleEnabled,
                CeneoEnabled = model.CeneoEnabled,

                TaskComplete = model.TaskComplete,
                CompletedAt = completedAtDate
            };

            // 4) Zapis
            _context.ScheduleTasks.Add(newTask);
            await _context.SaveChangesAsync();

            // 5) Przekierowanie
            var plan = await FindPlanByDayDetailId(newTask.DayDetailId);
            return RedirectToAction("Edit", new { id = plan.Id });
        }



        [HttpGet]
        public async Task<IActionResult> EditTask(int taskId)
        {
            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null)
                return NotFound();

            var vm = new AddTaskViewModel
            {
                TaskId = task.Id,
                DayDetailId = task.DayDetailId,
                StartTime = task.StartTime.ToString(@"hh\:mm"),
                BaseEnabled = task.BaseEnabled,
                UrlEnabled = task.UrlEnabled,
                GoogleEnabled = task.GoogleEnabled,
                CeneoEnabled = task.CeneoEnabled
            };

            return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> EditTask(AddTaskViewModel model)
        {
            if (!TimeSpan.TryParse(model.StartTime, out var parsedTime))
            {
                ModelState.AddModelError("StartTime", "Zły format godziny (HH:mm)");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/ManagerPanel/SchedulePlan/EditTask.cshtml", model);
            }

            var task = await _context.ScheduleTasks.FindAsync(model.TaskId);
            if (task == null)
                return NotFound();

            task.StartTime = parsedTime;
            task.BaseEnabled = model.BaseEnabled;
            task.UrlEnabled = model.UrlEnabled;
            task.GoogleEnabled = model.GoogleEnabled;
            task.CeneoEnabled = model.CeneoEnabled;

            _context.ScheduleTasks.Update(task);
            await _context.SaveChangesAsync();

            var plan = await FindPlanByDayDetailId(task.DayDetailId);
            return RedirectToAction("Edit", new { id = plan.Id });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTask(int taskId)
        {
            var task = await _context.ScheduleTasks.FindAsync(taskId);
            if (task == null)
                return NotFound();

            var dayDetailId = task.DayDetailId;
            _context.ScheduleTasks.Remove(task);
            await _context.SaveChangesAsync();

            var plan = await FindPlanByDayDetailId(dayDetailId);
            return RedirectToAction("Edit", new { id = plan.Id });
        }

        // ========================== PRZYPISYWANIE SKLEPÓW DO ZADANIA ==========================

        [HttpGet]
        public async Task<IActionResult> AssignStores(int taskId)
        {
            var task = await _context.ScheduleTasks
                .Include(t => t.TaskStores)
                .ThenInclude(ts => ts.Store)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return NotFound();

            var allStores = await _context.Stores.ToListAsync();

            var vm = new AssignStoresViewModel
            {
                TaskId = task.Id,
                StoreItems = allStores.Select(s => new StoreAssignItem
                {
                    StoreId = s.StoreId,
                    StoreName = s.StoreName,
                    IsSelected = task.TaskStores.Any(ts => ts.StoreId == s.StoreId)
                }).ToList()
            };

            return View("~/Views/ManagerPanel/SchedulePlan/AssignStores.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> AssignStores(AssignStoresViewModel model)
        {
            var task = await _context.ScheduleTasks
                .Include(t => t.TaskStores)
                .FirstOrDefaultAsync(t => t.Id == model.TaskId);

            if (task == null)
                return NotFound();

            foreach (var item in model.StoreItems)
            {
                bool alreadyHas = task.TaskStores.Any(ts => ts.StoreId == item.StoreId);

                if (item.IsSelected && !alreadyHas)
                {
                    _context.ScheduleTaskStores.Add(new ScheduleTaskStore
                    {
                        ScheduleTaskId = task.Id,
                        StoreId = item.StoreId
                    });
                }
                else if (!item.IsSelected && alreadyHas)
                {
                    var existing = task.TaskStores.First(ts => ts.StoreId == item.StoreId);
                    _context.ScheduleTaskStores.Remove(existing);
                }
            }

            await _context.SaveChangesAsync();

            var plan = await FindPlanByDayDetailId(task.DayDetailId);
            return RedirectToAction("Edit", new { id = plan.Id });
        }

        // ========================== METODA POMOCNICZA ==========================

        private async Task<SchedulePlan> FindPlanByDayDetailId(int dayDetailId)
        {
            // Znajduje plan, w którym dayDetailId = MondayId / TuesdayId / ...
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
