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

            var plan = await _context.SchedulePlans
                .AsSplitQuery()
                .Include(sp => sp.Monday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Friday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                .FirstOrDefaultAsync();

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
                await _context.SaveChangesAsync();

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

            return View("~/Views/ManagerPanel/SchedulePlan/Index.cshtml", plan);
        }

        [HttpGet]
        public IActionResult AddTask(int dayDetailId, int hour, int min)
        {

            var h = Math.Clamp(hour, 0, 23);
            var m = Math.Clamp(min, 0, 59);

            var allStores = _context.Stores.ToList();

            var vm = new AddTaskViewModel
            {
                SessionName = "",
                StartTime = $"{h:00}:{m:00}",
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

            if (!TimeSpan.TryParse(model.StartTime, out var startTs))
            {
                ModelState.AddModelError("StartTime", "Nieprawidłowy format godziny (HH:mm).");
            }

            if (!TimeSpan.TryParse(model.EndTime, out var endTs))
            {
                ModelState.AddModelError("EndTime", "Nieprawidłowy format godziny (HH:mm).");
            }

            if (endTs <= startTs)
            {
                ModelState.AddModelError("EndTime", "Godzina końca musi być późniejsza niż start.");
            }

            if (!ModelState.IsValid)
            {

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

            var dayDetail = await _context.DayDetails
                .Include(d => d.Tasks)
                .FirstOrDefaultAsync(d => d.Id == dayDetailId);

            if (dayDetail == null)
            {
                return RedirectToAction("Index");
            }

            bool collision = dayDetail.Tasks.Any(t =>

                (startTs < t.EndTime) && (endTs > t.StartTime)
            );
            if (collision)
            {
                ModelState.AddModelError("", "Przedział czasowy jest już zajęty.");

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

            var newTask = new ScheduleTask
            {
                SessionName = model.SessionName,
                StartTime = startTs,
                EndTime = endTs,
                
                UrlEnabled = model.UrlEnabled,               
                CeneoEnabled = model.CeneoEnabled,
                GoogleEnabled = model.GoogleEnabled,
                ApiBotEnabled = model.ApiBotEnabled,
                BaseEnabled = model.BaseEnabled,


                UrlScalAleEnabled = model.UrlScalAleEnabled,
                AleCrawEnabled = model.AleCrawEnabled,
                AleApiBotEnabled = model.AleApiBotEnabled,
                AleBaseEnabled = model.AleBaseEnabled,
                MarketPlaceAutomationEnabled = model.MarketPlaceAutomationEnabled,
                PriceComparisonAutomationEnabled = model.PriceComparisonAutomationEnabled,

                DayDetailId = dayDetailId
            };
            _context.ScheduleTasks.Add(newTask);
            await _context.SaveChangesAsync();

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

        [HttpGet]
        public async Task<IActionResult> EditTask(int taskId)
        {
            var task = await _context.ScheduleTasks
                .Include(t => t.TaskStores)
                .ThenInclude(ts => ts.Store)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null) return NotFound();

            var allStores = _context.Stores.ToList();

            var vm = new AddTaskViewModel
            {
                SessionName = task.SessionName,
                StartTime = task.StartTime.ToString(@"hh\:mm"),
                EndTime = task.EndTime.ToString(@"hh\:mm"),
                
                UrlEnabled = task.UrlEnabled,
                CeneoEnabled = task.CeneoEnabled,
                GoogleEnabled = task.GoogleEnabled,
                ApiBotEnabled = task.ApiBotEnabled,
                BaseEnabled = task.BaseEnabled,

                
                UrlScalAleEnabled = task.UrlScalAleEnabled,
                AleCrawEnabled = task.AleCrawEnabled,
                AleApiBotEnabled = task.AleApiBotEnabled,
                AleBaseEnabled = task.AleBaseEnabled,
                MarketPlaceAutomationEnabled = task.MarketPlaceAutomationEnabled,
                PriceComparisonAutomationEnabled = task.PriceComparisonAutomationEnabled,

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

            var dayDetail = await _context.DayDetails
                .Include(d => d.Tasks)
                .FirstOrDefaultAsync(d => d.Id == task.DayDetailId);

            if (dayDetail == null) return NotFound();

            bool collision = dayDetail.Tasks
                .Where(x => x.Id != taskId)
                .Any(t =>
                    (startTs < t.EndTime) && (endTs > t.StartTime)
                );
            if (collision)
            {
                ModelState.AddModelError("", "Przedział czasowy już zajęty.");

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

            task.SessionName = model.SessionName;
            task.StartTime = startTs;
            task.EndTime = endTs;
            

            task.UrlEnabled = model.UrlEnabled;
            task.CeneoEnabled = model.CeneoEnabled;
            task.GoogleEnabled = model.GoogleEnabled;
            task.ApiBotEnabled = model.ApiBotEnabled;
            task.BaseEnabled = model.BaseEnabled;

            
            task.UrlScalAleEnabled = model.UrlScalAleEnabled;
            task.AleCrawEnabled = model.AleCrawEnabled;
            task.AleApiBotEnabled = model.AleApiBotEnabled;
            task.AleBaseEnabled = model.AleBaseEnabled;
            task.MarketPlaceAutomationEnabled = model.MarketPlaceAutomationEnabled;
            task.PriceComparisonAutomationEnabled = model.PriceComparisonAutomationEnabled;

            foreach (var existingRel in task.TaskStores.ToList())
            {
                if (!model.Stores.Any(s => s.IsSelected && s.StoreId == existingRel.StoreId))
                {
                    _context.ScheduleTaskStores.Remove(existingRel);
                }
            }

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