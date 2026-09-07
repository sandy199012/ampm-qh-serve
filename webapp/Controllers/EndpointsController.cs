using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class EndpointsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public EndpointsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var endpoints = _db.KGetObj<List<Dictionary<string,object?>>>("endpoints") ?? DefaultEndpoints();
        ViewBag.PcInventory = _db.KGetObj<List<Dictionary<string,object?>>>("pc_inventory") ?? new();
        ViewBag.Licenses = _db.KGetObj<List<Dictionary<string,object?>>>("qh_licenses") ?? new();
        return View(endpoints);
    }

    [HttpPost]
    public IActionResult Save([FromBody] List<Dictionary<string,object?>> endpoints)
    {
        _db.Execute("INSERT INTO kv (k,v) VALUES ('endpoints',@v) ON CONFLICT (k) DO UPDATE SET v=@v",
            new { v = JsonConvert.SerializeObject(endpoints) });
        return Json(new { ok = true });
    }

    // ── PC Inventory & Antivirus (Quick Heal) ────────────────────
    [HttpPost]
    public IActionResult SavePcInventory([FromBody] List<Dictionary<string,object?>> pcs)
    {
        _db.KSet("pc_inventory", pcs);
        return Json(new { ok = true });
    }

    [HttpPost]
    public IActionResult SaveLicenses([FromBody] List<Dictionary<string,object?>> licenses)
    {
        _db.KSet("qh_licenses", licenses);
        return Json(new { ok = true });
    }

    // Shared key so only AMPM's own PC-inventory agent script can write here —
    // change this (and the matching value in AMPM_PC_Agent.ps1) any time it needs rotating.
    const string AgentKey = "AMPM-AGENT-2026";

    // Called by AMPM_PC_Agent.ps1/.bat — run once (or on a schedule) on any office
    // PC, it collects that PC's own hostname/IP/OS/CPU/RAM/disk and pushes it here,
    // so PC Inventory fills itself in instead of typing/CSV-importing every machine.
    // No login cookie exists for this caller, hence the shared key instead — see the
    // matching exemption in Filters/ModulePermissionFilter.cs.
    [HttpPost("/api/endpoints/report-pc")]
    public IActionResult ReportPc([FromBody] Dictionary<string,object?> data)
    {
        if (data == null || data.GetValueOrDefault("key")?.ToString() != AgentKey)
            return Unauthorized(new { ok = false, error = "Invalid or missing key" });

        var hostname = data.GetValueOrDefault("hostname")?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(hostname))
            return BadRequest(new { ok = false, error = "hostname missing" });

        var pcs = _db.KGetObj<List<Dictionary<string,object?>>>("pc_inventory") ?? new();
        var existing = pcs.FirstOrDefault(p =>
            string.Equals(p.GetValueOrDefault("hostname")?.ToString(), hostname, StringComparison.OrdinalIgnoreCase));
        var rec = existing ?? new Dictionary<string,object?>();

        void SetIf(string srcKey, string dataKey)
        {
            var v = data.GetValueOrDefault(srcKey)?.ToString();
            if (!string.IsNullOrWhiteSpace(v)) rec[dataKey] = v;
        }
        rec["hostname"] = hostname;
        SetIf("ip", "ip");
        SetIf("os", "os");
        SetIf("cpu", "cpu");
        SetIf("ramGb", "ramGb");
        SetIf("diskFree", "diskFree");
        SetIf("user", "user");
        rec["lastSeen"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (!rec.ContainsKey("qhVersion")) rec["qhVersion"] = "";
        if (!rec.ContainsKey("qhService")) rec["qhService"] = "Not Installed";
        if (!rec.ContainsKey("licenseKey")) rec["licenseKey"] = "";
        if (!rec.ContainsKey("notes")) rec["notes"] = "Auto-reported by Agent";

        if (existing == null) pcs.Add(rec);
        _db.KSet("pc_inventory", pcs);
        return Json(new { ok = true, hostname, updated = existing != null });
    }

    // Quick-start: pull hostname/IP/OS already on file in Employees into PC Inventory
    [HttpPost("/Endpoints/ImportPcFromEmployees")]
    public IActionResult ImportPcFromEmployees()
    {
        var pcs = _db.KGetObj<List<Dictionary<string,object?>>>("pc_inventory") ?? new();
        var known = new HashSet<string>(pcs.Select(p => p.GetValueOrDefault("hostname")?.ToString()?.Trim().ToLower() ?? ""));
        int added = 0;
        foreach (var e in _db.GetEmployees())
        {
            var hostname = e.GetValueOrDefault("hostname")?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(hostname) || known.Contains(hostname.ToLower())) continue;
            pcs.Add(new Dictionary<string,object?> {
                ["hostname"] = hostname,
                ["ip"] = e.GetValueOrDefault("ip")?.ToString() ?? "",
                ["os"] = e.GetValueOrDefault("os")?.ToString() ?? "",
                ["cpu"] = "",
                ["ramGb"] = e.GetValueOrDefault("ram")?.ToString() ?? "",
                ["diskFree"] = "",
                ["user"] = e.GetValueOrDefault("name")?.ToString() ?? "",
                ["qhVersion"] = "",
                ["qhService"] = "Not Installed",
                ["licenseKey"] = "",
                ["lastSeen"] = "",
                ["notes"] = $"Imported from Employee {e.GetValueOrDefault("emp")}"
            });
            known.Add(hostname.ToLower());
            added++;
        }
        _db.KSet("pc_inventory", pcs);
        TempData["Success"] = added > 0 ? $"{added} PC(s) imported from Employees." : "No new hostnames found in Employees to import.";
        return RedirectToAction("Index");
    }

    // Bulk CSV import — same column headers as ExportPcInventory. Matches by Hostname (updates if exists, adds if new).
    [HttpPost("/Endpoints/ImportPcCsv")]
    public async Task<IActionResult> ImportPcCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Index"); }
        var pcs = _db.KGetObj<List<Dictionary<string,object?>>>("pc_inventory") ?? new();
        int added = 0, updated = 0, skipped = 0;
        using var reader = new StreamReader(csvFile.OpenReadStream());
        string? headerLine = await reader.ReadLineAsync();
        if (headerLine == null) { TempData["Error"] = "CSV file is empty."; return RedirectToAction("Index"); }
        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLower()).ToList();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            var row = new Dictionary<string,string>();
            for (int i = 0; i < headers.Count && i < cols.Count; i++) row[headers[i]] = cols[i];
            string hostname = row.GetValueOrDefault("hostname") ?? "";
            if (string.IsNullOrWhiteSpace(hostname)) { skipped++; continue; }
            var existing = pcs.FirstOrDefault(p => string.Equals(p.GetValueOrDefault("hostname")?.ToString(), hostname, StringComparison.OrdinalIgnoreCase));
            var rec = existing ?? new Dictionary<string,object?>();
            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) rec[dataKey] = v; }
            rec["hostname"] = hostname;
            SetIf("ip address", "ip");
            SetIf("os", "os");
            SetIf("cpu", "cpu");
            SetIf("ram gb", "ramGb");
            SetIf("disk free", "diskFree");
            SetIf("user", "user");
            SetIf("qh version", "qhVersion");
            SetIf("qh service", "qhService");
            SetIf("license key", "licenseKey");
            SetIf("last seen", "lastSeen");
            SetIf("notes", "notes");
            if (existing == null) { pcs.Add(rec); added++; } else updated++;
        }
        _db.KSet("pc_inventory", pcs);
        TempData["Success"] = $"PC Inventory import complete: {added} added, {updated} updated, {skipped} skipped.";
        return RedirectToAction("Index");
    }

    // Bulk CSV import — same column headers as ExportLicenses (Status column is ignored, it's auto-computed). Matches by License Key.
    [HttpPost("/Endpoints/ImportLicensesCsv")]
    public async Task<IActionResult> ImportLicensesCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Index"); }
        var lics = _db.KGetObj<List<Dictionary<string,object?>>>("qh_licenses") ?? new();
        int added = 0, updated = 0, skipped = 0;
        using var reader = new StreamReader(csvFile.OpenReadStream());
        string? headerLine = await reader.ReadLineAsync();
        if (headerLine == null) { TempData["Error"] = "CSV file is empty."; return RedirectToAction("Index"); }
        var headers = ParseCsvLine(headerLine).Select(h => h.Trim().ToLower()).ToList();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            var row = new Dictionary<string,string>();
            for (int i = 0; i < headers.Count && i < cols.Count; i++) row[headers[i]] = cols[i];
            string key = row.GetValueOrDefault("license key") ?? "";
            if (string.IsNullOrWhiteSpace(key)) { skipped++; continue; }
            var existing = lics.FirstOrDefault(l => string.Equals(l.GetValueOrDefault("licenseKey")?.ToString(), key, StringComparison.OrdinalIgnoreCase));
            var rec = existing ?? new Dictionary<string,object?>();
            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) rec[dataKey] = v; }
            rec["licenseKey"] = key;
            SetIf("product", "product");
            SetIf("assigned to", "hostname");
            SetIf("ip address", "ip");
            SetIf("logged user", "loggedUser");
            SetIf("assigned on", "assignedOn");
            SetIf("purchase date", "purchaseDate");
            SetIf("expiry date", "expiryDate");
            SetIf("notes", "notes");
            if (existing == null) { lics.Add(rec); added++; } else updated++;
        }
        _db.KSet("qh_licenses", lics);
        TempData["Success"] = $"License import complete: {added} added, {updated} updated, {skipped} skipped.";
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

    [HttpGet("/Endpoints/ExportPcInventory")]
    public IActionResult ExportPcInventory()
    {
        var pcs = _db.KGetObj<List<Dictionary<string,object?>>>("pc_inventory") ?? new();
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Hostname,IP Address,OS,CPU,RAM GB,Disk Free,User,QH Version,QH Service,License Key,Last Seen,Notes");
        foreach (var p in pcs)
            csv.AppendLine(string.Join(",",
                CsvE(p.GetValueOrDefault("hostname")?.ToString()),
                CsvE(p.GetValueOrDefault("ip")?.ToString()),
                CsvE(p.GetValueOrDefault("os")?.ToString()),
                CsvE(p.GetValueOrDefault("cpu")?.ToString()),
                CsvE(p.GetValueOrDefault("ramGb")?.ToString()),
                CsvE(p.GetValueOrDefault("diskFree")?.ToString()),
                CsvE(p.GetValueOrDefault("user")?.ToString()),
                CsvE(p.GetValueOrDefault("qhVersion")?.ToString()),
                CsvE(p.GetValueOrDefault("qhService")?.ToString()),
                CsvE(p.GetValueOrDefault("licenseKey")?.ToString()),
                CsvE(p.GetValueOrDefault("lastSeen")?.ToString()),
                CsvE(p.GetValueOrDefault("notes")?.ToString())
            ));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"AMPM_PCInventory_{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpGet("/Endpoints/ExportLicenses")]
    public IActionResult ExportLicenses()
    {
        var lics = _db.KGetObj<List<Dictionary<string,object?>>>("qh_licenses") ?? new();
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Status,License Key,Product,Assigned To,IP Address,Logged User,Assigned On,Purchase Date,Expiry Date,Notes");
        foreach (var l in lics)
            csv.AppendLine(string.Join(",",
                CsvE(ComputeLicStatus(l)),
                CsvE(l.GetValueOrDefault("licenseKey")?.ToString()),
                CsvE(l.GetValueOrDefault("product")?.ToString()),
                CsvE(l.GetValueOrDefault("hostname")?.ToString()),
                CsvE(l.GetValueOrDefault("ip")?.ToString()),
                CsvE(l.GetValueOrDefault("loggedUser")?.ToString()),
                CsvE(l.GetValueOrDefault("assignedOn")?.ToString()),
                CsvE(l.GetValueOrDefault("purchaseDate")?.ToString()),
                CsvE(l.GetValueOrDefault("expiryDate")?.ToString()),
                CsvE(l.GetValueOrDefault("notes")?.ToString())
            ));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"AMPM_QHLicenses_{DateTime.Now:yyyyMMdd}.csv");
    }

    static string ComputeLicStatus(Dictionary<string,object?> l)
    {
        var hostname = l.GetValueOrDefault("hostname")?.ToString();
        if (DateTime.TryParse(l.GetValueOrDefault("expiryDate")?.ToString(), out var exp) && exp.Date < DateTime.Today)
            return "Expired";
        return string.IsNullOrEmpty(hostname) ? "Unassigned" : "Assigned";
    }

    static string CsvE(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

    [HttpGet("/api/endpoints/check")]
    public async Task<IActionResult> Check(string url)
    {
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await http.GetAsync(url);
            sw.Stop();
            return Json(new { ok=true, status=(int)resp.StatusCode, ms=sw.ElapsedMilliseconds });
        } catch (Exception ex) {
            return Json(new { ok=false, error=ex.Message });
        }
    }

    static List<Dictionary<string,object?>> DefaultEndpoints() => new()
    {
        new() { ["name"]="AMPM IT Tool", ["url"]="https://ampm-qh-serve-1.onrender.com", ["category"]="Internal", ["enabled"]=true },
        new() { ["name"]="QH Monitor", ["url"]="https://ampm-qh-serve.onrender.com", ["category"]="Internal", ["enabled"]=true },
        new() { ["name"]="Google", ["url"]="https://www.google.com", ["category"]="External", ["enabled"]=true },
        new() { ["name"]="Supabase", ["url"]="https://supabase.com", ["category"]="Cloud", ["enabled"]=true },
    };
}
