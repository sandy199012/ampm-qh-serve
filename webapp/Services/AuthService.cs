using AMPMWeb.Data;

namespace AMPMWeb.Services;

public class AuthService
{
    private readonly DbService _db;

    public AuthService(DbService db) { _db = db; }

    public UserSession? Login(string username, string password)
    {
        // First try DB user lookup
        var user = _db.QueryFirst<UserRow>(
            "SELECT * FROM users WHERE username=@u AND is_active=1",
            new { u = username.ToLower().Trim() });

        // Hardcoded admin fallback (always works)
        if (username.ToLower().Trim() == "sandy" && password == "AMPM@Sandy2026")
        {
            // Update hash in DB for future use
            try
            {
                string newHash = BCrypt.Net.BCrypt.HashPassword("AMPM@Sandy2026");
                _db.Execute("UPDATE users SET password_hash=@h WHERE username='sandy'", new { h = newHash });
            }
            catch { }

            return new UserSession
            {
                Id         = 1,
                Username   = "sandy",
                Name       = "Sandeep Kumar Singh Kushwaha",
                Role       = "superadmin",
                Department = "IT"
            };
        }

        if (user == null) return null;
        if (string.IsNullOrWhiteSpace(user.PasswordHash)) return null;

        try
        {
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;
        }
        catch { return null; }

        return new UserSession
        {
            Id         = user.Id,
            Username   = user.Username,
            Name       = user.Name ?? username,
            Role       = user.Role ?? "user",
            Department = user.Department ?? ""
        };
    }

    public bool IsSuperAdmin(HttpContext ctx)
        => ctx.Session.GetString("role") == "superadmin";

    public bool IsLoggedIn(HttpContext ctx)
        => ctx.Session.GetString("username") != null;

    public UserSession? GetCurrentUser(HttpContext ctx) =>
        ctx.Session.GetString("username") == null ? null : new UserSession
        {
            Username   = ctx.Session.GetString("username")!,
            Name       = ctx.Session.GetString("name")!,
            Role       = ctx.Session.GetString("role")!,
            Department = ctx.Session.GetString("department") ?? ""
        };
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
