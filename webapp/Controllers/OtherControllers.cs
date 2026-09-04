using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class PurchaseOrdersController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public PurchaseOrdersController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? status, string? search)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var pos = _db.GetPOs();
        if (!string.IsNullOrEmpty(status))
            pos = pos.Where(p => p.GetValueOrDefault("status")?.ToString() == status).ToList();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            pos = pos.Where(p =>
                p.GetValueOrDefault("poNumber")?.ToString()?.ToLower().Contains(s)==true ||
                p.GetValueOrDefault("vendorName")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.TotalValue = pos.Where(p => p.GetValueOrDefault("status")?.ToString() != "Cancelled")
            .Sum(p => { double.TryParse(p.GetValueOrDefault("grandTotal")?.ToString(), out var g); return g; });
        return View(pos);
    }

    public IActionResult Details(string id)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var po = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id });
        if (po == null) return NotFound();
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(po) ?? new());
    }
}

public class AssetsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public AssetsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? search, string? type)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);

        var assets = _db.GetAssets();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            assets = assets.Where(a =>
                a.GetValueOrDefault("assetTag")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("brand")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("model")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("assignedToName")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("serial")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        if (!string.IsNullOrEmpty(type))
            assets = assets.Where(a => a.GetValueOrDefault("assetType")?.ToString() == type).ToList();

        ViewBag.Search = search;
        ViewBag.TypeFilter = type;
        ViewBag.Types = _db.GetAssets()
            .Select(a => a.GetValueOrDefault("assetType")?.ToString() ?? "")
            .Distinct().OrderBy(t => t).ToList();
        return View(assets);
    }

    public IActionResult Details(string id)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var asset = _db.GetAssets().FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id ||
                                                         a.GetValueOrDefault("assetTag")?.ToString() == id);
        if (asset == null) return NotFound();
        return View(asset);
    }
}

public class ITStoreController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ITStoreController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? type)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);

        // Get from both tables
        string sql = string.IsNullOrEmpty(type)
            ? "SELECT data FROM it_stock_items ORDER BY item_type, name"
            : "SELECT data FROM it_stock_items WHERE item_type=@t ORDER BY name";
        var items = _db.Query<string>(sql, new { t = type })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();

        // Also get from stock_items
        string sql2 = string.IsNullOrEmpty(type)
            ? "SELECT data FROM stock_items ORDER BY item_type, name"
            : "SELECT data FROM stock_items WHERE item_type=@t ORDER BY name";
        var items2 = _db.Query<string>(sql2, new { t = type })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();
        items.AddRange(items2.Where(i2 => !items.Any(i1 => i1.GetValueOrDefault("name")?.ToString() == i2.GetValueOrDefault("name")?.ToString())));

        // Issues from both tables
        var issues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY rowid DESC")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();
        var issues2 = _db.Query<string>("SELECT data FROM stock_issues ORDER BY ts DESC")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();
        issues.AddRange(issues2.Where(i2 => !issues.Any(i1 => i1.GetValueOrDefault("issueNo")?.ToString() == i2.GetValueOrDefault("issueNo")?.ToString())));

        if (!string.IsNullOrEmpty(type))
            issues = issues.Where(i => i.GetValueOrDefault("itemType")?.ToString() == type).ToList();

        ViewBag.Issues = issues;
        ViewBag.TypeFilter = type;
        ViewBag.TotalIssued = issues.Sum(i => { int.TryParse(i.GetValueOrDefault("qty")?.ToString(), out var q); return q; });
        return View(items);
    }
}

public class GoalsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public GoalsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(int? week, string? status)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);

        var goals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();

        int maxWeek = goals.Any() ? goals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        int curWeek = week ?? maxWeek;

        var filtered = goals.Where(g => {
            if (week.HasValue && g.GetValueOrDefault("weekNo")?.ToString() != curWeek.ToString()) return false;
            if (!string.IsNullOrEmpty(status) && g.GetValueOrDefault("status")?.ToString() != status) return false;
            return true;
        }).ToList();

        // Week info
        var weekGoals = goals.Where(g => g.GetValueOrDefault("weekNo")?.ToString() == curWeek.ToString()).ToList();
        ViewBag.WeekStart = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekStart")?.ToString() ?? "";
        ViewBag.WeekEnd   = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekEnd")?.ToString() ?? "";

        ViewBag.CurrentWeek  = curWeek;
        ViewBag.MaxWeek      = maxWeek;
        ViewBag.StatusFilter = status;
        ViewBag.Total        = filtered.Count;
        ViewBag.Completed    = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed");
        ViewBag.InProg       = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress");
        ViewBag.NotStarted   = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Not Started");
        ViewBag.OnHold       = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "On Hold");
        return View(filtered);
    }

    [HttpPost]
    public IActionResult Complete(string id)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return Unauthorized();
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var g = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        g["status"]      = "Completed";
        g["progress"]    = 100;
        g["completedOn"] = DateTime.Now.ToString("dd-MMM-yyyy");
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(g), id });
        return Json(new { ok=true });
    }
}

public class BudgetController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public BudgetController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index()
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var budget = _db.GetBudget();
        var bills  = _db.GetBills();
        double totalBudget  = budget.Sum(b => { double.TryParse(b.GetValueOrDefault("allocated")?.ToString(), out var v); return v; });
        double totalSpent   = budget.Sum(b => { double.TryParse(b.GetValueOrDefault("spent")?.ToString(), out var v); return v; });
        ViewBag.TotalBudget = totalBudget;
        ViewBag.TotalSpent  = totalSpent;
        ViewBag.Remaining   = totalBudget - totalSpent;
        ViewBag.Bills       = bills;
        return View(budget);
    }
}

public class VendorsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public VendorsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? search)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var vendors = _db.GetVendors();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            vendors = vendors.Where(v =>
                v.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s)==true ||
                v.GetValueOrDefault("city")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        ViewBag.Search = search;
        return View(vendors);
    }
}
