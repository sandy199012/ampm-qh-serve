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

    static readonly string[] Months = {"Apr26","May26","Jun26","Jul26","Aug26","Sep26","Oct26","Nov26","Dec26","Jan27","Feb27","Mar27"};

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
        var monthly = new JObject();
        double totalProj = 0, totalAct = 0;
        foreach(var m in Months)
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
        var budget = _db.GetBudget();
        var item = budget.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (item == null) return NotFound();

        var monthly = new JObject();
        double totalProj = 0, totalAct = 0;
        foreach(var m in Months)
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

    [HttpGet("/Budget/Export")]
    public IActionResult Export()
    {
        var budget = _db.GetBudget();
        var sections = budget.Select(b => b.GetValueOrDefault("section")?.ToString() ?? "Other").Distinct().OrderBy(s => s).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:10px}}table{{border-collapse:collapse;width:100%;margin-bottom:16px}}
th{{background:#0A192F;color:white;padding:4px;text-align:center;font-size:9px;border:1px solid #1e3a5f}}
td{{padding:4px;border:1px solid #CBD5E1;font-size:9px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
.h2{{background:#1e3a5f;color:#06B6D4;padding:5px 10px;margin-top:10px;font-weight:bold}}
.green{{color:#059669;font-weight:bold}}.red{{color:#DC2626;font-weight:bold}}
</style></head><body>
<table><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — IT BUDGET 2026-27</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>");

        foreach (var section in sections)
        {
            var items = budget.Where(b => b.GetValueOrDefault("section")?.ToString() == section).ToList();
            sb.Append($"<div class='h2'>{section}</div><table><thead><tr><th>Description</th>");
            foreach (var m in Months) sb.Append($"<th>{m} Proj</th><th>{m} Act</th>");
            sb.Append("<th>Total Proj</th><th>Total Act</th><th>Variance</th></tr></thead><tbody>");

            foreach (var item in items)
            {
                var monthly = item.GetValueOrDefault("monthly") as JObject;
                double.TryParse(item.GetValueOrDefault("TotalProjected")?.ToString(), out var tp);
                double.TryParse(item.GetValueOrDefault("TotalActual")?.ToString(), out var ta);
                double variance = tp - ta;
                sb.Append($"<tr><td><b>{item.GetValueOrDefault("description")}</b></td>");
                foreach (var m in Months)
                {
                    double proj = 0, act = 0;
                    if (monthly?[m] is JObject mo)
                    {
                        double.TryParse(mo["projected"]?.ToString(), out proj);
                        double.TryParse(mo["actual"]?.ToString(), out act);
                    }
                    sb.Append($"<td style='text-align:center'>{proj:N0}</td><td style='text-align:center'>{act:N0}</td>");
                }
                string vc = variance >= 0 ? "green" : "red";
                sb.Append($"<td style='text-align:right'>₹{tp:N0}</td><td style='text-align:right'>₹{ta:N0}</td><td style='text-align:right' class='{vc}'>₹{variance:N0}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("</body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Budget_{DateTime.Now:yyyyMMdd}.xls");
    }

    void SaveBudget(List<Dictionary<string,object?>> budget) => _db.SaveBudget(budget);
}
