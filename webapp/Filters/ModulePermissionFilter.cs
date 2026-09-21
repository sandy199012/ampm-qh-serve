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
    // MyHelpdesk is exempt because it's the employee self-service portal (raise a
    // ticket, see your own tickets' status); the controller itself scopes everything
    // to the logged-in user's own EmpId, so no module-permission checkbox is needed
    // to reach it.
    // NOTE: Performance is intentionally NOT exempt — it's the IT team's own
    // productivity dashboard (Daily To-Do / Weekly Goals / Helpdesk resolution
    // stats), not something a general employee should see. It's also not one of
    // AuthService.Modules, so no "user"-role account can ever be granted a
    // per-module permission for it either — CanView("Performance") below is
    // therefore admin/superadmin-only by construction.
    static readonly HashSet<string> ExemptControllers = new(StringComparer.OrdinalIgnoreCase)
        { "Account", "Home", "Api", "MyHelpdesk" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (ExemptControllers.Contains(controllerName)) return;

        var ctx = context.HttpContext;

        // PC-inventory auto-report endpoint: called by the AMPM_PC_Agent script
        // running unattended on office PCs — no browser/cookie session to check
        // here, it verifies its own shared key instead (EndpointsController.ReportPc).
        if (ctx.Request.Path.StartsWithSegments("/api/endpoints/report-pc", StringComparison.OrdinalIgnoreCase))
            return;

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
