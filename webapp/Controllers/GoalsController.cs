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
        ViewBag.CurrentWeek = curWeek;
        ViewBag.MaxWeek = maxWeek;
        ViewBag.StatusFilter = status;
        ViewBag.Total = filtered.Count;
        ViewBag.Completed = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed");
        ViewBag.InProg = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress");
        ViewBag.NotStarted = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Not Started");
        ViewBag.OnHold = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "On Hold");
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
}
