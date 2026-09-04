using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class AccountController : Controller
{
    private readonly AuthService _auth;
    public AccountController(AuthService auth) { _auth = auth; }

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        var user = _auth.Login(username, password);
        if (user == null)
        {
            ViewBag.Error = "Invalid username or password!";
            return View();
        }
        // Simple cookie - no encryption
        Response.Cookies.Append("ampm_user", user.Username, new CookieOptions { 
            Expires = DateTimeOffset.UtcNow.AddHours(8), HttpOnly = true });
        Response.Cookies.Append("ampm_name", user.Name, new CookieOptions { 
            Expires = DateTimeOffset.UtcNow.AddHours(8) });
        Response.Cookies.Append("ampm_role", user.Role, new CookieOptions { 
            Expires = DateTimeOffset.UtcNow.AddHours(8) });
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Logout()
    {
        Response.Cookies.Delete("ampm_user");
        Response.Cookies.Delete("ampm_name");
        Response.Cookies.Delete("ampm_role");
        return RedirectToAction("Login");
    }
}
