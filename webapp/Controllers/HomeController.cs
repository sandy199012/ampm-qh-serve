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
        ViewBag.User          = _auth.GetCurrentUser(HttpContext);
        ViewBag.Stats         = _db.GetStats();
        ViewBag.RecentTickets = _db.GetTickets().Take(8).ToList();
        return View();
    }
}
