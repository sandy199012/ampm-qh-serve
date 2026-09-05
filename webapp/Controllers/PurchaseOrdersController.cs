using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AMPMWeb.Controllers;

public class PurchaseOrdersController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public PurchaseOrdersController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? status, string? search)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var pos = _db.GetPOs();
        if (!string.IsNullOrEmpty(status))
            pos = pos.Where(p => p.GetValueOrDefault("status")?.ToString() == status).ToList();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            pos = pos.Where(p =>
                p.GetValueOrDefault("poNumber")?.ToString()?.ToLower().Contains(s)==true ||
                p.GetValueOrDefault("vendorName")?.ToString()?.ToLower().Contains(s)==true).ToList();
        }
        ViewBag.Status = status;
        ViewBag.Search = search;
        return View(pos);
    }

    public IActionResult Details(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        // Decode URL-encoded PO number (/ becomes %2F)
        var poNumber = Uri.UnescapeDataString(id);
        var po = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id = poNumber });
        if (po == null) return NotFound();
        ViewBag.Bills = (_db.KGetObj<List<Dictionary<string,object?>>>("purchase_bills") ?? new())
            .Where(b => b.GetValueOrDefault("poNumber")?.ToString() == poNumber).ToList();
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(po) ?? new());
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.BudgetItems = _db.GetBudget();
        return View();
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        // Generate PO Number
        var year = DateTime.Now.Year;
        var fy = DateTime.Now.Month >= 4 ? $"{year}-{(year+1).ToString()[2..]}" : $"{year-1}-{year.ToString()[2..]}";
        var existing = _db.GetPOs().Count + 1;
        var poNumber = $"AMPM/IT/PO/{fy}/{existing:D4}";

        // Parse items from form
        var items = new List<Dictionary<string,object?>>();
        int i = 0;
        while (form.ContainsKey($"items[{i}][desc]"))
        {
            double.TryParse(form[$"items[{i}][qty]"].ToString(), out var qty);
            double.TryParse(form[$"items[{i}][rate]"].ToString(), out var rate);
            double.TryParse(form[$"items[{i}][gst]"].ToString(), out var gst);
            items.Add(new Dictionary<string,object?>
            {
                ["desc"]   = form[$"items[{i}][desc]"].ToString(),
                ["hsn"]    = form[$"items[{i}][hsn]"].ToString(),
                ["qty"]    = qty,
                ["rate"]   = rate,
                ["gst"]    = gst,
                ["amount"] = qty * rate
            });
            i++;
        }

        double.TryParse(form["subTotal"].ToString(), out var subTotal);
        double.TryParse(form["gstAmount"].ToString(), out var gstAmount);
        double.TryParse(form["grandTotal"].ToString(), out var grandTotal);

        var budgetId = form["budgetId"].ToString();
        var budget = _db.GetBudget();
        var budgetItem = budget.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == budgetId);

        var po = new Dictionary<string,object?>
        {
            ["poNumber"]     = poNumber,
            ["poDate"]       = form["poDate"].ToString(),
            ["date"]         = form["poDate"].ToString(),
            ["vendorName"]   = form["vendorName"].ToString(),
            ["vendorGst"]    = form["vendorGst"].ToString(),
            ["vendorAddr"]   = form["vendorAddr"].ToString(),
            ["vendorPhone"]  = form["vendorPhone"].ToString(),
            ["vendorContact"]= form["vendorContact"].ToString(),
            ["billToName"]   = string.IsNullOrWhiteSpace(form["billToName"].ToString()) ? "AMPM Fashions Pvt. Ltd." : form["billToName"].ToString(),
            ["billToGst"]    = string.IsNullOrWhiteSpace(form["billToGst"].ToString()) ? "09AAFCA4854J1ZE" : form["billToGst"].ToString(),
            ["billToAddr"]   = string.IsNullOrWhiteSpace(form["billToAddr"].ToString()) ? "B-144, Sector 10, Noida - 201301" : form["billToAddr"].ToString(),
            ["shipToName"]   = string.IsNullOrWhiteSpace(form["shipToName"].ToString()) ? form["billToName"].ToString() : form["shipToName"].ToString(),
            ["shipToAddr"]   = string.IsNullOrWhiteSpace(form["shipToAddr"].ToString()) ? form["billToAddr"].ToString() : form["shipToAddr"].ToString(),
            ["approvedBy"]   = form["approvedBy"].ToString(),
            ["purpose"]      = form["purpose"].ToString(),
            ["dept"]         = form["dept"].ToString(),
            ["priority"]     = form["priority"].ToString(),
            ["paymentTerms"] = form["paymentTerms"].ToString(),
            ["deliveryDate"] = form["deliveryDate"].ToString(),
            ["gstType"]      = form["gstType"].ToString(),
            ["notes"]        = form["notes"].ToString(),
            ["status"]       = "Draft",
            ["items"]        = items,
            ["subTotal"]     = subTotal,
            ["gstAmount"]    = gstAmount,
            ["grandTotal"]   = grandTotal,
            ["budgetId"]     = budgetId,
            ["budgetDesc"]   = budgetItem?.GetValueOrDefault("description")?.ToString() ?? "",
            ["createdBy"]    = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy",
            ["createdOn"]    = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };

        string json = JsonConvert.SerializeObject(po);
        _db.Execute("INSERT INTO po_list (po_number,data,vendor,total,status,ts) VALUES (@pn,@data,@vendor,@total,@status,@ts)",
            new { pn=poNumber, data=json, vendor=form["vendorName"].ToString(), total=grandTotal, status="Draft", ts=DateTime.Now.ToString("o") });

        TempData["Success"] = $"PO created: {poNumber}";
        return RedirectToAction("Details", new { id = poNumber });
    }

    [HttpPost]
    public IActionResult UpdateStatus(string id, string status)
    {
        var poNumber = Uri.UnescapeDataString(id);
        var raw = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id = poNumber });
        if (raw == null) return NotFound();
        var po = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        po["status"] = status;
        _db.Execute("UPDATE po_list SET data=@d, status=@s WHERE po_number=@id",
            new { d=JsonConvert.SerializeObject(po), s=status, id=poNumber });
        return Json(new { ok=true });
    }

    [HttpGet("/PurchaseOrders/Print/{*id}")]
    public IActionResult Print(string id)
    {
        var poNumber = Uri.UnescapeDataString(id);
        var raw = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id = poNumber });
        if (raw == null) return NotFound();
        var po = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();

        var itemsRaw = po.GetValueOrDefault("items");
        var items = itemsRaw is JArray ja ? ja.Cast<JObject>().ToList() : new List<JObject>();

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:12px;margin:30px;color:#111}}
.hdrbox{{text-align:center;margin-bottom:6px}}
.hdrbox h2{{margin:0}}
.sub{{text-align:center;color:#555;font-size:11px;margin-bottom:16px}}
.title{{text-align:center;background:#0A192F;color:white;padding:6px;font-weight:bold;margin-bottom:14px}}
table{{border-collapse:collapse;width:100%;margin-bottom:14px}}
td,th{{border:1px solid #333;padding:6px;font-size:11px}}
th{{background:#eee;text-align:left}}
.addrbox{{display:flex;gap:14px;margin-bottom:14px}}
.addrbox > div{{flex:1;border:1px solid #333;padding:8px}}
.addrbox h4{{margin:0 0 6px 0;font-size:12px;background:#1e3a5f;color:white;padding:4px 6px;margin:-8px -8px 8px -8px}}
.totals td{{border:none;padding:3px 6px}}
.totals{{width:300px;margin-left:auto}}
</style></head><body>
<div class='hdrbox'><h2>AMPM FASHIONS PVT. LTD.</h2></div>
<div class='sub'>B-144, Sector 10, Noida - 201301 | GSTIN: 09AAFCA4854J1ZE</div>
<div class='title'>PURCHASE ORDER — {po.GetValueOrDefault("poNumber")}</div>

<table>
<tr><th style='width:25%'>PO Date</th><td>{po.GetValueOrDefault("date")}</td><th style='width:20%'>Priority</th><td>{po.GetValueOrDefault("priority")}</td></tr>
<tr><th>Department</th><td>{po.GetValueOrDefault("dept")}</td><th>Delivery Date</th><td>{po.GetValueOrDefault("deliveryDate")}</td></tr>
<tr><th>Payment Terms</th><td>{po.GetValueOrDefault("paymentTerms")}</td><th>Purpose</th><td>{po.GetValueOrDefault("purpose")}</td></tr>
</table>

<div class='addrbox'>
<div><h4>VENDOR</h4><b>{po.GetValueOrDefault("vendorName")}</b><br/>GST: {po.GetValueOrDefault("vendorGst")}<br/>{po.GetValueOrDefault("vendorAddr")}<br/>Ph: {po.GetValueOrDefault("vendorPhone")} | {po.GetValueOrDefault("vendorContact")}</div>
<div><h4>BILL TO</h4><b>{po.GetValueOrDefault("billToName")}</b><br/>GST: {po.GetValueOrDefault("billToGst")}<br/>{po.GetValueOrDefault("billToAddr")}</div>
<div><h4>SHIP TO</h4><b>{po.GetValueOrDefault("shipToName")}</b><br/>{po.GetValueOrDefault("shipToAddr")}</div>
</div>

<table>
<thead><tr><th>#</th><th>Description</th><th>HSN</th><th>Qty</th><th>Rate</th><th>GST%</th><th>Amount</th></tr></thead>
<tbody>");
        int sno = 0;
        foreach (var item in items)
        {
            sno++;
            double.TryParse(item["rate"]?.ToString(), out var rate);
            double.TryParse(item["qty"]?.ToString(), out var qty);
            sb.Append($"<tr><td>{sno}</td><td>{item["desc"]}</td><td>{item["hsn"]}</td><td style='text-align:center'>{qty}</td><td style='text-align:right'>₹{rate:N2}</td><td style='text-align:center'>{item["gst"]}%</td><td style='text-align:right'>₹{(qty*rate):N2}</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append($@"<table class='totals'>
<tr><td>Sub Total</td><td style='text-align:right'>₹{po.GetValueOrDefault("subTotal")}</td></tr>
<tr><td>GST ({po.GetValueOrDefault("gstType")})</td><td style='text-align:right'>₹{po.GetValueOrDefault("gstAmount")}</td></tr>
<tr style='font-weight:bold;border-top:2px solid #333'><td>Grand Total</td><td style='text-align:right'>₹{po.GetValueOrDefault("grandTotal")}</td></tr>
</table>");

        if (!string.IsNullOrEmpty(po.GetValueOrDefault("notes")?.ToString()))
            sb.Append($"<p><b>Notes/Terms:</b> {po.GetValueOrDefault("notes")}</p>");

        sb.Append($@"
<div style='margin-top:50px;display:flex;justify-content:space-between'>
<div style='width:40%;border-top:1px solid #333;padding-top:6px;text-align:center;font-size:11px'>Prepared By — {po.GetValueOrDefault("createdBy")}</div>
<div style='width:40%;border-top:1px solid #333;padding-top:6px;text-align:center;font-size:11px'>Approved By — {po.GetValueOrDefault("approvedBy")}</div>
</div>
</body></html>");
        return Content(sb.ToString(), "text/html");
    }
}
