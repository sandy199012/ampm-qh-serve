using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AMPMWeb.Controllers;

public class BudgetController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public BudgetController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var budget = _db.GetBudget();
        return View(budget);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var months = new[]{"Apr26","May26","Jun26","Jul26","Aug26","Sep26","Oct26","Nov26","Dec26","Jan27","Feb27","Mar27"};
        var monthly = new JObject();
        double totalProj = 0, totalAct = 0;
        foreach(var m in months)
        {
            double.TryParse(form[$"proj_{m}"].ToString(), out var proj);
            double.TryParse(form[$"act_{m}"].ToString(), out var act);
            monthly[m] = new JObject { ["projected"] = proj, ["actual"] = act };
            totalProj += proj; totalAct += act;
        }
        var item = new Dictionary<string,object?>
        {
            ["id"]             = Guid.NewGuid().ToString("N")[..8],
            ["description"]    = form["description"].ToString(),
            ["section"]        = form["section"].ToString(),
            ["type"]           = form["type"].ToString(),
            ["monthly"]        = monthly,
            ["TotalProjected"] = totalProj,
            ["TotalActual"]    = totalAct,
            ["Variance"]       = totalProj - totalAct,
        };
        var budget = _db.GetBudget();
        budget.Add(item);
        SaveBudget(budget);
        TempData["Success"] = "Budget item added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var budget = _db.GetBudget();
        var item = budget.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var months = new[]{"Apr26","May26","Jun26","Jul26","Aug26","Sep26","Oct26","Nov26","Dec26","Jan27","Feb27","Mar27"};
        var budget = _db.GetBudget();
        var item = budget.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (item == null) return NotFound();

        var monthly = new JObject();
        double totalProj = 0, totalAct = 0;
        foreach(var m in months)
        {
            double.TryParse(form[$"proj_{m}"].ToString(), out var proj);
            double.TryParse(form[$"act_{m}"].ToString(), out var act);
            monthly[m] = new JObject { ["projected"] = proj, ["actual"] = act };
            totalProj += proj; totalAct += act;
        }
        item["description"]    = form["description"].ToString();
        item["section"]        = form["section"].ToString();
        item["monthly"]        = monthly;
        item["TotalProjected"] = totalProj;
        item["TotalActual"]    = totalAct;
        item["Variance"]       = totalProj - totalAct;

        SaveBudget(budget);
        TempData["Success"] = "Budget updated!";
        return RedirectToAction("Index");
    }

    void SaveBudget(List<Dictionary<string,object?>> budget)
    {
        _db.Execute("INSERT INTO kv (k,v) VALUES ('budget',@v) ON CONFLICT (k) DO UPDATE SET v=@v",
            new { v = JsonConvert.SerializeObject(budget) });
    }
}
