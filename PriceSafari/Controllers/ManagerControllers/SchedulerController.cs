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
            AutoMatchingStores = autoMatchingStores
        };

        return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", viewModel);
    }



    [HttpPost]
    public async Task<IActionResult> SetSchedule(ScheduledTaskInputModel model)
    {
        if (ModelState.IsValid)
        {
            TimeSpan scheduledTime;
            if (TimeSpan.TryParse(model.ScheduledTime, out scheduledTime))
            {
                var task = _context.ScheduledTasks.FirstOrDefault();
                if (task == null)
                {
                    task = new ScheduledTask
                    {
                        ScheduledTime = scheduledTime,
                        IsEnabled = model.IsEnabled
                    };
                    _context.ScheduledTasks.Add(task);
                }
                else
                {
                    task.ScheduledTime = scheduledTime;
                    task.IsEnabled = model.IsEnabled;
                    _context.ScheduledTasks.Update(task);
                }
                await _context.SaveChangesAsync();
                return RedirectToAction("SetSchedule");
            }
            else
            {
                ModelState.AddModelError("ScheduledTime", "Invalid time format.");
            }
        }

        // If ModelState is invalid, re-create the SchedulerViewModel for the view
        var autoMatchingStores = _context.Stores.Where(s => s.AutoMatching).ToList();

        var viewModel = new SchedulerViewModel
        {
            ScheduledTask = new ScheduledTask
            {
                ScheduledTime = TimeSpan.Zero, // Default value if parsing fails
                IsEnabled = model.IsEnabled
            },
            AutoMatchingStores = autoMatchingStores
        };

        return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", viewModel);
    }


}
