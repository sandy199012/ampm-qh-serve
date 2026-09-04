using System.Security.Claims;
using AMPMWeb.Data;

namespace AMPMWeb.Services;

public class AuthService
{
    private readonly DbService _db;
    public AuthService(DbService db) { _db = db; }

    public UserSession? Login(string username, string password)
    {
        // Hardcoded admin - always works
        if (username.ToLower().Trim() == "sandy" && password == "AMPM@Sandy2026")
        {
            return new UserSession { Id=1, Username="sandy",
                Name="Sandeep Kumar Singh Kushwaha", Role="superadmin", Department="IT" };
        }

        var user = _db.QueryFirst<UserRow>(
            "SELECT * FROM users WHERE username=@u AND is_active=1",
            new { u = username.ToLower().Trim() });

        if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash)) return null;

        try { if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null; }
        catch { return null; }

        return new UserSession {
            Id=user.Id, Username=user.Username,
            Name=user.Name ?? username, Role=user.Role ?? "user",
            Department=user.Department ?? ""
        };
    }

    // Get user from Claims (no session needed)
    public UserSession? GetCurrentUser(HttpContext ctx)
    {
        if (ctx.User?.Identity?.IsAuthenticated != true) return null;
        return new UserSession {
            Username   = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? "",
            Name       = ctx.User.FindFirst("FullName")?.Value ?? "",
            Role       = ctx.User.FindFirst(ClaimTypes.Role)?.Value ?? "user",
            Department = ctx.User.FindFirst("Department")?.Value ?? ""
        };
    }

    public bool IsLoggedIn(HttpContext ctx)
        => ctx.User?.Identity?.IsAuthenticated == true;
}

public class UserRow
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Name { get; set; }
    public string? Role { get; set; }
    public string? Department { get; set; }
}

public class UserSession
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Department { get; set; } = "";
    public bool IsSuperAdmin => Role == "superadmin";
    public bool IsAdmin => Role is "superadmin" or "admin";
}
