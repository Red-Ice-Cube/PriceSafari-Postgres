using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using PriceSafari.Models;

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
       
        return View("~/Views/ManagerPanel/Settings/SetSchedule.cshtml", task);
    }

    [HttpPost]
    public async Task<IActionResult> SetSchedule(ScheduledTask model)
    {
        if (ModelState.IsValid)
        {
            var task = _context.ScheduledTasks.FirstOrDefault();
            if (task == null)
            {
                _context.ScheduledTasks.Add(model);
            }
            else
            {
                task.ScheduledTime = model.ScheduledTime;
                task.IsEnabled = model.IsEnabled;
                _context.ScheduledTasks.Update(task);
            }
            await _context.SaveChangesAsync();
            return RedirectToAction("SetSchedule");
        }
        return View(model);
    }
}
