using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

/// <summary>IMEI / serial helpers shared by MyDevice and the Assets forms.</summary>
public static class MobileIds
{
    /// <summary>Digits only (spaces, dashes, slashes removed).</summary>
    public static string NormImei(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>15 digits and a correct Luhn check digit.</summary>
    public static bool IsImei(string? digits)
    {
        if (digits == null || digits.Length != 15 || !digits.All(char.IsDigit)) return false;
        int sum = 0;
        for (int i = 0; i < 15; i++)
        {
            int n = digits[14 - i] - '0';
            if (i % 2 == 1) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
        }
        return sum % 10 == 0;
    }

    public static string CleanSerial(string? s)
    {
        var t = (s ?? "").Trim().ToUpperInvariant();
        return new string(t.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/' or '.').ToArray());
    }
}

// Self-service "register my phone": the employee opens this page on the phone
// (Chrome / Safari), logs in once, and the page fills in everything a browser is
// allowed to read (model, OS, screen, RAM hint, battery). A browser can NOT read
// the IMEI or serial number, so those are typed once and checked (IMEI Luhn).
// The result is saved into Asset Stock (type Phone / iPad / Tablet, tag MOB- / IPAD-)
// assigned to the logged-in employee, and into Endpoints -> Mobile Devices.
// Exempt from the module-permission filter (any logged-in user may register THEIR
// OWN phone); the controller checks the login itself and never lets a normal user
// touch a device that belongs to somebody else.
public class MyDeviceController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public MyDeviceController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    static readonly string[] DeviceTypes = { "Phone", "iPad", "Tablet" };
    const int MaxPerEmployee = 5;

    static string S(Dictionary<string,object?> d, string k) => d.GetValueOrDefault(k)?.ToString()?.Trim() ?? "";
    static string Cap(string? s, int n) { s = (s ?? "").Trim(); return s.Length > n ? s.Substring(0, n) : s; }
    static bool IsDevice(Dictionary<string,object?> a) => DeviceTypes.Contains(S(a, "assetType"));

    IActionResult? NeedLogin(out UserSession? user)
    {
        user = null;
        if (!_auth.IsLoggedIn(HttpContext))
            return Redirect("/Account/Login?returnUrl=" + Uri.EscapeDataString("/MyDevice"));
        user = _auth.GetCurrentUser(HttpContext);
        if (user == null)
            return Redirect("/Account/Login?returnUrl=" + Uri.EscapeDataString("/MyDevice"));
        return null;
    }

    [HttpGet("/MyDevice")]
    public IActionResult Index()
    {
        var r = NeedLogin(out var user);
        if (r != null) return r;
        ViewBag.User = user;

        var empId = user!.EmpId ?? "";
        var emp = string.IsNullOrWhiteSpace(empId) ? null : _db.GetEmployeeByCode(empId);
        ViewBag.EmpCode = empId;
        ViewBag.EmpName = emp != null ? S(emp, "name") : "";
        ViewBag.NoEmployeeLinked = emp == null;

        var mine = string.IsNullOrWhiteSpace(empId)
            ? new List<Dictionary<string,object?>>()
            : _db.GetAssets().Where(a => IsDevice(a) && string.Equals(S(a, "assignedToEmp"), empId, StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.MineJson = JsonConvert.SerializeObject(mine.Select(a => new {
            tag = S(a, "assetTag"), type = S(a, "assetType"), brand = S(a, "brand"), model = S(a, "model"),
            imei = S(a, "imei"), imei2 = S(a, "imei2"), serial = S(a, "serial"), deviceId = S(a, "deviceId"),
            os = S(a, "os"), ram = S(a, "ram"), storage = S(a, "storage"), lastSeen = S(a, "lastSeen")
        })).Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026");   // safe inside <script>
        ViewBag.Mine = mine;
        return View();
    }

    [HttpPost("/MyDevice")]
    public IActionResult Submit(IFormCollection form)
    {
        var r = NeedLogin(out var user);
        if (r != null) return r;

        string F(string k) => form[k].ToString();

        // ---- whose device? Normal users: always their own linked employee. Admins may type another code.
        var empCode = (user!.EmpId ?? "").Trim();
        if (user.IsAdmin && !string.IsNullOrWhiteSpace(F("empCode"))) empCode = F("empCode").Trim();
        Dictionary<string,object?>? emp = string.IsNullOrWhiteSpace(empCode) ? null : _db.GetEmployeeByCode(empCode);
        if (emp == null && !user.IsAdmin)
        {
            TempData["Error"] = "Your login is not linked to an Employee record yet. Please ask IT to link it, then register your phone.";
            return Redirect("/MyDevice");
        }
        if (emp == null && !string.IsNullOrWhiteSpace(empCode))
        {
            TempData["Error"] = $"Employee code '{empCode}' was not found.";
            return Redirect("/MyDevice");
        }

        // ---- validate
        var type = DeviceTypes.Contains(F("assetType")) ? F("assetType") : "Phone";
        var imei  = MobileIds.NormImei(F("imei"));
        var imei2 = MobileIds.NormImei(F("imei2"));
        var serial = MobileIds.CleanSerial(F("serial"));
        if (imei != "" && !MobileIds.IsImei(imei))
        {
            TempData["Error"] = "IMEI looks wrong — it must be 15 digits (dial *#06# on the phone). Please re-check and try again.";
            return Redirect("/MyDevice");
        }
        if (imei2 != "" && !MobileIds.IsImei(imei2))
        {
            TempData["Error"] = "IMEI 2 looks wrong — it must be 15 digits. Leave it empty if the phone has only one SIM slot.";
            return Redirect("/MyDevice");
        }
        if (serial != "" && (serial.Length < 5 || serial.Length > 30))
        {
            TempData["Error"] = "Serial number must be 5 to 30 characters.";
            return Redirect("/MyDevice");
        }
        if (imei == "" && serial == "")
        {
            TempData["Error"] = "Please enter the IMEI or the Serial Number of the device (at least one).";
            return Redirect("/MyDevice");
        }
        var brand = Cap(F("brand"), 40);
        var model = Cap(F("model"), 60);
        if (model == "")
        {
            TempData["Error"] = "Model is required (e.g. SM-A546E / iPhone 14).";
            return Redirect("/MyDevice");
        }
        var deviceId = Cap(new string(F("deviceId").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()), 64);

        var assets = _db.GetAssets();
        bool Same(string a, string b) => a != "" && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ---- find an existing record: IMEI -> IMEI 2 -> serial -> browser device id
        var asset = assets.FirstOrDefault(a => IsDevice(a) && (Same(imei, S(a, "imei")) || Same(imei, S(a, "imei2"))))
                 ?? (imei2 != "" ? assets.FirstOrDefault(a => IsDevice(a) && (Same(imei2, S(a, "imei")) || Same(imei2, S(a, "imei2")))) : null)
                 ?? (serial != "" ? assets.FirstOrDefault(a => IsDevice(a) && Same(serial, S(a, "serial"))) : null)
                 ?? (deviceId != "" ? assets.FirstOrDefault(a => IsDevice(a) && Same(deviceId, S(a, "deviceId"))) : null);

        var empName = emp != null ? S(emp, "name") : "";
        var empDept = emp != null ? S(emp, "dept") : "";
        var empNo   = emp != null ? S(emp, "emp") : "";
        if (emp != null && empNo == "") empNo = empCode;
        var now = IstTime.Now.ToString("yyyy-MM-dd HH:mm");
        bool created = false;

        if (asset != null)
        {
            var owner = S(asset, "assignedToEmp");
            if (!user.IsAdmin && owner != "" && !string.Equals(owner, empNo, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = $"This device is already registered to {S(asset, "assignedToName")} ({owner}). Please contact IT.";
                return Redirect("/MyDevice");
            }
        }
        else
        {
            if (emp != null && assets.Count(a => IsDevice(a) && Same(empNo, S(a, "assignedToEmp"))) >= MaxPerEmployee)
            {
                TempData["Error"] = $"You already have {MaxPerEmployee} devices registered. Please contact IT to change them.";
                return Redirect("/MyDevice");
            }
            var prefix = AssetTags.PrefixFor(type) ?? "MOB-";
            asset = new Dictionary<string,object?> {
                ["id"] = Guid.NewGuid().ToString("N")[..8],
                ["assetTag"] = AssetTags.NextTag(assets, prefix),
                ["condition"] = "Good",
                ["source"] = "Mobile check-in",
                ["firstSeen"] = now
            };
            assets.Add(asset);
            created = true;
        }

        asset["assetType"] = type;
        if (brand != "") asset["brand"] = brand;
        asset["model"] = model;
        if (imei != "")   asset["imei"]   = imei;
        if (imei2 != "")  asset["imei2"]  = imei2;
        if (serial != "") asset["serial"] = serial;
        if (deviceId != "") asset["deviceId"] = deviceId;
        void SetIf(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) asset[k] = v; }
        SetIf("os", Cap(F("os"), 40));
        SetIf("ram", Cap(new string(F("ram").Where(c => char.IsDigit(c) || c == '.').ToArray()), 6));
        SetIf("storage", Cap(new string(F("storage").Where(c => char.IsDigit(c) || c == '.').ToArray()), 6));
        SetIf("screen", Cap(F("screen"), 30));
        SetIf("battery", Cap(new string(F("battery").Where(char.IsDigit).ToArray()), 3));
        asset["lastSeen"] = now;

        if (emp != null && (S(asset, "assignedToEmp") == "" || !user.IsAdmin || created))
        {
            asset["assignedToName"] = empName;
            asset["assignedToEmp"]  = empNo;
            asset["assignedToDept"] = empDept;
            if (S(asset, "assignedDate") == "" || created) asset["assignedDate"] = IstTime.Today.ToString("yyyy-MM-dd");
            asset["returnDate"] = "";
        }
        _db.SaveAssets(assets);

        // ---- Endpoints -> Mobile Devices list (same store the Flutter app writes to)
        try
        {
            var inv = _db.KGetObj<List<Dictionary<string,object?>>>("mobile_inventory") ?? new();
            var key = deviceId != "" ? deviceId : S(asset, "assetTag");
            var rec = inv.FirstOrDefault(d => string.Equals(S(d, "deviceId"), key, StringComparison.OrdinalIgnoreCase));
            bool isNew = rec == null;
            rec ??= new Dictionary<string,object?>();
            rec["deviceId"] = key;
            rec["assetTag"] = S(asset, "assetTag");
            rec["model"] = model;
            if (brand != "") rec["manufacturer"] = brand;
            if (S(asset, "os") != "") rec["os"] = S(asset, "os");
            if (S(asset, "battery") != "") rec["battery"] = S(asset, "battery");
            if (S(asset, "imei") != "") rec["imei"] = S(asset, "imei");
            if (S(asset, "serial") != "") rec["serial"] = S(asset, "serial");
            rec["user"] = user.Username;
            rec["empId"] = S(asset, "assignedToEmp");
            rec["empName"] = S(asset, "assignedToName");
            rec["source"] = "Web check-in";
            rec["lastSeen"] = now;
            if (isNew) inv.Add(rec);
            _db.KSet("mobile_inventory", inv);
        }
        catch { /* the asset record is the important one */ }

        TempData["Success"] = created
            ? $"Device registered as {S(asset, "assetTag")}. Thank you!"
            : $"Device {S(asset, "assetTag")} updated.";
        return Redirect("/MyDevice");
    }
}
