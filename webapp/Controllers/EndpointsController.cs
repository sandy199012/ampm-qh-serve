using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

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
        ViewBag.OnlineSubscriptions = _db.KGetObj<List<Dictionary<string,object?>>>("online_subscriptions") ?? new();
        ViewBag.ITSpendLog = _db.KGetObj<List<Dictionary<string,object?>>>("it_spend_log") ?? new();
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

    // ── Online Subscriptions (separate tab/KV store) ──
    [HttpPost]
    public IActionResult SaveOnlineSubscriptions([FromBody] List<Dictionary<string,object?>> onlineSubscriptions)
    {
        _db.KSet("online_subscriptions", onlineSubscriptions);
        return Json(new { ok = true });
    }

    // ── IT Spend Log (separate tab/KV store) ──
    [HttpPost]
    public IActionResult SaveITSpendLog([FromBody] List<Dictionary<string,object?>> itSpendLog)
    {
        _db.KSet("it_spend_log", itSpendLog);
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
        rec["lastSeen"] = IstTime.Now.ToString("yyyy-MM-dd HH:mm");
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

    // Bulk CSV import for Online Subscriptions — same columns as the Online
    // Subscriptions sheet in ExportITReport (computed columns like Utilization %,
    // Billing Amount INR, Annualized Cost INR, Days to Renewal, Renewal Flag are
    // ignored — they're auto-computed). Matches by Subscription ID; a blank/unmatched
    // Subscription ID is added as a new row with a freshly generated one.
    [HttpPost("/Endpoints/ImportOnlineSubscriptionsCsv")]
    public async Task<IActionResult> ImportOnlineSubscriptionsCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Index"); }
        var subs = _db.KGetObj<List<Dictionary<string,object?>>>("online_subscriptions") ?? new();
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

            string subId = row.GetValueOrDefault("subscription id") ?? "";
            string tool = row.GetValueOrDefault("tool / platform") ?? row.GetValueOrDefault("tool/platform") ?? "";
            if (string.IsNullOrWhiteSpace(tool)) { skipped++; continue; }

            var existing = !string.IsNullOrWhiteSpace(subId)
                ? subs.FirstOrDefault(l => string.Equals(l.GetValueOrDefault("subscriptionId")?.ToString(), subId, StringComparison.OrdinalIgnoreCase))
                : null;
            var rec = existing ?? new Dictionary<string,object?>();
            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) rec[dataKey] = v; }

            rec["subscriptionId"] = string.IsNullOrWhiteSpace(subId) ? NextOnlineSubId(subs) : subId;
            rec["toolPlatform"] = tool;
            SetIf("subscription type", "subscriptionType");
            SetIf("use case", "useCase");
            SetIf("vendor", "vendor");
            SetIf("website", "website");
            SetIf("business owner", "businessOwner");
            SetIf("admin account/email", "adminAccount");
            SetIf("department", "department");
            SetIf("plan", "plan");
            SetIf("seats", "seats");
            SetIf("active users", "activeUsers");
            SetIf("billing currency", "billingCurrency");
            SetIf("billing amount", "billingAmount");
            SetIf("fx rate to inr", "fxRate");
            SetIf("billing frequency", "billingFrequency");
            SetIf("start date", "startDate");
            SetIf("renewal date", "renewalDate");
            SetIf("auto-renew", "autoRenew");
            SetIf("payment mode", "paymentMode");
            SetIf("card/bank last 4", "cardLast4");
            SetIf("gst/tax credit", "gstTaxCredit");
            SetIf("data shared", "dataShared");
            SetIf("ai data retention/training", "aiDataRetention");
            SetIf("security review", "securityReview");
            SetIf("criticality", "criticality");
            SetIf("status", "status");
            SetIf("duplicate/overlap", "duplicateOverlap");
            SetIf("cost saving action", "costSavingAction");
            SetIf("remarks", "remarks");
            if (existing == null) { subs.Add(rec); added++; } else updated++;
        }
        _db.KSet("online_subscriptions", subs);
        TempData["Success"] = $"Import complete: {added} added, {updated} updated, {skipped} skipped.";
        return RedirectToAction("Index");
    }

    static string NextOnlineSubId(List<Dictionary<string,object?>> subs)
    {
        int max = 0;
        foreach (var l in subs)
        {
            var id = l.GetValueOrDefault("subscriptionId")?.ToString() ?? "";
            if (id.StartsWith("OS-") && int.TryParse(id.Substring(3), out var n)) max = Math.Max(max, n);
        }
        return $"OS-{(max + 1):D3}";
    }

    // Bulk CSV import for the IT Spend Log. This is a transactional log (not a
    // register), so every row is always appended as a new entry — there's no
    // upsert/matching by ID, unlike the other imports above.
    [HttpPost("/Endpoints/ImportITSpendLogCsv")]
    public async Task<IActionResult> ImportITSpendLogCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) { TempData["Error"] = "Choose a CSV file first."; return RedirectToAction("Index"); }
        var log = _db.KGetObj<List<Dictionary<string,object?>>>("it_spend_log") ?? new();
        int added = 0, skipped = 0;
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

            string vendor = row.GetValueOrDefault("vendor") ?? "";
            string tool = row.GetValueOrDefault("tool / software") ?? row.GetValueOrDefault("tool/software") ?? "";
            if (string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(tool)) { skipped++; continue; }

            var rec = new Dictionary<string,object?>();
            void SetIf(string csvKey, string dataKey) { if (row.TryGetValue(csvKey, out var v) && !string.IsNullOrWhiteSpace(v)) rec[dataKey] = v; }

            var expenseId = row.GetValueOrDefault("expense id") ?? "";
            rec["expenseId"] = string.IsNullOrWhiteSpace(expenseId) ? NextExpenseId(log) : expenseId;
            SetIf("expense date", "expenseDate");
            rec["vendor"] = vendor;
            rec["toolSoftware"] = tool;
            SetIf("subscription type", "subscriptionType");
            SetIf("cost category", "costCategory");
            SetIf("department", "department");
            SetIf("invoice no.", "invoiceNo");
            SetIf("po/contract no.", "poContractNo");
            SetIf("base amount (inr)", "baseAmount");
            SetIf("gst (inr)", "gst");
            SetIf("tds (inr)", "tds");
            SetIf("payment date", "paymentDate");
            SetIf("payment status", "paymentStatus");
            SetIf("payment mode", "paymentMode");
            SetIf("budget amount (inr)", "budgetAmount");
            SetIf("capex/opex", "capexOpex");
            SetIf("cost center", "costCenter");
            SetIf("remarks", "remarks");
            log.Add(rec);
            added++;
        }
        _db.KSet("it_spend_log", log);
        TempData["Success"] = $"Import complete: {added} added, {skipped} skipped.";
        return RedirectToAction("Index");
    }

    static string NextExpenseId(List<Dictionary<string,object?>> log)
    {
        int max = 0;
        foreach (var l in log)
        {
            var id = l.GetValueOrDefault("expenseId")?.ToString() ?? "";
            if (id.StartsWith("EXP-") && int.TryParse(id.Substring(4), out var n)) max = Math.Max(max, n);
        }
        return $"EXP-{(max + 1):D4}";
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
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"AMPM_PCInventory_{IstTime.Now:yyyyMMdd}.csv");
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
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"AMPM_QHLicenses_{IstTime.Now:yyyyMMdd}.csv");
    }

    static string ComputeLicStatus(Dictionary<string,object?> l)
    {
        var hostname = l.GetValueOrDefault("hostname")?.ToString();
        if (DateTime.TryParse(l.GetValueOrDefault("expiryDate")?.ToString(), out var exp) && exp.Date < IstTime.Today)
            return "Expired";
        return string.IsNullOrEmpty(hostname) ? "Unassigned" : "Assigned";
    }

    // Real .xlsx report generated from Sandy's own AMPM_IT_Reporting_Template.xlsx
    // (embedded into the app at build time — see the csproj). We surgically set only
    // the data cells of each sheet using the raw Open XML SDK — NOT a higher-level
    // library like ClosedXML, because those tend to fully re-parse/re-serialize
    // every part of the workbook (styles, conditional formatting, etc.) on save,
    // and this template's conditional-formatting rules (e.g. highlighting "Due
    // within 30 days") aren't round-trippable by ClosedXML's formula parser.
    // Writing cells directly leaves every other sheet, formula, style, column
    // width, dropdown and conditional format byte-for-byte untouched — the
    // Dashboard's totals/renewal counts recalculate on their own from the data.
    // This single export fills all three data sheets — Software Register, Online
    // Subscriptions, IT Spend Log — from their own tabs in the app, in one
    // combined download, sharing the same SharedStringTable across the workbook.
    [HttpGet("/Endpoints/ExportITReport")]
    public IActionResult ExportITReport()
    {
        var swReg = _db.KGetObj<List<Dictionary<string,object?>>>("software_register") ?? new();
        var onlineSubs = _db.KGetObj<List<Dictionary<string,object?>>>("online_subscriptions") ?? new();
        var spendLog = _db.KGetObj<List<Dictionary<string,object?>>>("it_spend_log") ?? new();
        string S(Dictionary<string,object?> l, string k) => l.GetValueOrDefault(k)?.ToString() ?? "";

        var asm = typeof(EndpointsController).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("AMPM_IT_Reporting_Template.xlsx", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return StatusCode(500, "Report template is missing from the app build.");

        using var ms = new MemoryStream();
        using (var templateStream = asm.GetManifestResourceStream(resourceName)!)
            templateStream.CopyTo(ms);
        ms.Position = 0;

        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wbPart = doc.WorkbookPart!;
            var sstPart = wbPart.SharedStringTablePart ?? wbPart.AddNewPart<SharedStringTablePart>();
            sstPart.SharedStringTable ??= new SharedStringTable();

            int SharedStringIndex(string text)
            {
                int i = 0;
                foreach (var item in sstPart.SharedStringTable.Elements<SharedStringItem>())
                {
                    if (item.InnerText == text) return i;
                    i++;
                }
                sstPart.SharedStringTable.AppendChild(new SharedStringItem(new Text(text)));
                sstPart.SharedStringTable.Count = (sstPart.SharedStringTable.Count?.Value ?? 0) + 1;
                sstPart.SharedStringTable.UniqueCount = (sstPart.SharedStringTable.UniqueCount?.Value ?? 0) + 1;
                return i;
            }

            static string ColumnLetter(int index)
            {
                string s = "";
                while (index > 0)
                {
                    int rem = (index - 1) % 26;
                    s = (char)('A' + rem) + s;
                    index = (index - 1) / 26;
                }
                return s;
            }

            static int ColumnNumber(string colLetters)
            {
                int n = 0;
                foreach (char c in colLetters) n = n * 26 + (c - 'A' + 1);
                return n;
            }

            Row GetOrCreateRow(SheetData sheetData, uint rowIndex)
            {
                var row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex is not null && r.RowIndex.Value == rowIndex);
                if (row != null) return row;
                row = new Row { RowIndex = rowIndex };
                var before = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex is not null && r.RowIndex.Value > rowIndex);
                if (before != null) sheetData.InsertBefore(row, before); else sheetData.AppendChild(row);
                return row;
            }

            Cell GetOrCreateCell(Row row, int colIndex, uint rowIndex)
            {
                string cellRef = ColumnLetter(colIndex) + rowIndex;
                var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference is not null && c.CellReference.Value == cellRef);
                if (cell != null) return cell;
                cell = new Cell { CellReference = cellRef };
                Cell? before = null;
                foreach (var c in row.Elements<Cell>())
                {
                    var refVal = c.CellReference?.Value ?? "";
                    var letters = new string(refVal.TakeWhile(char.IsLetter).ToArray());
                    if (letters.Length > 0 && ColumnNumber(letters) > colIndex) { before = c; break; }
                }
                if (before != null) row.InsertBefore(cell, before); else row.AppendChild(cell);
                return cell;
            }

            void SetText(SheetData sheetData, uint rowIndex, int colIndex, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var cell = GetOrCreateCell(GetOrCreateRow(sheetData, rowIndex), colIndex, rowIndex);
                cell.CellValue = new CellValue(SharedStringIndex(value).ToString());
                cell.DataType = new EnumValue<CellValues>(CellValues.SharedString);
            }

            void SetNumber(SheetData sheetData, uint rowIndex, int colIndex, double value)
            {
                var cell = GetOrCreateCell(GetOrCreateRow(sheetData, rowIndex), colIndex, rowIndex);
                cell.CellValue = new CellValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                cell.DataType = null;
            }

            void SetDate(SheetData sheetData, uint rowIndex, int colIndex, DateTime value) => SetNumber(sheetData, rowIndex, colIndex, value.ToOADate());

            SheetData GetSheetData(string sheetName)
            {
                var sheet = wbPart.Workbook.Descendants<Sheet>().First(s => s.Name == sheetName);
                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);
                return wsPart.Worksheet.GetFirstChild<SheetData>()!;
            }

            // ── Software Register (SoftwareRegisterTable, A6:AD206, data rows 7-206) ──
            {
                var sheetData = GetSheetData("Software Register");
                const uint firstRow = 7, lastRow = 206;
                uint row = firstRow;
                foreach (var l in swReg)
                {
                    if (row > lastRow) break; // template's pre-built rows are full

                    SetText(sheetData, row, 1, S(l, "recordId"));           // A Record ID
                    SetText(sheetData, row, 2, S(l, "software"));           // B Software / Application
                    SetText(sheetData, row, 3, S(l, "category"));           // C Category
                    SetText(sheetData, row, 4, S(l, "deployment"));         // D Deployment
                    SetText(sheetData, row, 5, S(l, "purpose"));            // E Purpose / Module
                    SetText(sheetData, row, 6, S(l, "vendor"));             // F Vendor
                    SetText(sheetData, row, 7, S(l, "businessOwner"));      // G Business Owner
                    SetText(sheetData, row, 8, S(l, "itOwner"));            // H IT Owner
                    SetText(sheetData, row, 9, S(l, "department"));         // I Department
                    SetText(sheetData, row, 10, S(l, "licenseType"));       // J License Type
                    if (double.TryParse(S(l, "purchasedLicenses"), out var purchased)) SetNumber(sheetData, row, 11, purchased); // K
                    if (double.TryParse(S(l, "assignedLicenses"), out var assigned)) SetNumber(sheetData, row, 12, assigned);    // L
                    // column 13 (M) = Utilization % — template formula, left untouched
                    SetText(sheetData, row, 14, S(l, "versionPlan"));       // N Version / Plan
                    if (DateTime.TryParse(S(l, "startDate"), out var startDate)) SetDate(sheetData, row, 15, startDate);         // O
                    if (DateTime.TryParse(S(l, "renewalDate"), out var renewalDate)) SetDate(sheetData, row, 16, renewalDate);   // P
                    if (double.TryParse(S(l, "annualCost"), out var annualCost)) SetNumber(sheetData, row, 17, annualCost);      // Q
                    SetText(sheetData, row, 18, S(l, "paymentFrequency"));  // R Payment Frequency
                    SetText(sheetData, row, 19, S(l, "autoRenew"));         // S Auto-Renew
                    SetText(sheetData, row, 20, S(l, "criticality"));       // T Criticality
                    SetText(sheetData, row, 21, S(l, "dataSensitivity"));   // U Data Sensitivity
                    SetText(sheetData, row, 22, S(l, "ssoMfa"));            // V SSO / MFA
                    SetText(sheetData, row, 23, S(l, "contractPo"));        // W Contract / PO No.
                    SetText(sheetData, row, 24, S(l, "invoiceNo"));         // X Invoice No.
                    SetText(sheetData, row, 25, S(l, "status"));            // Y Status
                    // columns 26 (Z) / 27 (AA) = Days to Renewal / Renewal Flag — template formulas, left untouched
                    SetText(sheetData, row, 28, S(l, "riskIssue"));         // AB Risk / Issue
                    SetText(sheetData, row, 29, S(l, "actionRequired"));    // AC Action Required
                    SetText(sheetData, row, 30, S(l, "remarks"));           // AD Remarks
                    row++;
                }
            }

            // ── Online Subscriptions (OnlineSubscriptionsTable, A6:AI206, data rows 7-206) ──
            {
                var sheetData = GetSheetData("Online Subscriptions");
                const uint firstRow = 7, lastRow = 206;
                uint row = firstRow;
                foreach (var l in onlineSubs)
                {
                    if (row > lastRow) break;

                    SetText(sheetData, row, 1, S(l, "subscriptionId"));      // A Subscription ID
                    SetText(sheetData, row, 2, S(l, "toolPlatform"));        // B Tool/Platform
                    SetText(sheetData, row, 3, S(l, "subscriptionType"));    // C Subscription Type
                    SetText(sheetData, row, 4, S(l, "useCase"));             // D Use Case
                    SetText(sheetData, row, 5, S(l, "vendor"));              // E Vendor
                    SetText(sheetData, row, 6, S(l, "website"));             // F Website
                    SetText(sheetData, row, 7, S(l, "businessOwner"));       // G Business Owner
                    SetText(sheetData, row, 8, S(l, "adminAccount"));        // H Admin Account/Email
                    SetText(sheetData, row, 9, S(l, "department"));          // I Department
                    SetText(sheetData, row, 10, S(l, "plan"));               // J Plan
                    if (double.TryParse(S(l, "seats"), out var seats)) SetNumber(sheetData, row, 11, seats);             // K
                    if (double.TryParse(S(l, "activeUsers"), out var activeUsers)) SetNumber(sheetData, row, 12, activeUsers); // L
                    // column 13 (M) = Utilization % — template formula, left untouched
                    SetText(sheetData, row, 14, S(l, "billingCurrency"));    // N Billing Currency
                    if (double.TryParse(S(l, "billingAmount"), out var billingAmount)) SetNumber(sheetData, row, 15, billingAmount); // O
                    if (double.TryParse(S(l, "fxRate"), out var fxRate)) SetNumber(sheetData, row, 16, fxRate);          // P
                    // column 17 (Q) = Billing Amount INR — template formula, left untouched
                    SetText(sheetData, row, 18, S(l, "billingFrequency"));   // R Billing Frequency
                    // column 19 (S) = Annualized Cost INR — template formula, left untouched
                    if (DateTime.TryParse(S(l, "startDate"), out var startDate)) SetDate(sheetData, row, 20, startDate);       // T
                    if (DateTime.TryParse(S(l, "renewalDate"), out var renewalDate)) SetDate(sheetData, row, 21, renewalDate); // U
                    SetText(sheetData, row, 22, S(l, "autoRenew"));          // V Auto-Renew
                    SetText(sheetData, row, 23, S(l, "paymentMode"));        // W Payment Mode
                    SetText(sheetData, row, 24, S(l, "cardLast4"));          // X Card/Bank Last 4
                    SetText(sheetData, row, 25, S(l, "gstTaxCredit"));       // Y GST/Tax Credit
                    SetText(sheetData, row, 26, S(l, "dataShared"));         // Z Data Shared
                    SetText(sheetData, row, 27, S(l, "aiDataRetention"));    // AA AI Data Retention/Training
                    SetText(sheetData, row, 28, S(l, "securityReview"));     // AB Security Review
                    SetText(sheetData, row, 29, S(l, "criticality"));        // AC Criticality
                    SetText(sheetData, row, 30, S(l, "status"));             // AD Status
                    // columns 31 (AE) / 32 (AF) = Days to Renewal / Renewal Flag — template formulas, left untouched
                    SetText(sheetData, row, 33, S(l, "duplicateOverlap"));   // AG Duplicate/Overlap
                    SetText(sheetData, row, 34, S(l, "costSavingAction"));   // AH Cost Saving Action
                    SetText(sheetData, row, 35, S(l, "remarks"));            // AI Remarks
                    row++;
                }
            }

            // ── IT Spend Log (ITSpendLogTable, A6:W306, data rows 7-306) ──
            {
                var sheetData = GetSheetData("IT Spend Log");
                const uint firstRow = 7, lastRow = 306;
                uint row = firstRow;
                foreach (var l in spendLog)
                {
                    if (row > lastRow) break;

                    if (DateTime.TryParse(S(l, "expenseDate"), out var expenseDate)) SetDate(sheetData, row, 1, expenseDate); // A
                    // column 2 (B) = Reporting Month — template formula, left untouched
                    SetText(sheetData, row, 3, S(l, "expenseId"));           // C Expense ID
                    SetText(sheetData, row, 4, S(l, "vendor"));              // D Vendor
                    SetText(sheetData, row, 5, S(l, "toolSoftware"));        // E Tool/Software
                    SetText(sheetData, row, 6, S(l, "subscriptionType"));    // F Subscription Type
                    SetText(sheetData, row, 7, S(l, "costCategory"));        // G Cost Category
                    SetText(sheetData, row, 8, S(l, "department"));          // H Department
                    SetText(sheetData, row, 9, S(l, "invoiceNo"));           // I Invoice No.
                    SetText(sheetData, row, 10, S(l, "poContractNo"));       // J PO/Contract No.
                    if (double.TryParse(S(l, "baseAmount"), out var baseAmount)) SetNumber(sheetData, row, 11, baseAmount); // K
                    if (double.TryParse(S(l, "gst"), out var gst)) SetNumber(sheetData, row, 12, gst);                      // L
                    // column 13 (M) = Gross Amount — template formula, left untouched
                    if (double.TryParse(S(l, "tds"), out var tds)) SetNumber(sheetData, row, 14, tds);                      // N
                    // column 15 (O) = Net Paid/Payable — template formula, left untouched
                    if (DateTime.TryParse(S(l, "paymentDate"), out var paymentDate)) SetDate(sheetData, row, 16, paymentDate); // P
                    SetText(sheetData, row, 17, S(l, "paymentStatus"));      // Q Payment Status
                    SetText(sheetData, row, 18, S(l, "paymentMode"));        // R Payment Mode
                    if (double.TryParse(S(l, "budgetAmount"), out var budgetAmount)) SetNumber(sheetData, row, 19, budgetAmount); // S
                    // column 20 (T) = Variance to Budget — template formula, left untouched
                    SetText(sheetData, row, 21, S(l, "capexOpex"));          // U Capex/Opex
                    SetText(sheetData, row, 22, S(l, "costCenter"));         // V Cost Center
                    SetText(sheetData, row, 23, S(l, "remarks"));            // W Remarks
                    row++;
                }
            }

            doc.Save();
        }

        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AMPM_IT_Report_{IstTime.Now:yyyyMMdd}.xlsx");
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
        fixedCount += RepairCorruptedStrings("online_subscriptions");
        fixedCount += RepairCorruptedStrings("it_spend_log");
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
