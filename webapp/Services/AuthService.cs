using AMPMWeb.Data;

namespace AMPMWeb.Services;

public class AuthService
{
    private readonly DbService _db;
    public AuthService(DbService db) { _db = db; }

    public UserSession? Login(string username, string password)
    {
        if (username?.ToLower().Trim() == "sandy" && password == "AMPM@Sandy2026")
            return new UserSession { Id=1, Username="sandy",
                Name="Sandeep Kumar Singh Kushwaha", Role="superadmin", Department="IT" };
        return null;
    }

    public bool IsLoggedIn(HttpContext ctx)
        => !string.IsNullOrEmpty(ctx.Request.Cookies["ampm_user"]);

    public UserSession? GetCurrentUser(HttpContext ctx)
    {
        var u = ctx.Request.Cookies["ampm_user"];
        if (string.IsNullOrEmpty(u)) return null;
        return new UserSession {
            Username   = u,
            Name       = ctx.Request.Cookies["ampm_name"] ?? u,
            Role       = ctx.Request.Cookies["ampm_role"] ?? "user",
            Department = "IT"
        };
    }
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
