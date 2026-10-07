using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
        "ITStore", "Goals", "Todos", "Budget", "Licenses", "Bills", "Vendors", "Endpoints"
    };

    public static readonly Dictionary<string,string> ModuleLabels = new() {
        ["Employees"] = "Employees",
        ["Helpdesk"] = "Helpdesk",
        ["Assets"] = "Assets",
        ["PurchaseOrders"] = "Purchase Orders",
        ["PurchaseBills"] = "Purchase Bills",
        ["ITStore"] = "IT Store",
        ["Goals"] = "Goals",
        ["Todos"] = "Daily To-Do",
        ["Budget"] = "Budget",
        ["Licenses"] = "Licenses",
        ["Bills"] = "Bills",
        ["Vendors"] = "Vendors",
        ["Endpoints"] = "Monitor",
    };

    public UserSession? Login(string username, string password)
    {
        var id = (username ?? "").Trim();
        var row = _db.GetUserByUsername(id);
        // The mobile app's login screen asks for "Employee Code", not the account
        // Username — those only match when a login was created by picking the
        // employee in the dropdown and leaving Username blank. If admin typed a
        // different Username (or edited it), logging in with the Employee Code
        // would otherwise fail even with the correct password. Fall back to
        // matching by the linked Employee Code so "Employee Code + Password"
        // always works, for both the mobile app and the web login.
        if (row == null) row = _db.GetUserByEmpId(id);
        if (row == null || row.IsActive == 0) return null;
        if (string.IsNullOrEmpty(row.PasswordHash) || !SafeVerify(password ?? "", row.PasswordHash)) return null;
        return ToSession(row);
    }

    static bool SafeVerify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }

    // ---- signed session cookie ------------------------------------------------
    // The login used to be a plain "ampm_user=<username>" cookie that the server
    // simply believed, so anyone who knew (or guessed) a username could type that
    // cookie into their own browser and be logged in as that user WITHOUT the
    // password. Now the login cookie is "ampm_sess" = username + expiry + an
    // HMAC-SHA256 signature made with a server-only secret, so it cannot be
    // forged or extended. Set SESSION_SECRET (a long random string) in the
    // server's environment so logins also survive a restart; without it a random
    // secret is created at every start (everyone just logs in again after a
    // restart/deploy).
    public const string SessionCookie = "ampm_sess";
    static readonly byte[] SessionKey = LoadSessionKey();
    public static bool SessionSecretFromEnv { get; private set; }

    static byte[] LoadSessionKey()
    {
        var s = Environment.GetEnvironmentVariable("SESSION_SECRET");
        if (!string.IsNullOrWhiteSpace(s) && s.Trim().Length >= 16)
        {
            SessionSecretFromEnv = true;
            return Encoding.UTF8.GetBytes(s.Trim());
        }
        return RandomNumberGenerator.GetBytes(32);
    }

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] B64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
        return Convert.FromBase64String(t);
    }
    static string Sign(string payload) => B64Url(HMACSHA256.HashData(SessionKey, Encoding.UTF8.GetBytes(payload)));

    public static string IssueSessionToken(string username, TimeSpan life)
    {
        var exp = DateTimeOffset.UtcNow.Add(life).ToUnixTimeSeconds();
        var payload = B64Url(Encoding.UTF8.GetBytes(username)) + "." + exp;
        return payload + "." + Sign(payload);
    }

    // Returns the username inside a valid, unexpired, correctly-signed session
    // cookie - or null for anything missing, tampered with, or expired.
    static string? ReadSessionUser(HttpContext ctx)
    {
        try
        {
            var tok = ctx.Request.Cookies[SessionCookie];
            if (string.IsNullOrEmpty(tok)) return null;
            var parts = tok.Split('.');
            if (parts.Length != 3) return null;
            var expected = Encoding.ASCII.GetBytes(Sign(parts[0] + "." + parts[1]));
            var given = Encoding.ASCII.GetBytes(parts[2]);
            if (expected.Length != given.Length || !CryptographicOperations.FixedTimeEquals(expected, given)) return null;
            if (!long.TryParse(parts[1], out var exp) || exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var user = Encoding.UTF8.GetString(B64UrlDecode(parts[0]));
            return string.IsNullOrWhiteSpace(user) ? null : user;
        }
        catch { return null; }
    }

    public bool IsLoggedIn(HttpContext ctx) => ReadSessionUser(ctx) != null;

    // Always resolved fresh from the DB (never trusts the role/name cookies), so
    // permission changes made in User Management take effect immediately without
    // requiring the affected user to log out and back in.
    public UserSession? GetCurrentUser(HttpContext ctx)
    {
        var u = ReadSessionUser(ctx);
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
            MustChangePassword = row.MustChangePassword == 1,
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
    public int MustChangePassword { get; set; } = 0;
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
    public bool MustChangePassword { get; set; }

    public bool IsSuperAdmin => Role == "superadmin";
    public bool IsAdmin => Role is "superadmin" or "admin";

    public bool CanView(string module)
        => IsAdmin || (Permissions.TryGetValue(module, out var p) && p.View);

    public bool CanApprove(string module)
        => IsAdmin || (Permissions.TryGetValue(module, out var p) && p.Approve);
}

// Brute-force protection for the web login: 5 wrong passwords for the same
// username from the same address, or 10 for the same username from anywhere,
// or 25 from one address, lock further attempts for 15 minutes.
public static class LoginThrottle
{
    class Entry { public int Count; public DateTime WindowStart = DateTime.UtcNow; public DateTime LockedUntil = DateTime.MinValue; }
    static readonly ConcurrentDictionary<string, Entry> Map = new();
    static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    static IEnumerable<(string key, int max)> Keys(string ip, string user)
    {
        var u = (user ?? "").Trim().ToLowerInvariant();
        yield return ("ipu|" + ip + "|" + u, 5);
        yield return ("u|" + u, 10);
        yield return ("ip|" + ip, 25);
    }

    public static bool IsLocked(string ip, string user)
    {
        foreach (var (key, _) in Keys(ip, user))
            if (Map.TryGetValue(key, out var e) && e.LockedUntil > DateTime.UtcNow) return true;
        return false;
    }

    public static void Fail(string ip, string user)
    {
        var now = DateTime.UtcNow;
        foreach (var (key, max) in Keys(ip, user))
        {
            var e = Map.GetOrAdd(key, _ => new Entry());
            lock (e)
            {
                if (now - e.WindowStart > Window) { e.Count = 0; e.WindowStart = now; }
                e.Count++;
                if (e.Count >= max) e.LockedUntil = now + Window;
            }
        }
        if (Map.Count > 5000) foreach (var k in Map.Where(kv => kv.Value.LockedUntil < now && now - kv.Value.WindowStart > Window).Select(kv => kv.Key).ToList()) Map.TryRemove(k, out _);
    }

    public static void Success(string ip, string user)
    {
        foreach (var (key, _) in Keys(ip, user))
            if (key.StartsWith("ipu|") || key.StartsWith("u|")) Map.TryRemove(key, out _);
    }
}
