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
        var assets = _db.GetAssets();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            assets = assets.Where(a =>
                a.GetValueOrDefault("assetTag")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("brand")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("model")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("assignedToName")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("serial")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        if (!string.IsNullOrEmpty(type))
            assets = assets.Where(a => a.GetValueOrDefault("assetType")?.ToString() == type).ToList();
        ViewBag.Search = search;
        ViewBag.TypeFilter = type;
        ViewBag.Types = _db.GetAssets().Select(a => a.GetValueOrDefault("assetType")?.ToString() ?? "").Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        return View(assets);
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
        var asset = new Dictionary<string,object?> { ["id"] = Guid.NewGuid().ToString("N")[..8] };
        foreach (var key in form.Keys) asset[key] = form[key].ToString();
        var assets = _db.GetAssets();
        assets.Add(asset);
        SaveAssets(assets);
        TempData["Success"] = $"Asset {form["assetTag"]} added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
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
        TempData["Success"] = "Asset updated!";
        return RedirectToAction("Index");
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
        asset["assignedDate"]   = DateTime.Today.ToString("yyyy-MM-dd");
        asset["returnDate"]     = "";
        SaveAssets(assets);
        TempData["Success"] = $"Asset assigned to {form["assignedToName"]}.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Unassign(string id)
    {
        var assets = _db.GetAssets();
        var asset = assets.FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        asset["assignedToName"] = "";
        asset["assignedToEmp"]  = "";
        asset["assignedToDept"] = "";
        asset["returnDate"]     = DateTime.Today.ToString("yyyy-MM-dd");
        SaveAssets(assets);
        TempData["Success"] = "Asset unassigned and returned to stock.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var assets = _db.GetAssets();
        assets.RemoveAll(a => a.GetValueOrDefault("id")?.ToString() == id);
        SaveAssets(assets);
        TempData["Success"] = "Asset deleted.";
        return RedirectToAction("Index");
    }

    [HttpGet("/Assets/Handover/{id}")]
    public IActionResult Handover(string id)
    {
        var asset = _db.GetAssets().FirstOrDefault(a => a.GetValueOrDefault("id")?.ToString() == id);
        if (asset == null) return NotFound();
        string S(string k) => asset.GetValueOrDefault(k)?.ToString() ?? "";

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
    <div class='meta'>Handover Date: <b>").Append(DateTime.Now.ToString("dd-MMM-yyyy")).Append(@"</b></div>
  </div>
  <div class='pad'>

    <div class='sec-hdr'>ASSET DETAILS</div>
    <table class='kv'>
      <tr><td class='k'>Asset Tag</td><td class='v'>").Append(S("assetTag")).Append(@"</td></tr>
      <tr><td class='k'>Type</td><td class='v'>").Append(S("assetType")).Append(@"</td></tr>
      <tr><td class='k'>Brand / Model</td><td class='v'>").Append(S("brand")).Append(' ').Append(S("model")).Append(@"</td></tr>
      <tr><td class='k'>Serial No.</td><td class='v'>").Append(S("serial")).Append(@"</td></tr>
      <tr><td class='k'>Processor</td><td class='v'>").Append(S("processor")).Append(@"</td></tr>
      <tr><td class='k'>RAM / Storage</td><td class='v'>").Append(S("ram")).Append(@" GB / ").Append(S("storage")).Append(@" GB</td></tr>
      <tr><td class='k'>OS</td><td class='v'>").Append(S("os")).Append(@"</td></tr>
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

    <div class='footer'>Printed: ").Append(DateTime.Now.ToString("dd MMM yyyy HH:mm")).Append(@" &nbsp;|&nbsp; AMPM Fashions Pvt. Ltd, B-144, Sector 10, Noida - 201301 &nbsp;|&nbsp; IT Department</div>

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
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<table><thead><tr><th>#</th><th>Asset Tag</th><th>Type</th><th>Brand/Model</th><th>Serial</th><th>Condition</th><th>Assigned To</th><th>Dept</th><th>Location</th></tr></thead><tbody>");
        int sno = 0;
        foreach (var a in assets)
        {
            sno++;
            bool assigned = !string.IsNullOrEmpty(a.GetValueOrDefault("assignedToName")?.ToString());
            string cls = assigned ? "green" : "amber";
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td><b>{a.GetValueOrDefault("assetTag")}</b></td><td>{a.GetValueOrDefault("assetType")}</td><td>{a.GetValueOrDefault("brand")} {a.GetValueOrDefault("model")}</td><td>{a.GetValueOrDefault("serial")}</td><td>{a.GetValueOrDefault("condition")}</td><td class='{cls}'>{(assigned ? a.GetValueOrDefault("assignedToName") : "Unassigned")}</td><td>{a.GetValueOrDefault("assignedToDept")}</td><td>{a.GetValueOrDefault("location")}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Assets_{DateTime.Now:yyyyMMdd}.xls");
    }
}
