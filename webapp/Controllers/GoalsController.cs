using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class GoalsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public GoalsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(int? week, string? status)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var goals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        int maxWeek = goals.Any() ? goals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        int curWeek = week ?? maxWeek;
        var filtered = goals.Where(g => {
            if (week.HasValue && g.GetValueOrDefault("weekNo")?.ToString() != curWeek.ToString()) return false;
            if (!string.IsNullOrEmpty(status) && g.GetValueOrDefault("status")?.ToString() != status) return false;
            return true;
        }).ToList();
        var weekGoals = goals.Where(g => g.GetValueOrDefault("weekNo")?.ToString() == curWeek.ToString()).ToList();
        ViewBag.WeekStart    = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekStart")?.ToString() ?? "";
        ViewBag.WeekEnd      = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekEnd")?.ToString() ?? "";
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
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var g = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        g["status"] = "Completed"; g["progress"] = 100;
        g["completedOn"] = DateTime.Now.ToString("dd-MMM-yyyy");
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(g), id });
        return Json(new { ok=true });
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
        var maxWk = _db.Query<string>("SELECT data FROM goals ORDER BY week_no DESC")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .Select(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0)
            .DefaultIfEmpty(1).Max();

        var goal = new Dictionary<string,object?>
        {
            ["id"]          = Guid.NewGuid().ToString("N")[..8],
            ["weekNo"]      = maxWk,
            ["weekStart"]   = DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek + 1).ToString("dd-MMM-yyyy"),
            ["weekEnd"]     = DateTime.Now.AddDays(7-(int)DateTime.Now.DayOfWeek).ToString("dd-MMM-yyyy"),
            ["title"]       = form["title"].ToString(),
            ["category"]    = form["category"].ToString(),
            ["priority"]    = form["priority"].ToString(),
            ["department"]  = form["department"].ToString(),
            ["requestedBy"] = form["requestedBy"].ToString(),
            ["assignedTo"]  = "Sandy",
            ["startDate"]   = form["startDate"].ToString(),
            ["targetDate"]  = form["targetDate"].ToString(),
            ["status"]      = form["status"].ToString(),
            ["progress"]    = int.TryParse(form["progress"].ToString(), out var p) ? p : 0,
            ["remarks"]     = form["remarks"].ToString(),
        };
        _db.Execute("INSERT INTO goals (id,week_no,data,ts) VALUES (@id,@wk,@data,@ts)",
            new { id=goal["id"], wk=maxWk, data=JsonConvert.SerializeObject(goal), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = "Goal added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var g = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        g["id"] = id;
        return View(g);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        var g = raw != null ? JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new() : new();
        g["title"]      = form["title"].ToString();
        g["category"]   = form["category"].ToString();
        g["priority"]   = form["priority"].ToString();
        g["department"] = form["department"].ToString();
        g["targetDate"] = form["targetDate"].ToString();
        g["status"]     = form["status"].ToString();
        g["remarks"]    = form["remarks"].ToString();
        int.TryParse(form["progress"].ToString(), out var prog);
        g["progress"]   = prog;
        if (form["status"].ToString() == "Completed")
        {
            g["completedOn"] = DateTime.Now.ToString("dd-MMM-yyyy");
            g["progress"] = 100;
        }
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(g), id });
        TempData["Success"] = "Goal updated!";
        return RedirectToAction("Index");
    }
}
