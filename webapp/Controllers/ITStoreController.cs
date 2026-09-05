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
        ViewBag.ScannedIssues = _db.GetScannedKeys("it_issue_scans", "issue_id");
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
            _db.Execute("INSERT INTO it_issue_scans (id,issue_id,file_name,file_data,content_type,uploaded_at,uploaded_by) VALUES (@id,@iid,@fn,@fd,@ct,@at,@by)",
                new { id=scanId, iid=id, fn=scan.FileName, fd=base64, ct=scan.ContentType, at=DateTime.Now.ToString("o"), by=HttpContext.Request.Cookies["ampm_name"] ?? "Sandy" });
        } catch { }
        return Json(new { ok=true, scanId, fileName=scan.FileName });
    }

    [HttpGet]
    public IActionResult ViewScan(string id)
    {
        var scan = _db.GetLatestScan("it_issue_scans", "issue_id", id);
        if (scan == null) return NotFound();
        var bytes = Convert.FromBase64String(scan.GetValueOrDefault("fileData")?.ToString() ?? "");
        var ct = scan.GetValueOrDefault("contentType")?.ToString();
        return File(bytes, string.IsNullOrEmpty(ct) ? "application/octet-stream" : ct);
    }

    [HttpGet]
    public IActionResult PrintIssue(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_issues WHERE issue_no=@id", new { id });
        if (raw == null) return NotFound();
        var issue = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        string S(string k) => issue.GetValueOrDefault(k)?.ToString() ?? "";

        var sb = new System.Text.StringBuilder();
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Material Issue Form</title>
<style>
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#1E293B;margin:34px;font-size:13px}
.header{display:flex;justify-content:space-between;align-items:flex-start;padding-bottom:14px;border-bottom:3px solid #0A192F}
.company-name{font-size:20px;font-weight:800;color:#0A192F;letter-spacing:.5px;margin:0 0 8px 0}
.company-info{font-size:11px;color:#475569;line-height:1.7}
.form-title{font-size:20px;font-weight:800;color:#0A192F;letter-spacing:.5px;text-align:right;margin:0 0 8px 0}
.form-meta{font-size:12px;text-align:right;color:#334155;line-height:1.7}
.form-meta b{color:#0A192F}

.box{margin-top:24px;border:1.5px solid #99F6E4;border-radius:6px;padding:16px 18px}
.box-label{color:#0D9488;font-size:10px;font-weight:700;letter-spacing:.6px;margin-bottom:12px}
.grid{display:flex;flex-wrap:wrap;gap:26px}
.item{min-width:160px}
.item .k{font-size:10px;color:#64748B;letter-spacing:.5px;margin-bottom:3px}
.item .v{font-size:14px;font-weight:700;color:#0F172A}

.sig-row{display:flex;gap:16px;margin-top:70px}
.sig-box{flex:1;border:1px solid #E2E8F0;border-radius:6px;padding:26px 12px 14px 12px;text-align:center}
.sig-line{border-top:1px solid #94A3B8;margin:0 20px 10px 20px}
.sig-name{font-size:12px;font-weight:700;color:#0F172A}
.sig-role{font-size:10px;color:#64748B;margin-top:3px}

.no-print{text-align:center;margin-bottom:20px}
.no-print button{padding:9px 22px;background:#0A192F;color:#fff;border:none;border-radius:5px;cursor:pointer;font-size:13px}
@media print{.no-print{display:none}body{margin:14px}}
</style></head><body>

<div class='no-print'><button onclick='window.print()'>Print / Save as PDF</button></div>

<div class='header'>
  <div>
    <div class='company-name'>AMPM FASHIONS PVT LTD</div>
    <div class='company-info'>B-144 SECTOR 10<br/>NOIDA- 201301<br/>IT Department</div>
  </div>
  <div>
    <div class='form-title'>MATERIAL ISSUE FORM</div>
    <div class='form-meta'><b>").Append(S("issueNo")).Append(@"</b><br/>Date: <b>").Append(S("issueDate")).Append(@"</b></div>
  </div>
</div>

<div class='box'>
  <div class='box-label'>ISSUE DETAILS</div>
  <div class='grid'>
    <div class='item'><div class='k'>ITEM</div><div class='v'>").Append(S("itemName")).Append(@"</div></div>
    <div class='item'><div class='k'>TYPE</div><div class='v'>").Append(S("itemType")).Append(@"</div></div>
    <div class='item'><div class='k'>QUANTITY</div><div class='v'>").Append(S("qty")).Append(@"</div></div>
    <div class='item'><div class='k'>DEPARTMENT</div><div class='v'>").Append(S("dept")).Append(@"</div></div>
    <div class='item'><div class='k'>ISSUED TO</div><div class='v'>").Append(S("empName")).Append(@"</div></div>
    <div class='item'><div class='k'>PURPOSE</div><div class='v'>").Append(S("purpose")).Append(@"</div></div>
    <div class='item'><div class='k'>ISSUED BY</div><div class='v'>").Append(S("issuedBy")).Append(@"</div></div>
  </div>
</div>

<div class='sig-row'>
  <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Issued By</div><div class='sig-role'>IT Department</div></div>
  <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Received By</div><div class='sig-role'>").Append(S("empName")).Append(@" — Employee Signature</div></div>
</div>

</body></html>");
        return Content(sb.ToString(), "text/html");
    }
}
