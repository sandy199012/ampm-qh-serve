using AMPMWeb.Data;
using Newtonsoft.Json;

namespace AMPMWeb.Services;

public class AuthService
{
    private readonly DbService _db;
    public AuthService(DbService db) { _db = db; }

    // Canonical module keys — must match each module controller's class name
    // (minus "Controller"), since the access filter checks by controller name.
    public static readonly string[] Modules = new[] {
        "Employees", "Helpdesk", "Assets", "PurchaseOrders", "PurchaseBills",
        "ITStore", "Goals", "Budget", "Licenses", "Bills", "Vendors", "Endpoints"
    };

    public static readonly Dictionary<string,string> ModuleLabels = new() {
        ["Employees"] = "Employees",
        ["Helpdesk"] = "Helpdesk",
        ["Assets"] = "Assets",
        ["PurchaseOrders"] = "Purchase Orders",
        ["PurchaseBills"] = "Purchase Bills",
        ["ITStore"] = "IT Store",
        ["Goals"] = "Goals",
        ["Budget"] = "Budget",
        ["Licenses"] = "Licenses",
        ["Bills"] = "Bills",
        ["Vendors"] = "Vendors",
        ["Endpoints"] = "Monitor",
    };

    public UserSession? Login(string username, string password)
    {
        var row = _db.GetUserByUsername((username ?? "").Trim());
        if (row == null || row.IsActive == 0) return null;
        if (string.IsNullOrEmpty(row.PasswordHash) || !SafeVerify(password ?? "", row.PasswordHash)) return null;
        return ToSession(row);
    }

    static bool SafeVerify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }

    public bool IsLoggedIn(HttpContext ctx)
        => !string.IsNullOrEmpty(ctx.Request.Cookies["ampm_user"]);

    // Always resolved fresh from the DB (never trusts the role/name cookies), so
    // permission changes made in User Management take effect immediately without
    // requiring the affected user to log out and back in.
    public UserSession? GetCurrentUser(HttpContext ctx)
    {
        var u = ctx.Request.Cookies["ampm_user"];
        if (string.IsNullOrEmpty(u)) return null;
        var row = _db.GetUserByUsername(u);
        if (row == null || row.IsActive == 0) return null;
        return ToSession(row);
    }

    UserSession ToSession(UserRow row)
    {
        var perms = new Dictionary<string, ModulePermission>();
        if (!string.IsNullOrWhiteSpace(row.Permissions))
        {
            try { perms = JsonConvert.DeserializeObject<Dictionary<string, ModulePermission>>(row.Permissions!) ?? new(); }
            catch { perms = new(); }
        }
        return new UserSession {
            Id = row.Id,
            Username = row.Username,
            Name = string.IsNullOrWhiteSpace(row.Name) ? row.Username : row.Name!,
            Role = string.IsNullOrWhiteSpace(row.Role) ? "user" : row.Role!,
            Department = row.Department ?? "",
            Permissions = perms,
            EmpId = row.EmpId,
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
    public int IsActive { get; set; } = 1;
    public string? Permissions { get; set; }
    public string? EmpId { get; set; }
}

public class ModulePermission
{
    public bool View { get; set; }
    public bool Approve { get; set; }
}

public class UserSession
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Department { get; set; } = "";
    public Dictionary<string, ModulePermission> Permissions { get; set; } = new();
    public string? EmpId { get; set; }

    public bool IsSuperAdmin => Role == "superadmin";
    public bool IsAdmin => Role is "superadmin" or "admin";

    public bool CanView(string module)
        => IsAdmin || (Permissions.TryGetValue(module, out var p) && p.View);

    public bool CanApprove(string module)
        => IsAdmin || (Permissions.TryGetValue(module, out var p) && p.Approve);
}
