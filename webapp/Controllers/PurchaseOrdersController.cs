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
            ["billToName"]   = string.IsNullOrWhiteSpace(form["billToName"].ToString()) ? "AMPM Fashions Pvt Ltd" : form["billToName"].ToString(),
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

        string S(string key) => po.GetValueOrDefault(key)?.ToString() ?? "";
        double.TryParse(S("subTotal"), out var subTotal);
        double.TryParse(S("gstAmount"), out var gstAmount);
        double.TryParse(S("grandTotal"), out var grandTotal);
        var priority = S("priority");
        var priColor = priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase) || priority.Equals("High", StringComparison.OrdinalIgnoreCase) ? "#DC2626" : "#0F172A";
        var status = string.IsNullOrEmpty(S("status")) ? "Draft" : S("status");

        var sb = new System.Text.StringBuilder();
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Purchase Order</title>
<style>
*{box-sizing:border-box}
body{font-family:Arial,Helvetica,sans-serif;color:#1E293B;margin:34px;font-size:13px}
.header{display:flex;justify-content:space-between;align-items:flex-start;padding-bottom:14px;border-bottom:3px solid #0A192F}
.company-name{font-size:22px;font-weight:800;color:#0A192F;letter-spacing:.5px;margin:0 0 8px 0}
.company-info{font-size:11px;color:#475569;line-height:1.7}
.po-title{font-size:22px;font-weight:800;color:#0A192F;letter-spacing:.5px;text-align:right;margin:0 0 8px 0}
.po-meta{font-size:12px;text-align:right;color:#334155;line-height:1.7}
.po-meta b{color:#0A192F}
.status-badge{font-size:11px;color:#94A3B8;text-align:right;margin-top:6px;font-style:italic}

.box-row{display:flex;gap:14px;margin-top:22px}
.box{flex:1;border:1.5px solid;border-radius:6px;padding:12px 14px}
.box-label{font-size:10px;font-weight:700;letter-spacing:.6px;margin-bottom:8px}
.box .nm{font-size:13px;font-weight:700;color:#0F172A;margin-bottom:2px}
.box .line{font-size:11px;color:#475569;margin-top:3px}

.vendor-box{border-color:#FDBA74}.vendor-box .box-label{color:#C2410C}
.billto-box{border-color:#FDE68A}.billto-box .box-label{color:#B45309}
.shipto-box{border-color:#99F6E4}.shipto-box .box-label{color:#0D9488}
.deliver{color:#059669;font-weight:600}

.req-box{margin-top:14px;border:1.5px solid #99F6E4;border-radius:6px;padding:14px 16px}
.req-box .box-label{color:#0D9488;font-size:10px;font-weight:700;letter-spacing:.6px;margin-bottom:10px}
.req-grid{display:flex;gap:34px;flex-wrap:wrap}
.req-item{min-width:150px}
.req-item .k{font-size:10px;color:#64748B;letter-spacing:.5px;margin-bottom:3px}
.req-item .v{font-size:13px;font-weight:700;color:#0F172A}
.req-item .v.small{font-size:10px;font-weight:400;color:#64748B;margin-top:2px}

table.items{width:100%;border-collapse:collapse;margin-top:24px}
table.items thead th{font-size:10px;color:#64748B;letter-spacing:.5px;text-align:left;padding:8px 6px;border-bottom:2px solid #E2E8F0}
table.items tbody td{font-size:12px;padding:9px 6px;border-bottom:1px solid #F1F5F9}
.num{text-align:right}.center{text-align:center}

.totals-wrap{display:flex;justify-content:flex-end;margin-top:18px}
.totals-box{width:300px;border:1.5px solid #E2E8F0;border-radius:6px;padding:14px 16px}
.totals-box .row{display:flex;justify-content:space-between;font-size:12px;padding:4px 0;color:#334155}
.totals-box .row b{color:#0F172A}
.totals-box .grand{border-top:2px solid #CBD5E1;margin-top:8px;padding-top:10px;display:flex;justify-content:space-between;align-items:baseline}
.totals-box .grand .lbl{font-size:12px;color:#94A3B8;font-weight:700}
.totals-box .grand .val{font-size:19px;font-weight:800;color:#0A192F}
.words{font-size:11px;font-style:italic;color:#1D4ED8;text-align:right;margin-top:8px}

.notes-box{margin-top:22px;background:#FFFBEB;border:1.5px solid #FDE68A;border-radius:6px;padding:12px 14px;font-size:11px;color:#78350F;line-height:1.7}

.sig-row{display:flex;gap:16px;margin-top:56px}
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
    <div class='company-info'>
      B-144 SECTOR 10<br/>NOIDA- 201301<br/>
      &#9742; 9871988372 &nbsp;|&nbsp; &#9993; itsupport@ampm.in<br/>
      GSTIN: <b>09AAFCA4854J1ZE</b>
    </div>
  </div>
  <div>
    <div class='po-title'>PURCHASE ORDER</div>
    <div class='po-meta'>
      <b>").Append(S("poNumber")).Append(@"</b><br/>
      Date: <b>").Append(S("date")).Append(@"</b><br/>
      Delivery By: <b>").Append(S("deliveryDate")).Append(@"</b>
    </div>
    <div class='status-badge'>").Append(status).Append(@"</div>
  </div>
</div>

<div class='box-row'>
  <div class='box vendor-box'>
    <div class='box-label'>VENDOR / SUPPLIER</div>
    <div class='nm'>").Append(S("vendorName")).Append(@"</div>
    <div class='line'>").Append(S("vendorAddr")).Append(@"</div>
    <div class='line'>GSTIN: <b>").Append(S("vendorGst")).Append(@"</b></div>
    <div class='line'>Attn: ").Append(S("vendorContact")).Append(@"</div>
    <div class='line'>&#9742; ").Append(S("vendorPhone")).Append(@"</div>
    <div class='line'>Payment Terms: <b>").Append(S("paymentTerms")).Append(@"</b></div>
  </div>
  <div class='box billto-box'>
    <div class='box-label'>BILL TO</div>
    <div class='nm'>").Append(S("billToName")).Append(@"</div>
    <div class='line'>GSTIN: <b>").Append(S("billToGst")).Append(@"</b></div>
    <div class='line'>").Append(S("billToAddr")).Append(@"</div>
  </div>
  <div class='box shipto-box'>
    <div class='box-label'>SHIP TO / DELIVER TO</div>
    <div class='nm'>").Append(S("shipToName")).Append(@"</div>
    <div class='line'>").Append(S("shipToAddr")).Append(@"</div>
    <div class='line deliver'>&#128197; Deliver by: ").Append(S("deliveryDate")).Append(@"</div>
  </div>
</div>

<div class='req-box'>
  <div class='box-label'>REQUISITION DETAILS</div>
  <div class='req-grid'>
    <div class='req-item'>
      <div class='k'>REQUESTED BY</div>
      <div class='v'>").Append(S("createdBy").ToUpper()).Append(@"</div>
      <div class='v small'>SYSTEM ADMINISTRATOR</div>
    </div>
    <div class='req-item'>
      <div class='k'>DEPARTMENT</div>
      <div class='v'>").Append(S("dept")).Append(@"</div>
    </div>
    <div class='req-item'>
      <div class='k'>APPROVED BY / HOD</div>
      <div class='v'>").Append(S("approvedBy")).Append(@"</div>
    </div>
    <div class='req-item'>
      <div class='k'>PRIORITY</div>
      <div class='v' style='color:").Append(priColor).Append(@"'>").Append(priority).Append(@"</div>
    </div>
    <div class='req-item'>
      <div class='k'>PURPOSE / REASON</div>
      <div class='v'>").Append(S("purpose")).Append(@"</div>
    </div>
  </div>
</div>

<table class='items'>
<thead><tr><th style='width:28px'>#</th><th>DESCRIPTION</th><th class='center'>QTY</th><th class='num'>RATE</th><th class='num'>AMOUNT</th><th class='center'>GST%</th><th class='num'>GST AMT</th><th class='num'>TOTAL</th></tr></thead>
<tbody>");
        int sno = 0;
        foreach (var item in items)
        {
            sno++;
            double.TryParse(item["rate"]?.ToString(), out var rate);
            double.TryParse(item["qty"]?.ToString(), out var qty);
            double.TryParse(item["gst"]?.ToString(), out var gstPct);
            var amount = qty * rate;
            var gstAmt = amount * gstPct / 100;
            var lineTotal = amount + gstAmt;
            sb.Append($"<tr><td>{sno}</td><td>{item["desc"]}</td><td class='center'>{qty:0.##}</td><td class='num'>₹{rate:N2}</td><td class='num'>₹{amount:N2}</td><td class='center'>{gstPct:0.##}%</td><td class='num'>₹{gstAmt:N2}</td><td class='num'><b>₹{lineTotal:N2}</b></td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append(@"<div class='totals-wrap'><div class='totals-box'>
  <div class='row'>Subtotal <b>&#8377;").Append(subTotal.ToString("N2")).Append(@"</b></div>
  <div class='row'>GST Total <b>&#8377;").Append(gstAmount.ToString("N2")).Append(@"</b></div>
  <div class='grand'><span class='lbl'>GRAND TOTAL</span><span class='val'>&#8377;").Append(grandTotal.ToString("N2")).Append(@"</span></div>
</div></div>
<div class='words'>Amount in Words: <b>").Append(NumberToWords(grandTotal)).Append(@"</b></div>

<div class='notes-box'><b>Notes:</b> E. &amp; O. E. 1. Our risk &amp; responsibility ceases on delivery of goods to the carrier. 2. No complaint whatsoever shall be entertained regarding quantity &amp; quality of the goods once the same are collected/Despatched from our work. 3. All disputes are subject to Gautam Budh Nagar Jurisdiction.</div>");

        if (!string.IsNullOrEmpty(S("notes")))
            sb.Append("<div class='notes-box' style='margin-top:10px'><b>Additional Notes:</b> ").Append(S("notes")).Append("</div>");

        sb.Append(@"<div class='sig-row'>
  <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Prepared By</div><div class='sig-role'>IT Department</div></div>
  <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Approved By</div><div class='sig-role'>").Append(S("approvedBy")).Append(@"</div></div>
  <div class='sig-box'><div class='sig-line'></div><div class='sig-name'>Authorised Signatory</div><div class='sig-role'>Sandeep Kumar | HOD- Accounts &amp; Finance</div></div>
</div>

</body></html>");
        return Content(sb.ToString(), "text/html");
    }

    static string NumberToWords(double num)
    {
        long rupees = (long)Math.Round(num);
        if (rupees == 0) return "Zero Rupees Only";
        return ConvertIndian(rupees) + " Rupees Only";
    }

    static readonly string[] Ones = {"","One","Two","Three","Four","Five","Six","Seven","Eight","Nine","Ten",
        "Eleven","Twelve","Thirteen","Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"};
    static readonly string[] Tens = {"","","Twenty","Thirty","Forty","Fifty","Sixty","Seventy","Eighty","Ninety"};

    static string TwoDigits(long x)
    {
        if (x < 20) return Ones[x];
        return Tens[x/10] + (x%10 > 0 ? " " + Ones[x%10] : "");
    }

    static string ThreeDigits(long x)
    {
        string s = "";
        if (x >= 100) { s += Ones[x/100] + " Hundred"; x %= 100; if (x > 0) s += " "; }
        if (x > 0) s += TwoDigits(x);
        return s;
    }

    static string ConvertIndian(long n)
    {
        if (n == 0) return "Zero";
        var parts = new List<string>();
        long crore = n / 10000000; n %= 10000000;
        long lakh = n / 100000; n %= 100000;
        long thousand = n / 1000; n %= 1000;
        long rest = n;

        if (crore > 0) parts.Add(TwoDigits(crore) + " Crore");
        if (lakh > 0) parts.Add(TwoDigits(lakh) + " Lakh");
        if (thousand > 0) parts.Add(TwoDigits(thousand) + " Thousand");
        if (rest > 0) parts.Add(ThreeDigits(rest));

        return string.Join(" ", parts);
    }
}
