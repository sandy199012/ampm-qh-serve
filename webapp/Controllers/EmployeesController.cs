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
        var depts = _db.GetEmployees().Select(e => e.GetValueOrDefault("dept")?.ToString() ?? "")
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
        foreach (var key in form.Keys) data[key] = form[key].ToString();
        _db.Execute("UPDATE employees SET data=@d WHERE emp=@e", new { d=JsonConvert.SerializeObject(data), e=id });
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
        foreach (var key in form.Keys) data[key] = form[key].ToString();
        _db.Execute("INSERT INTO employees (emp,data,ts) VALUES (@emp,@data,@ts) ON CONFLICT (emp) DO UPDATE SET data=@data",
            new { emp, data=JsonConvert.SerializeObject(data), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Employee {emp} added!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult MarkExited(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (raw == null) return NotFound();
        var data = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        data["exitDate"] = DateTime.Today.ToString("yyyy-MM-dd");
        _db.Execute("UPDATE employees SET data=@d WHERE emp=@e", new { d=JsonConvert.SerializeObject(data), e=id });
        TempData["Success"] = $"Employee {id} marked exited.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Restore(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (raw == null) return NotFound();
        var data = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        data["exitDate"] = "";
        _db.Execute("UPDATE employees SET data=@d WHERE emp=@e", new { d=JsonConvert.SerializeObject(data), e=id });
        TempData["Success"] = $"Employee {id} restored to active.";
        return RedirectToAction("Index");
    }

    [HttpGet("/Employees/Export")]
    public IActionResult Export()
    {
        var emps = _db.GetEmployees();
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Emp Code,Name,Department,Designation,Manager,Mobile,Email,DOJ,Category,Hostname,IP,OS");
        foreach (var e in emps)
            csv.AppendLine(string.Join(",",
                CsvE(e.GetValueOrDefault("emp")?.ToString()),
                CsvE(e.GetValueOrDefault("name")?.ToString()),
                CsvE(e.GetValueOrDefault("dept")?.ToString()),
                CsvE(e.GetValueOrDefault("designation")?.ToString()),
                CsvE(e.GetValueOrDefault("manager")?.ToString()),
                CsvE(e.GetValueOrDefault("mobile")?.ToString()),
                CsvE(e.GetValueOrDefault("email")?.ToString()),
                CsvE(e.GetValueOrDefault("doj")?.ToString()),
                CsvE(e.GetValueOrDefault("category")?.ToString()),
                CsvE(e.GetValueOrDefault("hostname")?.ToString()),
                CsvE(e.GetValueOrDefault("ip")?.ToString()),
                CsvE(e.GetValueOrDefault("os")?.ToString())
            ));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"Employees_{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpGet]
    public IActionResult Import()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Import(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Import"); }

        int added = 0, updated = 0, skipped = 0;
        using var reader = new StreamReader(csvFile.OpenReadStream());
        string? headerLine = await reader.ReadLineAsync();
        if (headerLine == null) { TempData["Error"] = "CSV file is empty."; return RedirectToAction("Import"); }
        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLower()).ToList();

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            var row = new Dictionary<string,string>();
            for (int i = 0; i < headers.Count && i < cols.Count; i++) row[headers[i]] = cols[i];

            string emp = row.GetValueOrDefault("emp code") ?? row.GetValueOrDefault("emp") ?? "";
            if (string.IsNullOrWhiteSpace(emp)) { skipped++; continue; }

            var existingRaw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = emp });
            var data = existingRaw != null ? JsonConvert.DeserializeObject<Dictionary<string,object?>>(existingRaw) ?? new() : new();
            bool isNew = existingRaw == null;

            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) data[dataKey] = v; }
            data["emp"] = emp;
            SetIf("name", "name");
            SetIf("department", "dept");
            SetIf("designation", "designation");
            SetIf("manager", "manager");
            SetIf("mobile", "mobile");
            SetIf("email", "email");
            SetIf("doj", "doj");
            SetIf("category", "category");
            SetIf("hostname", "hostname");
            SetIf("ip", "ip");
            SetIf("os", "os");

            _db.Execute("INSERT INTO employees (emp,data,ts) VALUES (@emp,@data,@ts) ON CONFLICT (emp) DO UPDATE SET data=@data",
                new { emp, data = JsonConvert.SerializeObject(data), ts = DateTime.Now.ToString("o") });

            if (isNew) added++; else updated++;
        }

        TempData["Success"] = $"Import complete: {added} added, {updated} updated, {skipped} skipped.";
        return RedirectToAction("Index");
    }

    static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i+1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cur.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
        }
        result.Add(cur.ToString());
        return result;
    }

    static string CsvE(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

    [HttpGet("/Employees/Handover/{id}")]
    public IActionResult Handover(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM employees WHERE emp=@e", new { e = id });
        if (raw == null) return NotFound();
        var e = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:13px;margin:30px}}
h2{{text-align:center;margin-bottom:2px}}
.sub{{text-align:center;color:#555;margin-bottom:20px;font-size:11px}}
table{{border-collapse:collapse;width:100%;margin-bottom:20px}}
td,th{{border:1px solid #333;padding:8px;font-size:12px}}
th{{background:#0A192F;color:white;text-align:left}}
.sign{{margin-top:60px;display:flex;justify-content:space-between}}
.sign div{{width:40%;border-top:1px solid #333;padding-top:6px;text-align:center;font-size:11px}}
</style></head><body>
<h2>AMPM FASHIONS PVT. LTD.</h2>
<div class='sub'>B-144, Sector 10, Noida - 201301 | GSTIN: 09AAFCA4854J1ZE</div>
<h3 style='text-align:center'>IT ASSET HANDOVER FORM</h3>
<table>
<tr><th style='width:35%'>Employee Code</th><td>{e.GetValueOrDefault("emp")}</td></tr>
<tr><th>Employee Name</th><td>{e.GetValueOrDefault("name")}</td></tr>
<tr><th>Department</th><td>{e.GetValueOrDefault("dept")}</td></tr>
<tr><th>Designation</th><td>{e.GetValueOrDefault("designation")}</td></tr>
<tr><th>Date of Joining</th><td>{e.GetValueOrDefault("doj")}</td></tr>
</table>
<table>
<tr><th colspan='2'>IT Asset Details</th></tr>
<tr><th style='width:35%'>Hostname</th><td>{e.GetValueOrDefault("hostname")}</td></tr>
<tr><th>Model</th><td>{e.GetValueOrDefault("model")}</td></tr>
<tr><th>Serial No.</th><td>{e.GetValueOrDefault("serial")}</td></tr>
<tr><th>RAM</th><td>{e.GetValueOrDefault("ram")}</td></tr>
<tr><th>OS</th><td>{e.GetValueOrDefault("os")}</td></tr>
<tr><th>IP Address</th><td>{e.GetValueOrDefault("ip")}</td></tr>
</table>
<p style='font-size:11px'>I acknowledge receipt of the above IT asset(s) in good working condition and agree to return the same upon separation from the company or upon request by the IT Department.</p>
<div class='sign'>
<div>Employee Signature &amp; Date</div>
<div>IT Department Signature &amp; Date</div>
</div>
</body></html>");
        return Content(sb.ToString(), "text/html");
    }
}
