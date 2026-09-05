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

        int issueSeq = 1;
        try { issueSeq = _db.QueryFirst<int>("SELECT COUNT(*) FROM it_stock_issues") + 1; } catch {}
        var issueNo = $"ISS-{issueSeq:D4}";
        var empId = form["empId"].ToString();
        var empName = form["empName"].ToString();
        var issue = new Dictionary<string,object?>
        {
            ["issueNo"]    = issueNo,
            ["itemId"]     = id,
            ["itemName"]   = item.GetValueOrDefault("name"),
            ["itemType"]   = item.GetValueOrDefault("itemType"),
            ["itemBrand"]  = item.GetValueOrDefault("brand"),
            ["qty"]        = qty,
            ["dept"]       = form["dept"].ToString(),
            ["empName"]    = empName,
            ["empId"]      = empId,
            ["empDesig"]   = form["empDesig"].ToString(),
            ["empHod"]     = form["empHod"].ToString(),
            ["purpose"]    = form["purpose"].ToString(),
            ["remarks"]    = form["remarks"].ToString(),
            ["status"]     = "Issued",
            ["issueDate"]  = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["issuedBy"]   = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy",
        };
        _db.Execute("INSERT INTO it_stock_issues (id,issue_no,emp_id,emp_name,dept,item_id,status,data,ts) VALUES (@id2,@ino,@eid,@ename,@dept,@iid,'Issued',@data,@ts)",
            new { id2=Guid.NewGuid().ToString("N")[..8], ino=issueNo, eid=empId, ename=empName, dept=form["dept"].ToString(), iid=id, data=JsonConvert.SerializeObject(issue), ts=DateTime.Now.ToString("o") });
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
        var itemId = S("itemId");
        var empId = S("empId");
        var empName = S("empName");
        var thisIssueNo = S("issueNo");

        // Walk issue history (newest first) to find the most recent PRIOR issue
        // of this same item, and the most recent PRIOR issue to this same employee.
        string prevItemLine = "First time this item is being issued";
        string prevEmpLine = "First time to this employee";
        try
        {
            var allIssues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY ts DESC")
                .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
                .ToList();
            bool pastCurrent = false;
            foreach (var other in allIssues)
            {
                var oNo = other.GetValueOrDefault("issueNo")?.ToString() ?? "";
                if (!pastCurrent)
                {
                    if (oNo == thisIssueNo) pastCurrent = true;
                    continue;
                }
                if (prevItemLine.StartsWith("First") && !string.IsNullOrEmpty(itemId) &&
                    other.GetValueOrDefault("itemId")?.ToString() == itemId)
                {
                    prevItemLine = $"{other.GetValueOrDefault("issueDate")} to {other.GetValueOrDefault("empName")} ({other.GetValueOrDefault("dept")})";
                }
                var oEmpId = other.GetValueOrDefault("empId")?.ToString() ?? "";
                var oEmpName = other.GetValueOrDefault("empName")?.ToString() ?? "";
                bool sameEmp = (!string.IsNullOrEmpty(empId) && oEmpId == empId) ||
                               (string.IsNullOrEmpty(empId) && !string.IsNullOrEmpty(empName) && oEmpName == empName);
                if (prevEmpLine.StartsWith("First") && sameEmp)
                {
                    prevEmpLine = $"{other.GetValueOrDefault("issueDate")} — {other.GetValueOrDefault("itemName")}";
                }
                if (!prevItemLine.StartsWith("First") && !prevEmpLine.StartsWith("First")) break;
            }
        }
        catch { }

        var sb = new System.Text.StringBuilder();
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Material Issue Form</title>
<style>
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#1E293B;margin:0;background:#F1F5F9;font-size:12.5px}
.wrap{max-width:760px;margin:26px auto;background:#fff;border:1px solid #CBD5E1;border-radius:6px;overflow:hidden}
.pad{padding:0 20px 20px 20px}
.header{background:#0A192F;color:#fff;padding:16px 20px;display:flex;justify-content:space-between;align-items:center}
.header .co{font-size:16px;font-weight:800;letter-spacing:.3px}
.header .sub{font-size:10.5px;color:#5EEAD4;margin-top:3px}
.header .issno{font-size:13px;font-weight:700;text-align:right}

.sec-hdr{background:#1e3a5f;color:#fff;font-size:10.5px;font-weight:700;letter-spacing:.6px;padding:7px 14px;margin-top:16px}
table.kv{width:100%;border-collapse:collapse;border:1px solid #E2E8F0;border-top:none}
table.kv td{padding:7px 14px;font-size:12px;border-bottom:1px solid #F1F5F9}
table.kv tr:last-child td{border-bottom:none}
table.kv td.k{width:36%;font-weight:700;color:#334155}
table.kv td.v{color:#0F172A}

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
      <div class='sub'>IT Department — Item Issue Form</div>
    </div>
    <div class='issno'>Issue No: ").Append(thisIssueNo).Append(@"</div>
  </div>
  <div class='pad'>

    <div class='sec-hdr'>ITEM DETAILS</div>
    <table class='kv'>
      <tr><td class='k'>Item Name</td><td class='v'>").Append(S("itemName")).Append(@"</td></tr>
      <tr><td class='k'>Type / Category</td><td class='v'>").Append(S("itemType")).Append(@"</td></tr>
      <tr><td class='k'>Brand</td><td class='v'>").Append(S("itemBrand")).Append(@"</td></tr>
      <tr><td class='k'>Quantity</td><td class='v'>").Append(S("qty")).Append(@" piece(s)</td></tr>
      <tr><td class='k'>Issue Date &amp; Time</td><td class='v'>").Append(S("issueDate")).Append(@"</td></tr>
      <tr><td class='k'>Purpose</td><td class='v'>").Append(S("purpose")).Append(@"</td></tr>
      <tr><td class='k'>Prev Issue (This Item)</td><td class='v'>").Append(prevItemLine).Append(@"</td></tr>
      <tr><td class='k'>Prev Issue (This Emp)</td><td class='v'>").Append(prevEmpLine).Append(@"</td></tr>
    </table>

    <div class='sec-hdr'>ISSUED TO</div>
    <table class='kv'>
      <tr><td class='k'>Name</td><td class='v'>").Append(empName.ToUpper()).Append(@"</td></tr>
      <tr><td class='k'>Employee ID</td><td class='v'>").Append(S("empId")).Append(@"</td></tr>
      <tr><td class='k'>Department</td><td class='v'>").Append(S("dept")).Append(@"</td></tr>
      <tr><td class='k'>Designation</td><td class='v'>").Append(S("empDesig")).Append(@"</td></tr>
      <tr><td class='k'>HOD / Manager</td><td class='v'>").Append(S("empHod")).Append(@"</td></tr>
    </table>

    <div class='sec-hdr'>AUTHORIZATION</div>
    <table class='kv'>
      <tr><td class='k'>Issued By</td><td class='v'>").Append(S("issuedBy")).Append(@" - IT Admin</td></tr>
      <tr><td class='k'>Status</td><td class='v'>").Append(S("status")).Append(@"</td></tr>
      <tr><td class='k'>Remarks</td><td class='v'>").Append(S("remarks")).Append(@"</td></tr>
    </table>

    <div class='sig-row'>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Employee Signature</div><div class='sig-pre'>").Append(empName.ToUpper()).Append(@"</div></div>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>IT Authorized By</div><div class='sig-pre'>").Append(S("issuedBy")).Append(@" - IT Admin</div></div>
      <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>HOD Approval</div><div class='sig-pre'>").Append(S("empHod").ToUpper()).Append(@"</div></div>
    </div>

    <div class='footer'>Printed: ").Append(DateTime.Now.ToString("dd MMM yyyy HH:mm")).Append(@" &nbsp;|&nbsp; AMPM Fashions Pvt. Ltd, B-144, Sector 10, Noida - 201301 &nbsp;|&nbsp; IT Department</div>

  </div>
</div>

</body></html>");
        return Content(sb.ToString(), "text/html");
    }
}
