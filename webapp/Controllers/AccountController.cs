using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class AccountController : Controller
{
    private readonly AuthService _auth;
    public AccountController(AuthService auth) { _auth = auth; }

    [HttpGet]
    public IActionResult Login() =>
        _auth.IsLoggedIn(HttpContext) ? RedirectToAction("Index","Home") : View();

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        var user = _auth.Login(username, password);
        if (user == null)
        {
            ViewBag.Error = "Invalid username or password!";
            return View();
        }
        HttpContext.Session.SetString("username",   user.Username);
        HttpContext.Session.SetString("name",       user.Name);
        HttpContext.Session.SetString("role",       user.Role);
        HttpContext.Session.SetString("department", user.Department);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}
