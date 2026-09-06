using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using AMPMWeb.Services;

namespace AMPMWeb.Filters;

// Runs before every action. Enforces: must be logged in, and (unless admin/superadmin)
// must have "View" permission for the module that matches the current controller name.
// Account/Home are always reachable (login page, dashboard, access-denied page).
// Users (the user-management module) is admin/superadmin only, regardless of any
// per-module permission a "user"-role account might have.
public class ModulePermissionFilter : IActionFilter
{
    private readonly AuthService _auth;
    public ModulePermissionFilter(AuthService auth) { _auth = auth; }

    // Api is exempt because it's the mobile app's entry point — it has no login
    // cookie to check here, and instead verifies username+password on every call itself.
    static readonly HashSet<string> ExemptControllers = new(StringComparer.OrdinalIgnoreCase)
        { "Account", "Home", "Api" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (ExemptControllers.Contains(controllerName)) return;

        var ctx = context.HttpContext;
        if (!_auth.IsLoggedIn(ctx))
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        var user = _auth.GetCurrentUser(ctx);
        if (user == null)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        if (controllerName.Equals("Users", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.IsAdmin)
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
            return;
        }

        if (!user.CanView(controllerName))
            context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
