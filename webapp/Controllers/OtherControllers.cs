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

    public IActionResult Index(string? status)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var pos = _db.GetPOs();
        if (!string.IsNullOrEmpty(status))
            pos = pos.Where(p => p.GetValueOrDefault("status")?.ToString() == status).ToList();
        ViewBag.Status = status;
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

public class ITStoreController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ITStoreController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? type)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);

        string sql = string.IsNullOrEmpty(type)
            ? "SELECT data FROM stock_items ORDER BY item_type, name"
            : "SELECT data FROM stock_items WHERE item_type=@t ORDER BY name";
        var items = _db.Query<string>(sql, new { t = type })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();

        var issues = _db.Query<string>("SELECT data FROM stock_issues ORDER BY ts DESC")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .ToList();

        // Also include old cartridges
        if (string.IsNullOrEmpty(type) || type == "Cartridge")
        {
            var carts = _db.Query<string>("SELECT data FROM cartridges ORDER BY name")
                .Select(r => {
                    var d = JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new();
                    d["itemType"] = "Cartridge";
                    return d;
                }).ToList();
            items.AddRange(carts.Where(c => !items.Any(i => i.GetValueOrDefault("name")?.ToString() == c.GetValueOrDefault("name")?.ToString())));
        }

        ViewBag.Issues = issues;
        ViewBag.TypeFilter = type;
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
            if (week.HasValue && g.GetValueOrDefault("weekNo")?.ToString() != week.ToString()) return false;
            if (!string.IsNullOrEmpty(status) && g.GetValueOrDefault("status")?.ToString() != status) return false;
            return true;
        }).ToList();

        ViewBag.CurrentWeek = curWeek;
        ViewBag.MaxWeek = maxWeek;
        ViewBag.StatusFilter = status;
        ViewBag.Total     = filtered.Count;
        ViewBag.Completed = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed");
        ViewBag.InProg    = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress");
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
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id",
            new { d = JsonConvert.SerializeObject(g), id });
        return Json(new { ok = true });
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

        var budget = _db.KGetObj<List<Dictionary<string,object?>>>("it_budget") ?? new();
        var bills  = _db.KGetObj<List<Dictionary<string,object?>>>("bills_utilities") ?? new();
        ViewBag.Bills = bills;
        return View(budget);
    }
}
