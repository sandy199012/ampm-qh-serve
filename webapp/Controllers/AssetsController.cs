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

    // ONE Excel file with three sheets: IT Assets (PCs, laptops, monitors, ...),
    // Printers, and Network Devices (switch / WiFi / NVR / camera / other).
    // Written as a genuine SpreadsheetML workbook (same approach as the Todo
    // report) because that has reliable multi-sheet support in Excel.
    [HttpGet("/Assets/Export")]
    public IActionResult Export()
    {
        var all = _db.GetAssets();
        static string V(Dictionary<string,object?> a, string k) => a.GetValueOrDefault(k)?.ToString()?.Trim() ?? "";
        static string TypeOf(Dictionary<string,object?> a) => V(a, "assetType");

        var pcs      = all.Where(a => TypeOf(a) != "Printer" && !AssetTags.IsNetworkType(TypeOf(a))).OrderBy(a => V(a, "assetTag"), StringComparer.OrdinalIgnoreCase).ToList();
        var printers = all.Where(a => TypeOf(a) == "Printer").OrderBy(a => V(a, "assetTag"), StringComparer.OrdinalIgnoreCase).ToList();
        var netOrder = AssetTags.NetworkTypes.ToList();
        var nets     = all.Where(a => AssetTags.IsNetworkType(TypeOf(a)))
                          .OrderBy(a => { var i = netOrder.IndexOf(TypeOf(a)); return i < 0 ? 99 : i; })
                          .ThenBy(a => V(a, "assetTag"), StringComparer.OrdinalIgnoreCase).ToList();

        string Assigned(Dictionary<string,object?> a) => V(a, "assignedToName") is { Length: > 0 } n ? n : "Unassigned";

        // ---- sheet 1: IT assets ----
        var pcHeads = new[] { "#", "Asset Tag", "Type", "Hostname", "Brand", "Model", "Serial Number", "MAC Address", "IP Address", "OS", "OS Build", "Architecture", "CPU", "RAM (GB)", "Disk Free", "Storage (Total)", "Condition", "Assigned To", "Emp Code", "Department", "Assigned Date", "Location", "Last Seen", "Source" };
        var pcW = new[] { 28, 70, 70, 95, 70, 110, 100, 100, 85, 120, 90, 70, 150, 55, 60, 70, 65, 120, 65, 110, 80, 95, 100, 80 };
        var pcRows = new List<string[]>();
        foreach (var a in pcs)
            pcRows.Add(new[] { (pcRows.Count + 1).ToString(), V(a,"assetTag"), TypeOf(a), V(a,"hostname"), V(a,"brand"), V(a,"model"), V(a,"serial"), V(a,"mac"), V(a,"ip"), V(a,"os"), V(a,"osBuild"), V(a,"arch"), V(a,"processor"), V(a,"ram"), V(a,"diskFree"), V(a,"storage"), V(a,"condition"), Assigned(a), V(a,"assignedToEmp"), V(a,"assignedToDept"), V(a,"assignedDate"), V(a,"location"), V(a,"lastSeen"), V(a,"source") });

        // ---- sheet 2: printers ----
        var prHeads = new[] { "#", "Asset Tag", "Brand", "Model", "Serial Number", "Printer Type", "Connection", "IP Address", "MAC Address", "Hostname", "Connected PC (USB)", "Printer Name (USB)", "USB Port", "USB Status", "Toner / Cartridge", "Page Count", "Condition", "Assigned To", "Emp Code", "Department", "Location", "Last Seen", "Source" };
        var prW = new[] { 28, 70, 70, 120, 100, 80, 85, 85, 100, 95, 100, 130, 60, 70, 100, 65, 65, 120, 65, 110, 95, 100, 80 };
        var prRows = new List<string[]>();
        foreach (var a in printers)
            prRows.Add(new[] { (prRows.Count + 1).ToString(), V(a,"assetTag"), V(a,"brand"), V(a,"model"), V(a,"serial"), V(a,"printerType"), V(a,"connection"), V(a,"ip"), V(a,"mac"), V(a,"hostname"), V(a,"connectedPc"), V(a,"printerName"), V(a,"usbPort"), V(a,"usbStatus"), V(a,"tonerModel"), V(a,"pageCount"), V(a,"condition"), Assigned(a), V(a,"assignedToEmp"), V(a,"assignedToDept"), V(a,"location"), V(a,"lastSeen"), V(a,"source") });

        // ---- sheet 3: network devices ----
        var nwHeads = new[] { "#", "Asset Tag", "Type", "Brand", "Model", "Serial Number", "IP Address", "MAC Address", "Hostname", "Ports / Channels", "Open Ports", "Description", "Location", "Condition", "First Seen", "Last Seen", "Source" };
        var nwW = new[] { 28, 70, 100, 85, 120, 100, 90, 105, 110, 70, 100, 200, 100, 65, 100, 100, 90 };
        var nwRows = new List<string[]>();
        foreach (var a in nets)
            nwRows.Add(new[] { (nwRows.Count + 1).ToString(), V(a,"assetTag"), TypeOf(a), V(a,"brand"), V(a,"model"), V(a,"serial"), V(a,"ip"), V(a,"mac"), V(a,"hostname"), V(a,"portCount"), V(a,"openPorts"), V(a,"descr"), V(a,"location"), V(a,"condition"), V(a,"firstSeen"), V(a,"lastSeen"), V(a,"source") });

        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version='1.0'?>\n<?mso-application progid='Excel.Sheet'?>\n");
        sb.Append(@"<Workbook xmlns='urn:schemas-microsoft-com:office:spreadsheet'
 xmlns:o='urn:schemas-microsoft-com:office:office'
 xmlns:x='urn:schemas-microsoft-com:office:excel'
 xmlns:ss='urn:schemas-microsoft-com:office:spreadsheet'>
<Styles>");
        sb.Append(AssetXlStyles);
        sb.Append("</Styles>\n");
        sb.Append(AssetSheet("IT Assets", "AMPM FASHIONS PVT. LTD. \u2014 IT ASSETS (PCs, laptops, monitors & other)", pcHeads, pcW, pcRows, 17));
        sb.Append(AssetSheet("Printers", "AMPM FASHIONS PVT. LTD. \u2014 PRINTERS", prHeads, prW, prRows, 17));
        sb.Append(AssetSheet("Network Devices", "AMPM FASHIONS PVT. LTD. \u2014 NETWORK DEVICES (switch / WiFi / NVR / camera / other)", nwHeads, nwW, nwRows, -1));
        sb.Append("</Workbook>");

        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Assets_{IstTime.Now:yyyyMMdd}.xls");
    }

    // ---- SpreadsheetML helpers for Export ----
    static string AssetXEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;   // not allowed in XML 1.0
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    static string AssetCell(string? text, string style, int mergeAcross = 0)
    {
        var m = mergeAcross > 0 ? $" ss:MergeAcross='{mergeAcross}'" : "";
        return $"<Cell ss:StyleID='{style}'{m}><Data ss:Type='String'>{AssetXEsc(text)}</Data></Cell>";
    }

    // assignCol = index of the "Assigned To" column (green when assigned, amber when
    // "Unassigned"), or -1 for none.
    static string AssetSheet(string name, string title, string[] heads, int[] widths, List<string[]> rows, int assignCol)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<Worksheet ss:Name='{AssetXEsc(name)}'><Table ss:DefaultColumnWidth='70'>");
        foreach (var w in widths) sb.Append($"<Column ss:Width='{w}'/>");
        sb.Append($"<Row ss:Height='26'>{AssetCell(title, "aTitle", heads.Length - 1)}</Row>");
        sb.Append($"<Row>{AssetCell($"Generated: {IstTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha | Total: {rows.Count}", "aInfo", heads.Length - 1)}</Row>");
        sb.Append("<Row ss:Height='22'>");
        foreach (var h in heads) sb.Append(AssetCell(h, "aHead"));
        sb.Append("</Row>");
        foreach (var r in rows)
        {
            sb.Append("<Row>");
            for (int c = 0; c < r.Length; c++)
            {
                string st = c == 0 ? "aCenter" : c == 1 ? "aBold" : "aCell";
                if (c == assignCol) st = r[c] == "Unassigned" ? "aAmber" : "aGreen";
                sb.Append(AssetCell(r[c], st));
            }
            sb.Append("</Row>");
        }
        if (rows.Count == 0)
            sb.Append($"<Row>{AssetCell("Nothing to list yet.", "aCell", Math.Min(3, heads.Length - 1))}</Row>");
        sb.Append("</Table></Worksheet>\n");
        return sb.ToString();
    }

    const string AssetXlBorder = "<Borders><Border ss:Position='Bottom' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Left' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Right' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Top' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/></Borders>";
    static readonly string AssetXlStyles = $@"
<Style ss:ID='Default' ss:Name='Normal'><Font ss:FontName='Arial' ss:Size='10'/></Style>
<Style ss:ID='aTitle' ss:Parent='Default'><Interior ss:Color='#0A192F' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='14'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='aInfo' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='aHead' ss:Parent='Default'><Interior ss:Color='#0A192F' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center' ss:WrapText='1'/>{AssetXlBorder}</Style>
<Style ss:ID='aCell' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Vertical='Center'/>{AssetXlBorder}</Style>
<Style ss:ID='aCenter' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{AssetXlBorder}</Style>
<Style ss:ID='aBold' ss:Parent='Default'><Font ss:Size='9' ss:Bold='1'/><Alignment ss:Vertical='Center'/>{AssetXlBorder}</Style>
<Style ss:ID='aGreen' ss:Parent='Default'><Font ss:Size='9' ss:Bold='1' ss:Color='#059669'/><Alignment ss:Vertical='Center'/>{AssetXlBorder}</Style>
<Style ss:ID='aAmber' ss:Parent='Default'><Font ss:Size='9' ss:Bold='1' ss:Color='#D97706'/><Alignment ss:Vertical='Center'/>{AssetXlBorder}</Style>
";

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
    public static readonly string[] NetworkTypes = { "Network Switch", "WiFi Device", "NVR", "Camera", "Other Network Device" };
    public static bool IsNetworkType(string? t) => t != null && NetworkTypes.Contains(t);

    // Tag series per asset type (null = the normal free-text tags, e.g. AMPM-0003).
    public static string? PrefixFor(string? type) => type switch
    {
        "Printer" => PrinterPrefix,
        "Network Switch" => "SW-",
        "WiFi Device" => "WIFI-",
        "NVR" => "NVR-",
        "Camera" => "CAM-",
        "Other Network Device" => "NET-",
        _ => null,
    };

    // type -> next free tag, for the Add Asset form.
    public static Dictionary<string,string> NextTags(List<Dictionary<string,object?>> assets)
    {
        var d = new Dictionary<string,string>();
        foreach (var t in new[] { "Printer", "Network Switch", "WiFi Device", "NVR", "Camera", "Other Network Device" })
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
