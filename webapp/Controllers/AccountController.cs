using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class AccountController : Controller
{
    private readonly AuthService _auth;
    public AccountController(AuthService auth) { _auth = auth; }

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        // Already signed in and following a shared link (e.g. /MyDevice)? Go straight there.
        if (_auth.IsLoggedIn(HttpContext) && !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return View();
    }

    // Client address as seen by the app. Render puts the real client address at
    // the END of X-Forwarded-For (anything before it can be typed by the caller),
    // so take the last entry.
    string ClientIp()
    {
        var xff = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var last = xff.Split(',').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
            if (!string.IsNullOrEmpty(last)) return last;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    [HttpPost]
    public IActionResult Login(string username, string password, string? returnUrl)
    {
        var ip = ClientIp();
        if (LoginThrottle.IsLocked(ip, username ?? ""))
        {
            ViewBag.Error = "Too many failed attempts. Please wait 15 minutes and try again.";
            return View();
        }

        var user = _auth.Login(username ?? "", password ?? "");
        if (user == null)
        {
            LoginThrottle.Fail(ip, username ?? "");
            ViewBag.Error = "Invalid username or password!";
            return View();
        }
        LoginThrottle.Success(ip, username ?? "");

        bool https = Request.IsHttps || string.Equals(Request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase);
        var life = TimeSpan.FromHours(8);

        // The real login: a signed cookie that cannot be forged or extended
        // (see AuthService.IssueSessionToken). HttpOnly so page scripts can't read it.
        Response.Cookies.Append(AuthService.SessionCookie, AuthService.IssueSessionToken(user.Username, life), new CookieOptions {
            Expires = DateTimeOffset.UtcNow.Add(life),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = https,
            Path = "/"
        });

        // Display-only cookies (name shown in "created by" / "raised by" labels).
        // Nothing grants access based on these any more.
        var opts = new CookieOptions {
            Expires = DateTimeOffset.UtcNow.Add(life),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = https,
            Path = "/"
        };
        Response.Cookies.Append("ampm_name", user.Name, opts);
        Response.Cookies.Append("ampm_role", user.Role, opts);
        Response.Cookies.Delete("ampm_user");   // old forgeable login cookie

        // A shared link such as /MyDevice sends people to login first and back afterwards.
        // Only local paths are accepted, so this can never redirect to another site.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);

        // Admins/superadmins land on the full dashboard as before; everyone
        // else goes straight to their own self-service Helpdesk portal.
        if (!user.IsAdmin) return RedirectToAction("Index", "MyHelpdesk");
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthService.SessionCookie);
        Response.Cookies.Delete("ampm_user");
        Response.Cookies.Delete("ampm_name");
        Response.Cookies.Delete("ampm_role");
        return RedirectToAction("Login");
    }
}
