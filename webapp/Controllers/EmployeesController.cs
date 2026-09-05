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
        string S(string k) => e.GetValueOrDefault(k)?.ToString() ?? "";

        var assignedAssets = _db.GetAssets()
            .Where(a => a.GetValueOrDefault("assignedToEmp")?.ToString() == id)
            .ToList();

        // Legacy fallback: some old employee records carry a single asset's fields
        // directly (hostname/model/serial) instead of a linked Assets record.
        bool hasLegacyAsset = !assignedAssets.Any() &&
            (!string.IsNullOrEmpty(S("hostname")) || !string.IsNullOrEmpty(S("model")) || !string.IsNullOrEmpty(S("serial")));

        var sb = new System.Text.StringBuilder();
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Asset Handover Form</title>
<style>
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#1E293B;margin:0;background:#F1F5F9;font-size:12.5px}
.wrap{max-width:800px;margin:26px auto;background:#fff;border:1px solid #CBD5E1;border-radius:6px;overflow:hidden}
.pad{padding:0 22px 20px 22px}
.header{background:#0A192F;color:#fff;padding:16px 22px;display:flex;justify-content:space-between;align-items:center}
.header .co{font-size:16px;font-weight:800;letter-spacing:.3px}
.header .sub{font-size:10.5px;color:#5EEAD4;margin-top:3px}
.header .meta{font-size:12px;text-align:right;line-height:1.6}

.sec-hdr{background:#1e3a5f;color:#fff;font-size:10.5px;font-weight:700;letter-spacing:.6px;padding:7px 14px;margin-top:16px}
table.kv{width:100%;border-collapse:collapse;border:1px solid #E2E8F0;border-top:none}
table.kv td{padding:7px 14px;font-size:12px;border-bottom:1px solid #F1F5F9}
table.kv tr:last-child td{border-bottom:none}
table.kv td.k{width:32%;font-weight:700;color:#334155}
table.kv td.v{color:#0F172A}

table.items{width:100%;border-collapse:collapse;border:1px solid #E2E8F0;border-top:none}
table.items th{background:#F8FAFC;color:#64748B;font-size:10px;letter-spacing:.4px;text-align:left;padding:7px 10px;border-bottom:2px solid #E2E8F0}
table.items td{padding:7px 10px;font-size:11.5px;border-bottom:1px solid #F1F5F9}
table.items tr:last-child td{border-bottom:none}
.none-row{text-align:center;color:#94A3B8;font-style:italic;padding:14px}

.ack{margin-top:16px;background:#FFFBEB;border:1.5px solid #FDE68A;border-radius:6px;padding:12px 14px;font-size:11px;color:#78350F;line-height:1.6}

.sig-row{display:flex;gap:12px;margin-top:38px}
.sig-box{flex:1;border:1px solid #CBD5E1;border-radius:4px;padding:22px 8px 8px 8px;text-align:center}
.sig-line{border-top:1px solid #94A3B8;margin:0 12px 8px 12px}
.sig-name{font-size:11px;font-weight:700;color:#0F172A}
.sig-pre{font-size:9.5px;color:#94A3B8;margin-top:2px}

.footer{margin-top:18px;font-size:9px;color:#94A3B8;text-align:center}
.no-print{text-align:center;margin:16px 0}
.no-print button{padding:8px 20px;background:#0A192F;color:#fff;border:none;border-radius:4px;cursor:pointer;font-size:12.5px}
@media print{.no-print{display:none}body{background:#fff}.wrap{border:none;margin:0;max-width:100%}}
</style></head><body>

<div class='no-print'><button onclick='window.print()'>Print / Save as PDF</button></div>

<div class='wrap'>
  <div class='header'>
    <div>
      <div class='co'>AMPM FASHIONS PVT. LTD.</div>
      <div class='sub'>IT Department — Asset Handover Form</div>
    </div>
    <div class='meta'>Handover Date: <b>").Append(DateTime.Now.ToString("dd-MMM-yyyy")).Append(@"</b></div>
  </div>
  <div class='pad'>

    <div class='sec-hdr'>EMPLOYEE DETAILS</div>
    <table class='kv'>
      <tr><td class='k'>Employee Code</td><td class='v'>").Append(S("emp")).Append(@"</td></tr>
      <tr><td class='k'>Employee Name</td><td class='v'>").Append(S("name").ToUpper()).Append(@"</td></tr>
      <tr><td class='k'>Department</td><td class='v'>").Append(S("dept")).Append(@"</td></tr>
      <tr><td class='k'>Designation</td><td class='v'>").Append(S("designation")).Append(@"</td></tr>
      <tr><td class='k'>Reporting Manager</td><td class='v'>").Append(S("manager")).Append(@"</td></tr>
      <tr><td class='k'>Date of Joining</td><td class='v'>").Append(S("doj")).Append(@"</td></tr>
      <tr><td class='k'>Mobile</td><td class='v'>").Append(S("mobile")).Append(@"</td></tr>
    </table>

    <div class='sec-hdr'>ASSETS ASSIGNED (").Append(hasLegacyAsset ? 1 : assignedAssets.Count).Append(@")</div>
    <table class='items'>
      <thead><tr><th>Asset Tag</th><th>Type</th><th>Brand / Model</th><th>Serial No.</th><th>Condition</th><th>Assigned Date</th></tr></thead>
      <tbody>");
        if (assignedAssets.Any())
        {
            foreach (var a in assignedAssets)
            {
                sb.Append("<tr><td><b>").Append(a.GetValueOrDefault("assetTag")).Append("</b></td><td>")
                  .Append(a.GetValueOrDefault("assetType")).Append("</td><td>")
                  .Append(a.GetValueOrDefault("brand")).Append(' ').Append(a.GetValueOrDefault("model")).Append("</td><td>")
                  .Append(a.GetValueOrDefault("serial")).Append("</td><td>")
                  .Append(a.GetValueOrDefault("condition")).Append("</td><td>")
                  .Append(a.GetValueOrDefault("assignedDate")).Append("</td></tr>");
            }
        }
        else if (hasLegacyAsset)
        {
            sb.Append("<tr><td><b>—</b></td><td>").Append(S("hostname")).Append("</td><td>")
              .Append(S("model")).Append("</td><td>").Append(S("serial")).Append("</td><td>—</td><td>—</td></tr>");
        }
        else
        {
            sb.Append("<tr><td colspan='6' class='none-row'>No assets currently linked to this employee in the system</td></tr>");
        }
        sb.Append(@"</tbody>
    </table>

    <div class='ack'><b>Acknowledgement:</b> I acknowledge receipt of the above IT asset(s) in good working condition and agree to return the same upon separation from the company, transfer, or upon request by the IT Department.</div>

    <div class='sig-row'>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Employee Signature</div><div class='sig-pre'>").Append(S("name").ToUpper()).Append(@"</div></div>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>IT Department</div><div class='sig-pre'>Sandeep Kumar Singh Kushwaha</div></div>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>HOD / Manager</div><div class='sig-pre'>").Append(S("manager")).Append(@"</div></div>
    </div>

    <div class='footer'>Printed: ").Append(DateTime.Now.ToString("dd MMM yyyy HH:mm")).Append(@" &nbsp;|&nbsp; AMPM Fashions Pvt. Ltd, B-144, Sector 10, Noida - 201301 &nbsp;|&nbsp; IT Department</div>

  </div>
</div>

</body></html>");
        return Content(sb.ToString(), "text/html");
    }
}
