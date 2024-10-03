using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PriceSafari.Enums;
using PriceSafari.Models;
using System.Threading.Tasks;

public class RequireUserAccessFilter : IAsyncAuthorizationFilter
{
    private readonly UserAccessRequirement _requirement;
    private readonly UserManager<PriceSafariUser> _userManager;

    public RequireUserAccessFilter(UserAccessRequirement requirement, UserManager<PriceSafariUser> userManager)
    {
        _requirement = requirement;
        _userManager = userManager;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var userPrincipal = context.HttpContext.User;
        if (!userPrincipal.Identity.IsAuthenticated)
        {
            // User is not authenticated
            context.Result = new ChallengeResult();
            return;
        }

        var userId = _userManager.GetUserId(userPrincipal);
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        bool hasAccess = false;
        switch (_requirement)
        {
            case UserAccessRequirement.ViewSafari:
                hasAccess = user.AccesToViewSafari;
                break;
            case UserAccessRequirement.CreateSafari:
                hasAccess = user.AccesToCreateSafari;
                break;
            case UserAccessRequirement.ViewMargin:
                hasAccess = user.AccesToViewMargin;
                break;
            case UserAccessRequirement.SetMargin:
                hasAccess = user.AccesToSetMargin;
                break;
            default:
                hasAccess = false;
                break;
        }

        if (!hasAccess)
        {
            context.Result = new ForbidResult();
            return;
        }
    }
}
