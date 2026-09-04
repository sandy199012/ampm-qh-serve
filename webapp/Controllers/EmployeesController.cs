using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class EmployeesController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;

    public EmployeesController(DbService db, AuthService auth)
    { _db = db; _auth = auth; }

    public IActionResult Index(string? search, string? dept)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);

        var emps = _db.GetEmployees();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            emps = emps.Where(e =>
                e.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s) == true ||
                e.GetValueOrDefault("emp")?.ToString()?.ToLower().Contains(s) == true ||
                e.GetValueOrDefault("dept")?.ToString()?.ToLower().Contains(s) == true
            ).ToList();
        }

        if (!string.IsNullOrEmpty(dept))
            emps = emps.Where(e => e.GetValueOrDefault("dept")?.ToString() == dept).ToList();

        var depts = _db.GetEmployees()
            .Select(e => e.GetValueOrDefault("dept")?.ToString() ?? "")
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct().OrderBy(d => d).ToList();

        ViewBag.Departments = depts;
        ViewBag.Search = search;
        ViewBag.Dept = dept;
        return View(emps);
    }

    public IActionResult Details(string id)
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var emp = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (emp == null) return NotFound();
        var data = JsonConvert.DeserializeObject<Dictionary<string,object?>>(emp) ?? new();
        data["emp"] = id;
        return View(data);
    }

    // API for AJAX
    [HttpGet("/api/employees")]
    public IActionResult ApiList()
    {
        if (!_auth.IsLoggedIn(HttpContext)) return Unauthorized();
        return Json(_db.GetEmployees());
    }
}
