using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class BillsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public BillsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? status, string? search)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var bills = _db.GetBills();

        // auto-flag overdue on read
        bool changed = false;
        foreach (var b in bills)
        {
            var st = b.GetValueOrDefault("status")?.ToString();
            if ((st == "Pending" || st == "Submitted") &&
                DateTime.TryParse(b.GetValueOrDefault("dueDate")?.ToString(), out var dd) && dd.Date < DateTime.Today)
            {
                b["status"] = "Overdue";
                changed = true;
            }
        }
        if (changed) _db.SaveBills(bills);

        if (!string.IsNullOrEmpty(status))
            bills = bills.Where(b => b.GetValueOrDefault("status")?.ToString() == status).ToList();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            bills = bills.Where(b =>
                b.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s) == true ||
                b.GetValueOrDefault("category")?.ToString()?.ToLower().Contains(s) == true ||
                b.GetValueOrDefault("vendor")?.ToString()?.ToLower().Contains(s) == true
            ).ToList();
        }

        bills = bills.OrderBy(b => b.GetValueOrDefault("dueDate")?.ToString()).ToList();

        ViewBag.StatusFilter = status;
        ViewBag.Search = search;
        ViewBag.Total = bills.Count;
        ViewBag.TotalAmount = bills.Sum(b => { double.TryParse(b.GetValueOrDefault("amount")?.ToString(), out var a); return a; });
        return View(bills);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        double.TryParse(form["amount"].ToString(), out var amt);
        int.TryParse(form["alertDays"].ToString(), out var alertDays);
        var bill = new Dictionary<string,object?>
        {
            ["id"]            = Guid.NewGuid().ToString("N")[..8],
            ["name"]          = form["name"].ToString(),
            ["category"]      = form["category"].ToString(),
            ["vendor"]        = form["vendor"].ToString(),
            ["dueDate"]       = form["dueDate"].ToString(),
            ["amount"]        = amt,
            ["period"]        = form["period"].ToString(),
            ["status"]        = "Pending",
            ["alertDays"]     = alertDays > 0 ? alertDays : 7,
            ["submittedDate"] = "",
            ["submittedBy"]   = "",
            ["paidDate"]      = "",
            ["paidBy"]        = "",
            ["paymentRef"]    = "",
            ["invoiceNo"]     = "",
            ["notes"]         = form["notes"].ToString(),
        };
        var bills = _db.GetBills();
        bills.Add(bill);
        _db.SaveBills(bills);
        TempData["Success"] = $"Bill '{bill["name"]}' added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var bill = _db.GetBills().FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();
        return View(bill);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var bills = _db.GetBills();
        var bill = bills.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();

        double.TryParse(form["amount"].ToString(), out var amt);
        int.TryParse(form["alertDays"].ToString(), out var alertDays);
        bill["name"]      = form["name"].ToString();
        bill["category"]  = form["category"].ToString();
        bill["vendor"]    = form["vendor"].ToString();
        bill["dueDate"]   = form["dueDate"].ToString();
        bill["amount"]    = amt;
        bill["period"]    = form["period"].ToString();
        bill["alertDays"] = alertDays > 0 ? alertDays : 7;
        bill["notes"]     = form["notes"].ToString();

        _db.SaveBills(bills);
        TempData["Success"] = "Bill updated!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult MarkSubmitted(string id)
    {
        var bills = _db.GetBills();
        var bill = bills.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();

        bill["status"]        = "Submitted";
        bill["submittedDate"] = DateTime.Today.ToString("yyyy-MM-dd");
        bill["submittedBy"]   = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";

        _db.SaveBills(bills);
        TempData["Success"] = $"'{bill["name"]}' marked Submitted.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult MarkPaid(string id, IFormCollection form)
    {
        var bills = _db.GetBills();
        var bill = bills.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();

        // auto-backfill submission if it was skipped
        if (string.IsNullOrEmpty(bill.GetValueOrDefault("submittedDate")?.ToString()))
        {
            bill["submittedDate"] = DateTime.Today.ToString("yyyy-MM-dd");
            bill["submittedBy"]   = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";
        }
        bill["status"]     = "Paid";
        bill["paidDate"]   = DateTime.Today.ToString("yyyy-MM-dd");
        bill["paidBy"]     = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";
        bill["paymentRef"] = form["paymentRef"].ToString();
        bill["invoiceNo"]  = form["invoiceNo"].ToString();

        _db.SaveBills(bills);
        TempData["Success"] = $"'{bill["name"]}' marked Paid.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var bills = _db.GetBills();
        bills.RemoveAll(b => b.GetValueOrDefault("id")?.ToString() == id);
        _db.SaveBills(bills);
        TempData["Success"] = "Bill deleted.";
        return RedirectToAction("Index");
    }

    [HttpGet("/Bills/Export")]
    public IActionResult Export()
    {
        var bills = _db.GetBills().OrderBy(b => b.GetValueOrDefault("dueDate")?.ToString()).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:11px}}table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:white;padding:6px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px;border:1px solid #CBD5E1;font-size:10px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
.green{{color:#059669;font-weight:bold}}.red{{color:#DC2626;font-weight:bold}}.amber{{color:#D97706;font-weight:bold}}.teal{{color:#0891B2;font-weight:bold}}
</style></head><body>
<table style='margin-bottom:12px'><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — BILLS & UTILITIES REPORT</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<table><thead><tr><th>#</th><th>Name</th><th>Category</th><th>Vendor/Place</th><th>Due Date</th><th>Amount</th><th>Period</th><th>Status</th></tr></thead><tbody>");

        int sno = 0;
        foreach (var b in bills)
        {
            sno++;
            var st = b.GetValueOrDefault("status")?.ToString();
            string cls = st == "Overdue" ? "red" : st == "Paid" ? "green" : st == "Submitted" ? "teal" : "amber";
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td><b>{b.GetValueOrDefault("name")}</b></td><td>{b.GetValueOrDefault("category")}</td><td>{b.GetValueOrDefault("vendor")}</td><td>{b.GetValueOrDefault("dueDate")}</td><td>₹{b.GetValueOrDefault("amount")}</td><td>{b.GetValueOrDefault("period")}</td><td class='{cls}'>{st}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Bills_{DateTime.Now:yyyyMMdd}.xls");
    }
}
