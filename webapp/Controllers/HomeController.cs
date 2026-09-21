using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class HomeController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;

    public HomeController(DbService db, AuthService auth)
    { _db = db; _auth = auth; }

    public IActionResult Index()
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        var user = _auth.GetCurrentUser(HttpContext);
        if (user == null) return RedirectToAction("Login","Account");

        // Non-admin accounts (role "user") never see the admin dashboard —
        // they get the self-service "My Helpdesk" portal instead: their own
        // tickets and the ability to raise a new one, nothing global.
        if (!user.IsAdmin) return RedirectToAction("Index", "MyHelpdesk");

        ViewBag.User          = user;
        ViewBag.Stats         = _db.GetStats();
        ViewBag.RecentTickets = _db.GetTickets().Take(8).ToList();
        ViewBag.LowStock      = _db.GetLowStockItems();
        ViewBag.PendingGoals  = _db.GetPendingGoalsCount();
        return View();
    }

    public IActionResult AccessDenied()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }
}
