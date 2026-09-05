using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class ITStoreController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ITStoreController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? type)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var items = new List<Dictionary<string,object?>>();
        var issues = new List<Dictionary<string,object?>>();
        try {
            var sql = string.IsNullOrEmpty(type)
                ? "SELECT data FROM it_stock_items ORDER BY item_type, ts"
                : "SELECT data FROM it_stock_items WHERE item_type=@t ORDER BY ts";
            items = _db.Query<string>(sql, new { t=type }).Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        } catch {}
        try {
            issues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY ts DESC LIMIT 100")
                .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        } catch {}
        if (!string.IsNullOrEmpty(type))
            issues = issues.Where(i => i.GetValueOrDefault("itemType")?.ToString() == type).ToList();
        ViewBag.Issues = issues;
        ViewBag.TypeFilter = type;
        ViewBag.TotalIssued = issues.Sum(i => { int.TryParse(i.GetValueOrDefault("qty")?.ToString(), out var q); return q; });
        return View(items);
    }

    [HttpGet] public IActionResult AddItem() { ViewBag.User = _auth.GetCurrentUser(HttpContext); return View(); }

    [HttpPost]
    public IActionResult AddItem(IFormCollection form)
    {
        var item = new Dictionary<string,object?>
        {
            ["id"]       = Guid.NewGuid().ToString("N")[..8],
            ["itemType"] = form["itemType"].ToString(),
            ["name"]     = form["name"].ToString(),
            ["brand"]    = form["brand"].ToString(),
            ["model"]    = form["model"].ToString(),
            ["specs"]    = form["specs"].ToString(),
            ["totalQty"] = int.TryParse(form["totalQty"].ToString(), out var q) ? q : 0,
            ["issuedQty"]= 0,
            ["vendor"]   = form["vendor"].ToString(),
            ["unitCost"] = double.TryParse(form["unitCost"].ToString(), out var c) ? c : 0,
            ["location"] = "IT Store",
        };
        _db.Execute("INSERT INTO it_stock_items (id,item_type,data,ts) VALUES (@id,@type,@data,@ts)",
            new { id=item["id"], type=item["itemType"], data=JsonConvert.SerializeObject(item), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Item '{item["name"]}' added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult StockIn(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        item["id"] = id;
        return View(item);
    }

    [HttpPost]
    public IActionResult StockIn(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        int.TryParse(item.GetValueOrDefault("totalQty")?.ToString(), out var cur);
        int.TryParse(form["qty"].ToString(), out var add);
        item["totalQty"] = cur + add;
        if (!string.IsNullOrEmpty(form["vendor"].ToString())) item["vendor"] = form["vendor"].ToString();
        _db.Execute("UPDATE it_stock_items SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(item), id });
        TempData["Success"] = $"Stock added! New total: {cur+add}";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Issue(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        item["id"] = id;
        return View(item);
    }

    [HttpPost]
    public IActionResult Issue(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        int.TryParse(item.GetValueOrDefault("issuedQty")?.ToString(), out var cur);
        int.TryParse(form["qty"].ToString(), out var qty);
        item["issuedQty"] = cur + qty;
        _db.Execute("UPDATE it_stock_items SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(item), id });

        var issueNo = $"ISS-{DateTime.Now:yyyyMMddHHmmss}";
        var issue = new Dictionary<string,object?>
        {
            ["issueNo"]   = issueNo,
            ["itemId"]    = id,
            ["itemName"]  = item.GetValueOrDefault("name"),
            ["itemType"]  = item.GetValueOrDefault("itemType"),
            ["qty"]       = qty,
            ["dept"]      = form["dept"].ToString(),
            ["empName"]   = form["empName"].ToString(),
            ["purpose"]   = form["purpose"].ToString(),
            ["issueDate"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["issuedBy"]  = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy",
        };
        _db.Execute("INSERT INTO it_stock_issues (id,issue_no,emp_id,emp_name,dept,item_id,status,data,ts) VALUES (@id2,@ino,'','',@dept,@iid,'Issued',@data,@ts)",
            new { id2=Guid.NewGuid().ToString("N")[..8], ino=issueNo, dept=form["dept"].ToString(), iid=id, data=JsonConvert.SerializeObject(issue), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Issued {qty} items! Issue No: {issueNo}";
        return RedirectToAction("Index");
    }

    [HttpGet("/ITStore/Export")]
    public IActionResult Export()
    {
        var items = new List<Dictionary<string,object?>>();
        var issues = new List<Dictionary<string,object?>>();
        try { items = _db.Query<string>("SELECT data FROM it_stock_items ORDER BY item_type").Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList(); } catch {}
        try { issues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY ts DESC").Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList(); } catch {}

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:11px}}table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:white;padding:6px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px;border:1px solid #CBD5E1;font-size:10px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
.h2{{background:#1e3a5f;color:#06B6D4;padding:5px 10px;margin-top:16px;font-weight:bold}}
.green{{color:#059669;font-weight:bold}}.red{{color:#DC2626;font-weight:bold}}.amber{{color:#D97706;font-weight:bold}}
</style></head><body>
<table style='margin-bottom:12px'><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — IT STORE STOCK REPORT</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<div class='h2'>STOCK INVENTORY</div>
<table><thead><tr><th>#</th><th>Type</th><th>Item Name</th><th>Brand</th><th>Model</th><th>Total</th><th>Issued</th><th>Balance</th><th>Vendor</th><th>Cost</th></tr></thead><tbody>");

        int sno = 0;
        foreach (var item in items)
        {
            sno++;
            int.TryParse(item.GetValueOrDefault("totalQty")?.ToString(), out var tq);
            int.TryParse(item.GetValueOrDefault("issuedQty")?.ToString(), out var iq);
            int bal = tq - iq;
            string bc = bal <= 0 ? "red" : bal <= 2 ? "amber" : "green";
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td>{item.GetValueOrDefault("itemType")}</td><td><b>{item.GetValueOrDefault("name")}</b></td><td>{item.GetValueOrDefault("brand")}</td><td>{item.GetValueOrDefault("model")}</td><td style='text-align:center'>{tq}</td><td style='text-align:center'>{iq}</td><td style='text-align:center' class='{bc}'>{bal}</td><td>{item.GetValueOrDefault("vendor")}</td><td>₹{item.GetValueOrDefault("unitCost")}</td></tr>");
        }
        sb.Append("</tbody></table><div class='h2' style='margin-top:20px'>ISSUE HISTORY</div><table><thead><tr><th>#</th><th>Issue No.</th><th>Date</th><th>Item</th><th>Type</th><th>Qty</th><th>Department</th><th>Issued To</th><th>Purpose</th><th>By</th></tr></thead><tbody>");
        sno = 0;
        foreach (var iss in issues)
        {
            sno++;
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td style='font-family:monospace'>{iss.GetValueOrDefault("issueNo")}</td><td>{iss.GetValueOrDefault("issueDate")}</td><td><b>{iss.GetValueOrDefault("itemName")}</b></td><td>{iss.GetValueOrDefault("itemType")}</td><td style='text-align:center;font-weight:bold'>{iss.GetValueOrDefault("qty")}</td><td>{iss.GetValueOrDefault("dept")}</td><td>{iss.GetValueOrDefault("empName")}</td><td>{iss.GetValueOrDefault("purpose")}</td><td>{iss.GetValueOrDefault("issuedBy")}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_ITStore_{DateTime.Now:yyyyMMdd}.xls");
    }

    [HttpPost]
    public async Task<IActionResult> UploadScan(string id, IFormFile scan)
    {
        if (scan == null || scan.Length == 0) return Json(new { ok=false, msg="No file" });
        using var ms = new MemoryStream();
        await scan.CopyToAsync(ms);
        string base64 = Convert.ToBase64String(ms.ToArray());
        string scanId = Guid.NewGuid().ToString("N")[..8];
        try {
            _db.Execute("INSERT INTO it_issue_scans (id,issue_id,file_name,file_data,uploaded_at,uploaded_by) VALUES (@id,@iid,@fn,@fd,@at,@by)",
                new { id=scanId, iid=id, fn=scan.FileName, fd=base64, at=DateTime.Now.ToString("o"), by=HttpContext.Request.Cookies["ampm_name"] ?? "Sandy" });
        } catch { }
        return Json(new { ok=true, scanId, fileName=scan.FileName });
    }
}
