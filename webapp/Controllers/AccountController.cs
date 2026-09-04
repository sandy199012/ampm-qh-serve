using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

[IgnoreAntiforgeryToken]
public class AccountController : Controller
{
    private readonly AuthService _auth;
    public AccountController(AuthService auth) { _auth = auth; }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        var user = _auth.Login(username, password);
        if (user == null)
        {
            ViewBag.Error = "Invalid username or password!";
            return View();
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name,     user.Username),
            new Claim("FullName",          user.Name),
            new Claim(ClaimTypes.Role,     user.Role),
            new Claim("Department",        user.Department),
        };

        var identity  = new ClaimsIdentity(claims, "CookieAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("CookieAuth", principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("CookieAuth");
        return RedirectToAction("Login");
    }
}
