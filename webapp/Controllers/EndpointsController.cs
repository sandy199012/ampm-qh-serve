using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ClosedXML.Excel;

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
        ViewBag.SoftwareRegister = _db.KGetObj<List<Dictionary<string,object?>>>("software_register") ?? new();
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

    // ── IT Software Register (separate from Quick Heal licenses above) ──
    [HttpPost]
    public IActionResult SaveSoftwareRegister([FromBody] List<Dictionary<string,object?>> softwareRegister)
    {
        _db.KSet("software_register", softwareRegister);
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
        // Installed-software list (array of {name, version}) — collected by the
        // agent from the Windows Uninstall registry keys. Stored as-is; the
        // PC Inventory view renders it in a per-PC "Installed Software" modal.
        if (data.TryGetValue("software", out var sw) && sw != null) rec["software"] = sw;
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

    // Bulk CSV import for the IT Software Register — same column headers as
    // ExportSoftwareRegister (Utilization %, Days to Renewal and Renewal Flag are
    // ignored — they're auto-computed). Matches by Record ID; a blank/unmatched
    // Record ID is added as a new row with a freshly generated one.
    [HttpPost("/Endpoints/ImportSoftwareRegisterCsv")]
    public async Task<IActionResult> ImportSoftwareRegisterCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Index"); }
        var lics = _db.KGetObj<List<Dictionary<string,object?>>>("software_register") ?? new();
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

            string recordId = row.GetValueOrDefault("record id") ?? "";
            string software = row.GetValueOrDefault("software / application") ?? row.GetValueOrDefault("software") ?? "";
            if (string.IsNullOrWhiteSpace(software)) { skipped++; continue; }

            var existing = !string.IsNullOrWhiteSpace(recordId)
                ? lics.FirstOrDefault(l => string.Equals(l.GetValueOrDefault("recordId")?.ToString(), recordId, StringComparison.OrdinalIgnoreCase))
                : null;
            var rec = existing ?? new Dictionary<string,object?>();
            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) rec[dataKey] = v; }

            rec["recordId"] = string.IsNullOrWhiteSpace(recordId) ? NextSoftwareRegisterId(lics) : recordId;
            rec["software"] = software;
            SetIf("category", "category");
            SetIf("deployment", "deployment");
            SetIf("purpose / module", "purpose");
            SetIf("vendor", "vendor");
            SetIf("business owner", "businessOwner");
            SetIf("it owner", "itOwner");
            SetIf("department", "department");
            SetIf("license type", "licenseType");
            SetIf("purchased licenses", "purchasedLicenses");
            SetIf("assigned licenses", "assignedLicenses");
            SetIf("version / plan", "versionPlan");
            SetIf("start date", "startDate");
            SetIf("renewal / support due", "renewalDate");
            SetIf("annual support cost (inr)", "annualCost");
            SetIf("payment frequency", "paymentFrequency");
            SetIf("auto-renew", "autoRenew");
            SetIf("criticality", "criticality");
            SetIf("data sensitivity", "dataSensitivity");
            SetIf("sso / mfa", "ssoMfa");
            SetIf("contract / po no.", "contractPo");
            SetIf("invoice no.", "invoiceNo");
            SetIf("status", "status");
            SetIf("risk / issue", "riskIssue");
            SetIf("action required", "actionRequired");
            SetIf("remarks", "remarks");
            if (existing == null) { lics.Add(rec); added++; } else updated++;
        }
        _db.KSet("software_register", lics);
        TempData["Success"] = $"Import complete: {added} added, {updated} updated, {skipped} skipped.";
        return RedirectToAction("Index");
    }

    static string NextSoftwareRegisterId(List<Dictionary<string,object?>> lics)
    {
        int max = 0;
        foreach (var l in lics)
        {
            var id = l.GetValueOrDefault("recordId")?.ToString() ?? "";
            if (id.StartsWith("SW-") && int.TryParse(id.Substring(3), out var n)) max = Math.Max(max, n);
        }
        return $"SW-{(max + 1):D3}";
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

    // Real .xlsx report generated from Sandy's own AMPM_IT_Reporting_Template.xlsx
    // (embedded into the app at build time — see the csproj). We only fill in the
    // data cells of the "Software Register" sheet; every sheet (Dashboard, Software
    // Register, Online Subscriptions, IT Spend Log, Lists), formula, style, column
    // width and dropdown stays exactly as in the original workbook — the Dashboard's
    // totals/renewal counts recalculate on their own from the data we write in.
    [HttpGet("/Endpoints/ExportSoftwareRegister")]
    public IActionResult ExportSoftwareRegister()
    {
        var lics = _db.KGetObj<List<Dictionary<string,object?>>>("software_register") ?? new();
        string S(Dictionary<string,object?> l, string k) => l.GetValueOrDefault(k)?.ToString() ?? "";

        var asm = typeof(EndpointsController).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("AMPM_IT_Reporting_Template.xlsx", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return StatusCode(500, "Report template is missing from the app build.");

        using var templateStream = asm.GetManifestResourceStream(resourceName)!;
        using var wb = new XLWorkbook(templateStream);
        var ws = wb.Worksheet("Software Register");

        // The template's SoftwareRegisterTable and its per-row formulas
        // (Utilization %, Days to Renewal, Renewal Flag) run from row 7 to row 206 —
        // we only ever write into the data columns of that same range.
        const int firstRow = 7, lastRow = 206;
        int row = firstRow;
        foreach (var l in lics)
        {
            if (row > lastRow) break; // template's pre-built rows are full

            ws.Cell(row, 1).Value = S(l, "recordId");            // A Record ID
            ws.Cell(row, 2).Value = S(l, "software");            // B Software / Application
            ws.Cell(row, 3).Value = S(l, "category");            // C Category
            ws.Cell(row, 4).Value = S(l, "deployment");          // D Deployment
            ws.Cell(row, 5).Value = S(l, "purpose");             // E Purpose / Module
            ws.Cell(row, 6).Value = S(l, "vendor");              // F Vendor
            ws.Cell(row, 7).Value = S(l, "businessOwner");       // G Business Owner
            ws.Cell(row, 8).Value = S(l, "itOwner");             // H IT Owner
            ws.Cell(row, 9).Value = S(l, "department");          // I Department
            ws.Cell(row, 10).Value = S(l, "licenseType");        // J License Type
            if (double.TryParse(S(l, "purchasedLicenses"), out var purchased)) ws.Cell(row, 11).Value = purchased; // K
            if (double.TryParse(S(l, "assignedLicenses"), out var assigned)) ws.Cell(row, 12).Value = assigned;    // L
            // column 13 (M) = Utilization % — template formula, left untouched
            ws.Cell(row, 14).Value = S(l, "versionPlan");        // N Version / Plan
            if (DateTime.TryParse(S(l, "startDate"), out var startDate)) ws.Cell(row, 15).Value = startDate;       // O
            if (DateTime.TryParse(S(l, "renewalDate"), out var renewalDate)) ws.Cell(row, 16).Value = renewalDate; // P
            if (double.TryParse(S(l, "annualCost"), out var annualCost)) ws.Cell(row, 17).Value = annualCost;      // Q
            ws.Cell(row, 18).Value = S(l, "paymentFrequency");   // R Payment Frequency
            ws.Cell(row, 19).Value = S(l, "autoRenew");          // S Auto-Renew
            ws.Cell(row, 20).Value = S(l, "criticality");        // T Criticality
            ws.Cell(row, 21).Value = S(l, "dataSensitivity");    // U Data Sensitivity
            ws.Cell(row, 22).Value = S(l, "ssoMfa");             // V SSO / MFA
            ws.Cell(row, 23).Value = S(l, "contractPo");         // W Contract / PO No.
            ws.Cell(row, 24).Value = S(l, "invoiceNo");          // X Invoice No.
            ws.Cell(row, 25).Value = S(l, "status");             // Y Status
            // columns 26 (Z) / 27 (AA) = Days to Renewal / Renewal Flag — template formulas, left untouched
            ws.Cell(row, 28).Value = S(l, "riskIssue");          // AB Risk / Issue
            ws.Cell(row, 29).Value = S(l, "actionRequired");     // AC Action Required
            ws.Cell(row, 30).Value = S(l, "remarks");            // AD Remarks
            row++;
        }

        using var outStream = new MemoryStream();
        wb.SaveAs(outStream);
        return File(outStream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AMPM_IT_Software_Register_{DateTime.Now:yyyyMMdd}.xlsx");
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

    // ── One-time repair for data corrupted by the JsonElement/[FromBody] bug
    // (values that got saved as e.g. {"ValueKind":3} instead of plain text).
    // The original text in those fields is unrecoverable — this just clears
    // the garbage back to "" so the tables render normally again; re-enter
    // or re-import the real values afterward. Safe to run any time: it only
    // touches fields that actually show the corruption signature.
    [HttpPost("/Endpoints/RepairData")]
    public IActionResult RepairData()
    {
        int fixedCount = 0;
        fixedCount += RepairCorruptedStrings("qh_licenses");
        fixedCount += RepairCorruptedStrings("software_register");
        fixedCount += RepairCorruptedStrings("pc_inventory");
        fixedCount += RepairCorruptedStrings("endpoints");
        TempData["Success"] = fixedCount > 0
            ? $"Repaired {fixedCount} corrupted field(s). The original text in those fields could not be recovered — please re-enter or re-import them."
            : "No corrupted data found — nothing to repair.";
        return RedirectToAction("Index");
    }

    int RepairCorruptedStrings(string kvKey)
    {
        var list = _db.KGetObj<List<Dictionary<string,object?>>>(kvKey);
        if (list == null) return 0;
        int fixedCount = 0;
        foreach (var rec in list)
        {
            foreach (var k in rec.Keys.ToList())
            {
                if (rec[k] is JObject jo && jo.ContainsKey("ValueKind"))
                {
                    rec[k] = "";
                    fixedCount++;
                }
            }
        }
        if (fixedCount > 0) _db.KSet(kvKey, list);
        return fixedCount;
    }

    static List<Dictionary<string,object?>> DefaultEndpoints() => new()
    {
        new() { ["name"]="AMPM IT Tool", ["url"]="https://ampm-qh-serve-1.onrender.com", ["category"]="Internal", ["enabled"]=true },
        new() { ["name"]="QH Monitor", ["url"]="https://ampm-qh-serve.onrender.com", ["category"]="Internal", ["enabled"]=true },
        new() { ["name"]="Google", ["url"]="https://www.google.com", ["category"]="External", ["enabled"]=true },
        new() { ["name"]="Supabase", ["url"]="https://supabase.com", ["category"]="Cloud", ["enabled"]=true },
    };
}
