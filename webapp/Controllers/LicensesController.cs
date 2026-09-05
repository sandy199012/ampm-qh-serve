using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class LicensesController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public LicensesController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public static bool IsExpiringSoon(Dictionary<string,object?> l)
    {
        if (!DateTime.TryParse(l.GetValueOrDefault("renewalDate")?.ToString(), out var rd)) return false;
        int.TryParse(l.GetValueOrDefault("alertDays")?.ToString(), out var ad);
        int window = ad > 0 ? ad : 30;
        return (rd.Date - DateTime.Today).TotalDays <= window;
    }

    public IActionResult Index(string? status, string? search)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var licenses = _db.GetLicenses();

        if (!string.IsNullOrEmpty(status))
            licenses = licenses.Where(l => l.GetValueOrDefault("status")?.ToString() == status).ToList();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            licenses = licenses.Where(l =>
                l.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s) == true ||
                l.GetValueOrDefault("vendor")?.ToString()?.ToLower().Contains(s) == true ||
                l.GetValueOrDefault("category")?.ToString()?.ToLower().Contains(s) == true
            ).ToList();
        }

        licenses = licenses.OrderBy(l => l.GetValueOrDefault("renewalDate")?.ToString()).ToList();

        ViewBag.StatusFilter = status;
        ViewBag.Search = search;
        ViewBag.Total = licenses.Count;
        ViewBag.ExpiringSoon = licenses.Count(IsExpiringSoon);
        return View(licenses);
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
        double.TryParse(form["cost"].ToString(), out var cost);
        int.TryParse(form["seats"].ToString(), out var seats);
        int.TryParse(form["alertDays"].ToString(), out var alertDays);
        var lic = new Dictionary<string,object?>
        {
            ["id"]              = Guid.NewGuid().ToString("N")[..8],
            ["name"]            = form["name"].ToString(),
            ["vendor"]          = form["vendor"].ToString(),
            ["category"]        = form["category"].ToString(),
            ["seats"]           = seats,
            ["purchaseDate"]    = form["purchaseDate"].ToString(),
            ["renewalDate"]     = form["renewalDate"].ToString(),
            ["cost"]            = cost,
            ["status"]          = form["status"].ToString(),
            ["alertDays"]       = alertDays > 0 ? alertDays : 30,
            ["invoiceNo"]       = form["invoiceNo"].ToString(),
            ["licenseKey"]      = form["licenseKey"].ToString(),
            ["notes"]           = form["notes"].ToString(),
            ["lastRenewedDate"] = "",
            ["lastRenewedBy"]   = "",
        };
        var licenses = _db.GetLicenses();
        licenses.Add(lic);
        _db.SaveLicenses(licenses);
        TempData["Success"] = $"License '{lic["name"]}' added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var lic = _db.GetLicenses().FirstOrDefault(l => l.GetValueOrDefault("id")?.ToString() == id);
        if (lic == null) return NotFound();
        return View(lic);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var licenses = _db.GetLicenses();
        var lic = licenses.FirstOrDefault(l => l.GetValueOrDefault("id")?.ToString() == id);
        if (lic == null) return NotFound();

        double.TryParse(form["cost"].ToString(), out var cost);
        int.TryParse(form["seats"].ToString(), out var seats);
        int.TryParse(form["alertDays"].ToString(), out var alertDays);
        lic["name"]         = form["name"].ToString();
        lic["vendor"]       = form["vendor"].ToString();
        lic["category"]     = form["category"].ToString();
        lic["seats"]        = seats;
        lic["purchaseDate"] = form["purchaseDate"].ToString();
        lic["renewalDate"]  = form["renewalDate"].ToString();
        lic["cost"]         = cost;
        lic["status"]       = form["status"].ToString();
        lic["alertDays"]    = alertDays > 0 ? alertDays : 30;
        lic["invoiceNo"]    = form["invoiceNo"].ToString();
        lic["licenseKey"]   = form["licenseKey"].ToString();
        lic["notes"]        = form["notes"].ToString();

        _db.SaveLicenses(licenses);
        TempData["Success"] = "License updated!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult MarkRenewed(string id)
    {
        var licenses = _db.GetLicenses();
        var lic = licenses.FirstOrDefault(l => l.GetValueOrDefault("id")?.ToString() == id);
        if (lic == null) return NotFound();

        DateTime.TryParse(lic.GetValueOrDefault("renewalDate")?.ToString(), out var oldRenewal);
        var baseDate = oldRenewal > DateTime.Today ? oldRenewal : DateTime.Today;
        lic["lastRenewedDate"] = DateTime.Today.ToString("yyyy-MM-dd");
        lic["lastRenewedBy"]   = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";
        lic["renewalDate"]     = baseDate.AddYears(1).ToString("yyyy-MM-dd");
        lic["status"]          = "Active";

        _db.SaveLicenses(licenses);
        TempData["Success"] = $"'{lic["name"]}' renewed! Next renewal: {lic["renewalDate"]}";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var licenses = _db.GetLicenses();
        licenses.RemoveAll(l => l.GetValueOrDefault("id")?.ToString() == id);
        _db.SaveLicenses(licenses);
        TempData["Success"] = "License deleted.";
        return RedirectToAction("Index");
    }

    [HttpGet("/Licenses/Export")]
    public IActionResult Export()
    {
        var licenses = _db.GetLicenses().OrderBy(l => l.GetValueOrDefault("renewalDate")?.ToString()).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:11px}}table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:white;padding:6px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px;border:1px solid #CBD5E1;font-size:10px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
.green{{color:#059669;font-weight:bold}}.red{{color:#DC2626;font-weight:bold}}.amber{{color:#D97706;font-weight:bold}}
</style></head><body>
<table style='margin-bottom:12px'><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — SOFTWARE LICENSES REPORT</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<table><thead><tr><th>#</th><th>Name</th><th>Vendor</th><th>Category</th><th>Seats</th><th>Renewal Date</th><th>Cost</th><th>Status</th><th>Invoice No</th></tr></thead><tbody>");

        int sno = 0;
        foreach (var l in licenses)
        {
            sno++;
            DateTime.TryParse(l.GetValueOrDefault("renewalDate")?.ToString(), out var rd);
            bool expiring = IsExpiringSoon(l);
            string cls = rd < DateTime.Today ? "red" : expiring ? "amber" : "green";
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td><b>{l.GetValueOrDefault("name")}</b></td><td>{l.GetValueOrDefault("vendor")}</td><td>{l.GetValueOrDefault("category")}</td><td style='text-align:center'>{l.GetValueOrDefault("seats")}</td><td class='{cls}'>{l.GetValueOrDefault("renewalDate")}</td><td>₹{l.GetValueOrDefault("cost")}</td><td>{l.GetValueOrDefault("status")}</td><td>{l.GetValueOrDefault("invoiceNo")}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Licenses_{DateTime.Now:yyyyMMdd}.xls");
    }
}
