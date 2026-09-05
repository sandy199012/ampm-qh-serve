using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class EmployeesController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public EmployeesController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? search, string? dept)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var emps = _db.GetEmployees();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            emps = emps.Where(e =>
                e.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s)==true ||
                e.GetValueOrDefault("emp")?.ToString()?.ToLower().Contains(s)==true ||
                e.GetValueOrDefault("mobile")?.ToString()?.Contains(s)==true ||
                e.GetValueOrDefault("designation")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        if (!string.IsNullOrEmpty(dept))
            emps = emps.Where(e => e.GetValueOrDefault("dept")?.ToString() == dept).ToList();

        var depts = _db.GetEmployees()
            .Select(e => e.GetValueOrDefault("dept")?.ToString() ?? "")
            .Where(d => !string.IsNullOrEmpty(d)).Distinct().OrderBy(d => d).ToList();

        ViewBag.Departments = depts;
        ViewBag.Search = search;
        ViewBag.Dept = dept;
        return View(emps);
    }

    public IActionResult Details(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (raw == null) return NotFound();
        var data = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        data["emp"] = id;
        return View(data);
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (raw == null) return NotFound();
        var data = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        data["emp"] = id;
        return View(data);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        var data = raw != null ? JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new() : new();
        
        foreach (var key in form.Keys)
            data[key] = form[key].ToString();

        _db.Execute("UPDATE employees SET data=@d WHERE emp=@e",
            new { d = JsonConvert.SerializeObject(data), e = id });
        
        TempData["Success"] = $"Employee {id} updated!";
        return RedirectToAction("Details", new { id });
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View(new Dictionary<string,object?>());
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var emp = form["emp"].ToString().Trim();
        if (string.IsNullOrEmpty(emp)) { TempData["Error"] = "Employee code required!"; return View(); }

        var data = new Dictionary<string,object?>();
        foreach (var key in form.Keys)
            data[key] = form[key].ToString();

        _db.Execute("INSERT INTO employees (emp,data,ts) VALUES (@emp,@data,@ts) ON CONFLICT (emp) DO UPDATE SET data=@data",
            new { emp, data=JsonConvert.SerializeObject(data), ts=DateTime.Now.ToString("o") });

        TempData["Success"] = $"Employee {emp} added!";
        return RedirectToAction("Index");
    }

    [HttpGet("/api/employees")]
    public IActionResult ApiList() => Json(_db.GetEmployees());
}

[HttpGet("/Employees/Export")]
public IActionResult Export()
{
    var emps = _db.GetEmployees();
    var csv = new System.Text.StringBuilder();
    csv.AppendLine("Emp Code,Name,Department,Designation,Manager,Mobile,Email,DOJ,Category,Hostname,IP,OS");
    foreach (var e in emps)
    {
        csv.AppendLine(string.Join(",",
            CsvEsc(e.GetValueOrDefault("emp")?.ToString()),
            CsvEsc(e.GetValueOrDefault("name")?.ToString()),
            CsvEsc(e.GetValueOrDefault("dept")?.ToString()),
            CsvEsc(e.GetValueOrDefault("designation")?.ToString()),
            CsvEsc(e.GetValueOrDefault("manager")?.ToString()),
            CsvEsc(e.GetValueOrDefault("mobile")?.ToString()),
            CsvEsc(e.GetValueOrDefault("email")?.ToString()),
            CsvEsc(e.GetValueOrDefault("doj")?.ToString()),
            CsvEsc(e.GetValueOrDefault("category")?.ToString()),
            CsvEsc(e.GetValueOrDefault("hostname")?.ToString()),
            CsvEsc(e.GetValueOrDefault("ip")?.ToString()),
            CsvEsc(e.GetValueOrDefault("os")?.ToString())
        ));
    }
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
    return File(bytes, "text/csv", $"Employees_{DateTime.Now:yyyyMMdd}.csv");
}

static string CsvEsc(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
