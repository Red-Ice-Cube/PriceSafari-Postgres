using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;

[Authorize(Roles = "Admin")]
public class SchedulerController : Controller
{
    private readonly PriceSafariContext _context;

    public SchedulerController(PriceSafariContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult SetSchedule()
    {
        var task = _context.ScheduledTasks.FirstOrDefault() ?? new ScheduledTask();

        var autoMatchingStores = _context.Stores.Where(s => s.AutoMatching).ToList();

        var viewModel = new SchedulerViewModel
        {
            ScheduledTask = task,
            ScheduledTime = task.ScheduledTime.ToString(@"hh\:mm"),
            IsEnabled = task.IsEnabled,

            UrlScheduledTime = task.UrlScheduledTime.ToString(@"hh\:mm"),
            UrlIsEnabled = task.UrlIsEnabled,

            AutoMatchingStores = autoMatchingStores
        };

        return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> SetSchedule(ScheduledTaskInputModel model)
    {
        if (ModelState.IsValid)
        {
            // Próba sparsowania czasu
            if (TimeSpan.TryParse(model.ScheduledTime, out var baseTime)
                && TimeSpan.TryParse(model.UrlScheduledTime, out var urlTime))
            {
                // Pobierz (lub utwórz nowy) rekord z bazy
                var task = _context.ScheduledTasks.FirstOrDefault();

                if (task == null)
                {
                    task = new ScheduledTask
                    {
                        ScheduledTime = baseTime,
                        IsEnabled = model.IsEnabled,
                        UrlScheduledTime = urlTime,
                        UrlIsEnabled = model.UrlIsEnabled
                    };
                    _context.ScheduledTasks.Add(task);
                }
                else
                {
                    task.ScheduledTime = baseTime;
                    task.IsEnabled = model.IsEnabled;
                    task.UrlScheduledTime = urlTime;
                    task.UrlIsEnabled = model.UrlIsEnabled;
                    _context.ScheduledTasks.Update(task);
                }

                await _context.SaveChangesAsync();
                return RedirectToAction("SetSchedule");
            }
            else
            {
                // Jeśli parsowanie się nie udało, to też dodajmy błędy do ModelState
                if (!TimeSpan.TryParse(model.ScheduledTime, out _))
                    ModelState.AddModelError("ScheduledTime", "Invalid time format for BASE_SCAL time.");

                if (!TimeSpan.TryParse(model.UrlScheduledTime, out _))
                    ModelState.AddModelError("UrlScheduledTime", "Invalid time format for URL_SCAL time.");
            }
        }

        // Jeśli ModelState nie jest valid, musimy odtworzyć ViewModel, bo zwracamy ten sam widok
        var autoMatchingStores = _context.Stores.Where(s => s.AutoMatching).ToList();

        // Możemy skonstruować SchedulerViewModel, który jest używany w widoku SetSchedule.cshtml
        var viewModel = new SchedulerViewModel
        {
            // Tu możemy odtwarzać pola, żeby nie tracić wpisanych danych w formularzu
            ScheduledTask = new ScheduledTask(),
            ScheduledTime = model.ScheduledTime,
            IsEnabled = model.IsEnabled,
            UrlScheduledTime = model.UrlScheduledTime,
            UrlIsEnabled = model.UrlIsEnabled,

            AutoMatchingStores = autoMatchingStores
        };

        return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", viewModel);
    }




    //TAK BYŁO DAWNIEJ I DZIALALO 

    //[HttpPost]
    //public async Task<IActionResult> SetSchedule(ScheduledTaskInputModel model)
    //{
    //    if (ModelState.IsValid)
    //    {
    //        TimeSpan scheduledTime;
    //        if (TimeSpan.TryParse(model.ScheduledTime, out scheduledTime))
    //        {
    //            var task = _context.ScheduledTasks.FirstOrDefault();
    //            if (task == null)
    //            {
    //                task = new ScheduledTask
    //                {
    //                    ScheduledTime = scheduledTime,
    //                    IsEnabled = model.IsEnabled
    //                };
    //                _context.ScheduledTasks.Add(task);
    //            }
    //            else
    //            {
    //                task.ScheduledTime = scheduledTime;
    //                task.IsEnabled = model.IsEnabled;
    //                _context.ScheduledTasks.Update(task);
    //            }
    //            await _context.SaveChangesAsync();
    //            return RedirectToAction("SetSchedule");
    //        }
    //        else
    //        {
    //            ModelState.AddModelError("ScheduledTime", "Invalid time format.");
    //        }
    //    }

    //    // If ModelState is invalid, re-create the SchedulerViewModel for the view
    //    var autoMatchingStores = _context.Stores.Where(s => s.AutoMatching).ToList();

    //    var viewModel = new SchedulerViewModel
    //    {
    //        ScheduledTask = new ScheduledTask
    //        {
    //            ScheduledTime = TimeSpan.Zero, // Default value if parsing fails
    //            IsEnabled = model.IsEnabled
    //        },
    //        AutoMatchingStores = autoMatchingStores
    //    };

    //    return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", viewModel);
    //}


}
