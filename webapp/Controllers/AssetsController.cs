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
<tr><th style='width:35%'>Asset Tag</th><td>{asset.GetValueOrDefault("assetTag")}</td></tr>
<tr><th>Type</th><td>{asset.GetValueOrDefault("assetType")}</td></tr>
<tr><th>Brand / Model</th><td>{asset.GetValueOrDefault("brand")} {asset.GetValueOrDefault("model")}</td></tr>
<tr><th>Serial No.</th><td>{asset.GetValueOrDefault("serial")}</td></tr>
<tr><th>Processor</th><td>{asset.GetValueOrDefault("processor")}</td></tr>
<tr><th>RAM / Storage</th><td>{asset.GetValueOrDefault("ram")} GB / {asset.GetValueOrDefault("storage")} GB</td></tr>
<tr><th>OS</th><td>{asset.GetValueOrDefault("os")}</td></tr>
<tr><th>Condition</th><td>{asset.GetValueOrDefault("condition")}</td></tr>
</table>
<table>
<tr><th colspan='2'>Assigned To</th></tr>
<tr><th style='width:35%'>Employee Name</th><td>{asset.GetValueOrDefault("assignedToName")}</td></tr>
<tr><th>Emp Code</th><td>{asset.GetValueOrDefault("assignedToEmp")}</td></tr>
<tr><th>Department</th><td>{asset.GetValueOrDefault("assignedToDept")}</td></tr>
<tr><th>Assigned Date</th><td>{asset.GetValueOrDefault("assignedDate")}</td></tr>
<tr><th>Location</th><td>{asset.GetValueOrDefault("location")}</td></tr>
</table>
<p style='font-size:11px'>I acknowledge receipt of the above IT asset in good working condition and agree to return the same upon separation from the company or upon request by the IT Department.</p>
<div class='sign'>
<div>Employee Signature &amp; Date</div>
<div>IT Department Signature &amp; Date</div>
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
