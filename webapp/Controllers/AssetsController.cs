using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class AssetsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public AssetsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    void SaveAssets(List<Dictionary<string,object?>> assets)
        => _db.Execute("INSERT INTO kv (k,v) VALUES ('asset_stock',@v) ON CONFLICT (k) DO UPDATE SET v=@v",
            new { v = JsonConvert.SerializeObject(assets) });

    public IActionResult Index(string? search, string? type)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Employees = _db.GetEmployees()
            .Where(e => string.IsNullOrWhiteSpace(e.GetValueOrDefault("exitDate")?.ToString()))
            .OrderBy(e => e.GetValueOrDefault("name")?.ToString())
            .ToList();
        // Network devices (switch / WiFi / NVR / camera) live in their own tab,
        // so the main list and its stats exclude them.
        var assets = _db.GetAssets().Where(a => !AssetTags.IsNetworkType(a.GetValueOrDefault("assetType")?.ToString())).ToList();
        ViewBag.NetDevices = _db.GetAssets()
            .Where(a => AssetTags.IsNetworkType(a.GetValueOrDefault("assetType")?.ToString()))
            .OrderBy(a => a.GetValueOrDefault("assetType")?.ToString())
            .ThenBy(a => a.GetValueOrDefault("assetTag")?.ToString())
            .ToList();
        ViewBag.NetScanInfo = _db.KGetObj<Dictionary<string,object?>>("network_scan_last");
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            assets = assets.Where(a =>
                a.GetValueOrDefault("assetTag")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("brand")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("model")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("assignedToName")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("serial")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("ip")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("hostname")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        if (!string.IsNullOrEmpty(type))
            assets = assets.Where(a => a.GetValueOrDefault("assetType")?.ToString() == type).ToList();
        ViewBag.Search = search;
        ViewBag.TypeFilter = type;
        // Last network-printer scan summary (written by EndpointsController.ReportPrinters)
        ViewBag.PrinterScanInfo = _db.KGetObj<Dictionary<string,object?>>("printer_scan_last");
        ViewBag.Types = _db.GetAssets().Select(a => a.GetValueOrDefault("assetType")?.ToString() ?? "").Where(t => !string.IsNullOrEmpty(t) && !AssetTags.IsNetworkType(t)).Distinct().OrderBy(t => t).ToList();
        return View(assets);
    }

    [HttpGet]
    public IActionResult Create(string? type)
    {
        ViewBag.PresetType = type;
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Employees = _db.GetEmployees()
            .Where(e => string.IsNullOrWhiteSpace(e.GetValueOrDefault("exitDate")?.ToString()))
            .OrderBy(e => e.GetValueOrDefault("name")?.ToString())
            .ToList();
        // Printers (PRN-), switches (SW-), WiFi devices (WIFI-), NVRs (NVR-) and
        // cameras (CAM-) each have their own tag series — the form pre-fills the
        // next free number when one of those types is chosen.
        ViewBag.NextTags = AssetTags.NextTags(_db.GetAssets());
        return View(new Dictionary<string,object?>());
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var asset = new Dictionary<string,object?> { ["id"] = Guid.NewGuid().ToString("N")[..8] };
        foreach (var key in form.Keys) asset[key] = form[key].ToString();
        var assets = _db.GetAssets();
        var newType = asset.GetValueOrDefault("assetType")?.ToString();
        var prefix = AssetTags.PrefixFor(newType);
        if (prefix != null)
        {
            // Blank tag, or a tag that already exists, -> next free number in that type's series.
            var tag = asset.GetValueOrDefault("assetTag")?.ToString()?.Trim() ?? "";
            bool taken = assets.Any(a => string.Equals(a.GetValueOrDefault("assetTag")?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(tag) || taken)
                asset["assetTag"] = AssetTags.NextTag(assets, prefix);
            if (string.IsNullOrWhiteSpace(asset.GetValueOrDefault("source")?.ToString()))
                asset["source"] = "Manual";
        }
        if (newType == "Printer" && !string.IsNullOrWhiteSpace(asset.GetValueOrDefault("assignedToName")?.ToString()))
            asset["assignedDate"] = IstTime.Today.ToString("yyyy-MM-dd");
        assets.Add(asset);
        SaveAssets(assets);
        SyncAssetToEmployee(asset);
        TempData["Success"] = $"Asset {asset.GetValueOrDefault("assetTag")} added!";
        return AssetTags.IsNetworkType(newType) ? Redirect("/Assets/Index#net") : RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Employees = _db.GetEmployees()
            .Where(e => string.IsNullOrWhiteSpace(e.GetValueOrDefault("exitDate")?.ToString()))
            .OrderBy(e => e.GetValueOrDefault("name")?.ToString())
            .ToList();
        var asset = _db.GetAssets().FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        return View(asset);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var assets = _db.GetAssets();
        var asset = assets.FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        foreach (var key in form.Keys) asset[key] = form[key].ToString();
        SaveAssets(assets);
        SyncAssetToEmployee(asset);
        TempData["Success"] = "Asset updated!";
        return AssetTags.IsNetworkType(asset.GetValueOrDefault("assetType")?.ToString()) ? Redirect("/Assets/Index#net") : RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Assign(string id, IFormCollection form)
    {
        var assets = _db.GetAssets();
        var asset = assets.FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        asset["assignedToName"] = form["assignedToName"].ToString();
        asset["assignedToEmp"]  = form["assignedToEmp"].ToString();
        asset["assignedToDept"] = form["assignedToDept"].ToString();
        asset["assignedDate"]   = IstTime.Today.ToString("yyyy-MM-dd");
        asset["returnDate"]     = "";
        SaveAssets(assets);
        SyncAssetToEmployee(asset);
        TempData["Success"] = $"Asset assigned to {form["assignedToName"]}.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Unassign(string id)
    {
        var assets = _db.GetAssets();
        var asset = assets.FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        var prevEmpCode = asset.GetValueOrDefault("assignedToEmp")?.ToString();
        asset["assignedToName"] = "";
        asset["assignedToEmp"]  = "";
        asset["assignedToDept"] = "";
        asset["returnDate"]     = IstTime.Today.ToString("yyyy-MM-dd");
        SaveAssets(assets);
        ClearEmployeeAsset(prevEmpCode, asset);
        TempData["Success"] = "Asset unassigned and returned to stock.";
        return RedirectToAction("Index");
    }

    // Mirrors an asset's specs onto the Employee record it's assigned to (the
    // legacy single-PC fields shown on Employees Index/Details, and used as a
    // fallback on the Handover form when no Assets record is linked). Kept in
    // sync whenever an asset is created, edited, or assigned. Only hardware
    // fields sourced from the asset are touched — never the employee's own
    // details (name, department, etc.).
    private void SyncAssetToEmployee(Dictionary<string,object?> asset)
    {
        if (AssetTags.IsPeripheral(asset)) return;   // printer/monitor/UPS never overwrite the employee's PC fields
        var empCode = asset.GetValueOrDefault("assignedToEmp")?.ToString();
        if (string.IsNullOrWhiteSpace(empCode)) return;
        var emp = _db.GetEmployeeByCode(empCode);
        if (emp == null) return;
        void SetEmpIf(string srcKey, string destKey)
        {
            var v = asset.GetValueOrDefault(srcKey)?.ToString();
            if (!string.IsNullOrWhiteSpace(v)) emp[destKey] = v;
        }
        var hn = asset.GetValueOrDefault("hostname")?.ToString();
        if (string.IsNullOrWhiteSpace(hn)) hn = asset.GetValueOrDefault("assetTag")?.ToString() ?? "";
        emp["hostname"] = hn;
        SetEmpIf("ip", "ip");
        SetEmpIf("mac", "mac");
        SetEmpIf("os", "os");
        SetEmpIf("osBuild", "osBuild");
        SetEmpIf("brand", "manufacturer");
        SetEmpIf("model", "model");
        SetEmpIf("serial", "serial");
        SetEmpIf("processor", "processor");
        SetEmpIf("ram", "ram");
        _db.SaveEmployee(empCode, emp);
    }

    // Undoes SyncAssetToEmployee when an asset is unassigned — but only if the
    // employee's own record still shows THIS asset (matched by hostname or
    // serial), so unassigning one asset never wipes out a different one.
    private void ClearEmployeeAsset(string? empCode, Dictionary<string,object?> asset)
    {
        if (AssetTags.IsPeripheral(asset)) return;
        if (string.IsNullOrWhiteSpace(empCode)) return;
        var emp = _db.GetEmployeeByCode(empCode);
        if (emp == null) return;
        var assetSerial = asset.GetValueOrDefault("serial")?.ToString() ?? "";
        var assetHost = asset.GetValueOrDefault("hostname")?.ToString();
        if (string.IsNullOrWhiteSpace(assetHost)) assetHost = asset.GetValueOrDefault("assetTag")?.ToString() ?? "";
        bool matches = (!string.IsNullOrWhiteSpace(assetHost) && emp.GetValueOrDefault("hostname")?.ToString() == assetHost)
                    || (!string.IsNullOrWhiteSpace(assetSerial) && emp.GetValueOrDefault("serial")?.ToString() == assetSerial);
        if (!matches) return;
        emp["hostname"] = ""; emp["ip"] = ""; emp["mac"] = ""; emp["os"] = ""; emp["osBuild"] = "";
        emp["manufacturer"] = ""; emp["model"] = ""; emp["serial"] = ""; emp["processor"] = ""; emp["ram"] = "";
        _db.SaveEmployee(empCode, emp);
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var assets = _db.GetAssets();
        var gone = assets.FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        bool wasNet = AssetTags.IsNetworkType(gone?.GetValueOrDefault("assetType")?.ToString());
        assets.RemoveAll(a => a.GetValueOrDefault("id")?.ToString() == id);
        SaveAssets(assets);
        TempData["Success"] = "Asset deleted.";
        return wasNet ? Redirect("/Assets/Index#net") : RedirectToAction("Index");
    }

    [HttpGet("/Assets/Handover/{id}")]
    public IActionResult Handover(string id)
    {
        var asset = _db.GetAssets().FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        string S(string k) => asset.GetValueOrDefault(k)?.ToString() ?? "";

        // Spec rows differ by asset type: a printer has no CPU/RAM/OS, it has a
        // printer type, connection and (for network printers) an IP address.
        string specRows;
        if (S("assetType") == "Printer")
        {
            specRows = $"      <tr><td class='k'>Printer Type</td><td class='v'>{S("printerType")}</td></tr>\n"
                     + $"      <tr><td class='k'>Connection</td><td class='v'>{S("connection")}</td></tr>\n"
                     + $"      <tr><td class='k'>IP Address</td><td class='v'>{S("ip")}</td></tr>\n"
                     + $"      <tr><td class='k'>Toner / Cartridge</td><td class='v'>{S("tonerModel")}</td></tr>";
        }
        else
        {
            specRows = $"      <tr><td class='k'>Processor</td><td class='v'>{S("processor")}</td></tr>\n"
                     + $"      <tr><td class='k'>RAM / Storage</td><td class='v'>{S("ram")} GB / {S("storage")} GB</td></tr>\n"
                     + $"      <tr><td class='k'>OS</td><td class='v'>{S("os")}</td></tr>";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Asset Handover Form</title>
<style>
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#1E293B;margin:0;background:#F1F5F9;font-size:12.5px}
.wrap{max-width:760px;margin:26px auto;background:#fff;border:1px solid #CBD5E1;border-radius:6px;overflow:hidden}
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
    <div class='meta'>Handover Date: <b>").Append(IstTime.Now.ToString("dd-MMM-yyyy")).Append(@"</b></div>
  </div>
  <div class='pad'>

    <div class='sec-hdr'>ASSET DETAILS</div>
    <table class='kv'>
      <tr><td class='k'>Asset Tag</td><td class='v'>").Append(S("assetTag")).Append(@"</td></tr>
      <tr><td class='k'>Type</td><td class='v'>").Append(S("assetType")).Append(@"</td></tr>
      <tr><td class='k'>Brand / Model</td><td class='v'>").Append(S("brand")).Append(' ').Append(S("model")).Append(@"</td></tr>
      <tr><td class='k'>Serial No.</td><td class='v'>").Append(S("serial")).Append(@"</td></tr>
").Append(specRows).Append(@"
      <tr><td class='k'>Condition</td><td class='v'>").Append(S("condition")).Append(@"</td></tr>
    </table>

    <div class='sec-hdr'>ASSIGNED TO</div>
    <table class='kv'>
      <tr><td class='k'>Employee Name</td><td class='v'>").Append(S("assignedToName").ToUpper()).Append(@"</td></tr>
      <tr><td class='k'>Emp Code</td><td class='v'>").Append(S("assignedToEmp")).Append(@"</td></tr>
      <tr><td class='k'>Department</td><td class='v'>").Append(S("assignedToDept")).Append(@"</td></tr>
      <tr><td class='k'>Assigned Date</td><td class='v'>").Append(S("assignedDate")).Append(@"</td></tr>
      <tr><td class='k'>Location</td><td class='v'>").Append(S("location")).Append(@"</td></tr>
    </table>

    <div class='ack'><b>Acknowledgement:</b> I acknowledge receipt of the above IT asset in good working condition and agree to return the same upon separation from the company, transfer, or upon request by the IT Department.</div>

    <div class='sig-row'>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Employee Signature</div><div class='sig-pre'>").Append(S("assignedToName").ToUpper()).Append(@"</div></div>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>IT Department</div><div class='sig-pre'>Sandeep Kumar Singh Kushwaha</div></div>
    </div>

    <div class='footer'>Printed: ").Append(IstTime.Now.ToString("dd MMM yyyy HH:mm")).Append(@" &nbsp;|&nbsp; AMPM Fashions Pvt. Ltd, B-144, Sector 10, Noida - 201301 &nbsp;|&nbsp; IT Department</div>

  </div>
</div>

</body></html>");
        return Content(sb.ToString(), "text/html");
    }

    [HttpGet("/Assets/Export")]
    public IActionResult Export()
    {
        var assets = _db.GetAssets();
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:11px}}table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:white;padding:6px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px;border:1px solid #CBD5E1;font-size:10px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
.green{{color:#059669;font-weight:bold}}.amber{{color:#D97706;font-weight:bold}}
</style></head><body>
<table style='margin-bottom:12px'><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — ASSET STOCK REPORT</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {IstTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<table><thead><tr><th>#</th><th>Asset Tag</th><th>Type</th><th>Hostname</th><th>Brand</th><th>Model</th><th>Serial Number</th><th>MAC Address</th><th>IP Address</th><th>OS</th><th>OS Build</th><th>Architecture</th><th>CPU</th><th>RAM (GB)</th><th>Disk Free</th><th>Storage (Total)</th><th>Condition</th><th>Assigned To</th><th>Emp Code</th><th>Department</th><th>Assigned Date</th><th>Location</th><th>Last Seen</th><th>Printer Type</th><th>Connection</th><th>Toner / Cartridge</th><th>Page Count</th><th>Source</th></tr></thead><tbody>");
        int sno = 0;
        foreach (var a in assets)
        {
            sno++;
            bool assigned = !string.IsNullOrEmpty(a.GetValueOrDefault("assignedToName")?.ToString());
            string cls = assigned ? "green" : "amber";
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td><b>{a.GetValueOrDefault("assetTag")}</b></td><td>{a.GetValueOrDefault("assetType")}</td><td>{a.GetValueOrDefault("hostname")}</td><td>{a.GetValueOrDefault("brand")}</td><td>{a.GetValueOrDefault("model")}</td><td>{a.GetValueOrDefault("serial")}</td><td>{a.GetValueOrDefault("mac")}</td><td>{a.GetValueOrDefault("ip")}</td><td>{a.GetValueOrDefault("os")}</td><td>{a.GetValueOrDefault("osBuild")}</td><td>{a.GetValueOrDefault("arch")}</td><td>{a.GetValueOrDefault("processor")}</td><td>{a.GetValueOrDefault("ram")}</td><td>{a.GetValueOrDefault("diskFree")}</td><td>{a.GetValueOrDefault("storage")}</td><td>{a.GetValueOrDefault("condition")}</td><td class='{cls}'>{(assigned ? a.GetValueOrDefault("assignedToName") : "Unassigned")}</td><td>{a.GetValueOrDefault("assignedToEmp")}</td><td>{a.GetValueOrDefault("assignedToDept")}</td><td>{a.GetValueOrDefault("assignedDate")}</td><td>{a.GetValueOrDefault("location")}</td><td>{a.GetValueOrDefault("lastSeen")}</td><td>{a.GetValueOrDefault("printerType")}</td><td>{a.GetValueOrDefault("connection")}</td><td>{a.GetValueOrDefault("tonerModel")}</td><td>{a.GetValueOrDefault("pageCount")}</td><td>{a.GetValueOrDefault("source")}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Assets_{IstTime.Now:yyyyMMdd}.xls");
    }

    // Zips the network-printer scanner scripts (kept under wwwroot/tools) and
    // serves them as one download. Done in code rather than as plain static
    // files because ASP.NET's static-file middleware refuses unknown extensions
    // like .ps1/.bat, and a single ZIP is easier for the user anyway.
    [HttpGet("/Assets/ScannerDownload")]
    public IActionResult ScannerDownload([FromServices] IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "tools");
        if (!Directory.Exists(dir)) return NotFound("Scanner files are not deployed on the server yet.");
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var f in Directory.GetFiles(dir, "AMPM_Printer_Scanner*"))
            {
                var entry = zip.CreateEntry(Path.GetFileName(f));
                using var es = entry.Open();
                if (f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    // The agent key is never stored in the repo: it is put into the script
                    // here, at download time, for a logged-in user with Assets access only.
                    var text = System.IO.File.ReadAllText(f).Replace("__AMPM_AGENT_KEY__", EndpointsController.CurrentAgentKey);
                    var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                    es.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    using var fs = System.IO.File.OpenRead(f);
                    fs.CopyTo(es);
                }
            }
        }
        Response.Headers["Cache-Control"] = "no-store";
        return File(ms.ToArray(), "application/zip", "AMPM_Printer_Scanner.zip");
    }
}

// Asset-tag helpers shared by AssetsController (manual add) and
// EndpointsController (auto-discovered printers).
public static class AssetTags
{
    public const string PrinterPrefix = "PRN-";

    // Next free tag for a prefix series: highest existing number + 1,
    // zero-padded to 4 digits (PRN-0001, PRN-0002, ...).
    public static string NextTag(List<Dictionary<string,object?>> assets, string prefix)
    {
        int max = 0;
        foreach (var a in assets)
        {
            var t = a.GetValueOrDefault("assetTag")?.ToString()?.Trim() ?? "";
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t.Substring(prefix.Length), out var n) && n > max)
                max = n;
        }
        return prefix + (max + 1).ToString("D4");
    }

    // Types shown in the separate "Network Devices" tab of Asset Stock.
    public static readonly string[] NetworkTypes = { "Network Switch", "WiFi Device", "NVR", "Camera" };
    public static bool IsNetworkType(string? t) => t != null && NetworkTypes.Contains(t);

    // Tag series per asset type (null = the normal free-text tags, e.g. AMPM-0003).
    public static string? PrefixFor(string? type) => type switch
    {
        "Printer" => PrinterPrefix,
        "Network Switch" => "SW-",
        "WiFi Device" => "WIFI-",
        "NVR" => "NVR-",
        "Camera" => "CAM-",
        _ => null,
    };

    // type -> next free tag, for the Add Asset form.
    public static Dictionary<string,string> NextTags(List<Dictionary<string,object?>> assets)
    {
        var d = new Dictionary<string,string>();
        foreach (var t in new[] { "Printer", "Network Switch", "WiFi Device", "NVR", "Camera" })
            d[t] = NextTag(assets, PrefixFor(t)!);
        return d;
    }

    // Things that are not "a person's PC": they must never overwrite the
    // employee record's hostname/IP/OS/CPU fields when assigned.
    public static bool IsPeripheral(Dictionary<string,object?> asset)
    {
        var t = asset.GetValueOrDefault("assetType")?.ToString() ?? "";
        return t == "Printer" || t == "Monitor" || t == "UPS" || IsNetworkType(t);
    }
}
