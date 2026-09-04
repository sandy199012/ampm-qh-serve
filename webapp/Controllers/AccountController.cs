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

        var opts = new CookieOptions {
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            HttpOnly = false,  // JS readable for checks
            SameSite = SameSiteMode.Lax,
            Secure = false,    // Works on both HTTP and HTTPS
            Path = "/"
        };

        Response.Cookies.Append("ampm_user", user.Username, opts);
        Response.Cookies.Append("ampm_name", user.Name, opts);
        Response.Cookies.Append("ampm_role", user.Role, opts);

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
