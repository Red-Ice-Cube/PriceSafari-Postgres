using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

public class AuthorizeStoreAccessAttribute : ActionFilterAttribute
{
    private readonly PriceSafariContext _context;

    public AuthorizeStoreAccessAttribute(PriceSafariContext context)
    {
        _context = context;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        // Pobieranie storeId z parametrów akcji
        if (context.ActionArguments.TryGetValue("storeId", out var storeIdObj) && storeIdObj is int storeId)
        {
            // Sprawdzenie, czy użytkownik ma dostęp do sklepu
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
            {
                context.Result = new ForbidResult(); // Użytkownik nie ma dostępu do sklepu
                return;
            }
        }

        // Pobieranie reportId z parametrów akcji
        if (context.ActionArguments.TryGetValue("reportId", out var reportIdObj) && reportIdObj is int reportId)
        {
            // Sprawdzenie, czy raport istnieje i czy jest powiązany ze sklepem, do którego użytkownik ma dostęp
            var report = await _context.PriceSafariReports
                .Include(r => r.Store)  // Pobranie powiązanego sklepu
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null)
            {
                context.Result = new NotFoundResult(); // Raport nie istnieje
                return;
            }

            // Sprawdzenie, czy użytkownik ma dostęp do sklepu powiązanego z raportem
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == report.StoreId);
            if (!hasAccess)
            {
                context.Result = new ForbidResult(); // Użytkownik nie ma dostępu do sklepu powiązanego z raportem
                return;
            }
        }

        // Kontynuacja wykonywania akcji
        await next();
    }
}
