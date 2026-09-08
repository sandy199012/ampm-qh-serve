using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AMPMWeb.Controllers;

public class PurchaseBillsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public PurchaseBillsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    List<Dictionary<string,object?>> GetBills() => _db.KGetObj<List<Dictionary<string,object?>>>("purchase_bills") ?? new();
    void SaveBills(List<Dictionary<string,object?>> bills) => _db.KSet("purchase_bills", bills);

    static string MonthKey(DateTime d) => d.ToString("MMM") + (d.Year % 100).ToString("D2");

    public IActionResult Index(string? poNumber, string? status)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var allBills = GetBills();
        var bills = allBills;
        if (!string.IsNullOrEmpty(poNumber))
            bills = bills.Where(b => b.GetValueOrDefault("poNumber")?.ToString() == poNumber).ToList();
        if (!string.IsNullOrEmpty(status))
            bills = bills.Where(b => (b.GetValueOrDefault("status")?.ToString() ?? "Mapped") == status).ToList();
        bills = bills.OrderByDescending(b => b.GetValueOrDefault("billDate")?.ToString()).ToList();
        ViewBag.PoFilter = poNumber;
        ViewBag.StatusFilter = status;
        ViewBag.Total = bills.Sum(b => { double.TryParse(b.GetValueOrDefault("totalAmount")?.ToString(), out var a); return a; });
        ViewBag.UnmappedCount = allBills.Count(b => (b.GetValueOrDefault("status")?.ToString() ?? "Mapped") == "Unmapped");
        return View(bills);
    }

    // ── Bulk PDF upload — upload many bill scans at once, map each to a PO
    // and Budget line afterward via Edit. ────────────────────────────────
    [HttpGet]
    public IActionResult BulkUpload()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> BulkUpload(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            TempData["Error"] = "Please select at least one PDF file.";
            return RedirectToAction("BulkUpload");
        }

        var bills = GetBills();
        var createdBy = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";
        int uploaded = 0;

        foreach (var file in files)
        {
            if (file == null || file.Length == 0) continue;

            var billId = Guid.NewGuid().ToString("N")[..8];
            var bill = new Dictionary<string,object?>
            {
                ["id"]          = billId,
                ["poNumber"]    = "",
                ["vendorName"]  = "",
                ["billNo"]      = Path.GetFileNameWithoutExtension(file.FileName),
                ["fileName"]    = file.FileName,
                ["billDate"]    = DateTime.Today.ToString("yyyy-MM-dd"),
                ["amount"]      = 0.0,
                ["gstAmount"]   = 0.0,
                ["totalAmount"] = 0.0,
                ["budgetId"]    = "",
                ["monthKey"]    = "",
                ["notes"]       = "",
                ["status"]      = "Unmapped",
                ["createdBy"]   = createdBy,
                ["createdOn"]   = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var scanId = Guid.NewGuid().ToString("N")[..8];
            try
            {
                _db.Execute("INSERT INTO bill_scans (id,bill_id,file_name,file_data,content_type,uploaded_at,uploaded_by) VALUES (@id,@bid,@fn,@fd,@ct,@at,@by)",
                    new { id = scanId, bid = billId, fn = file.FileName, fd = base64, ct = file.ContentType, at = DateTime.Now.ToString("o"), by = createdBy });
                bills.Add(bill);
                uploaded++;
            }
            catch { }
        }

        SaveBills(bills);
        TempData["Success"] = uploaded > 0
            ? $"{uploaded} bill PDF(s) uploaded. Ab neeche list mein har bill ko PO aur Budget line se map kar dein."
            : "Upload failed — koi file save nahi hui.";
        return RedirectToAction("Index", new { status = "Unmapped" });
    }

    [HttpGet("/PurchaseBills/ViewScan/{id}")]
    public IActionResult ViewScan(string id)
    {
        var scan = _db.GetLatestScan("bill_scans", "bill_id", id);
        if (scan == null) return NotFound();
        var bytes = Convert.FromBase64String(scan.GetValueOrDefault("fileData")?.ToString() ?? "");
        var ct = scan.GetValueOrDefault("contentType")?.ToString();
        return File(bytes, string.IsNullOrEmpty(ct) ? "application/octet-stream" : ct);
    }

    // ── Map an (unmapped or already-mapped) bill to a PO / Budget line ───
    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var bill = GetBills().FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();
        ViewBag.AllPOs = _db.GetPOs();
        ViewBag.BudgetItems = _db.GetBudget();
        ViewBag.HasScan = _db.GetLatestScan("bill_scans", "bill_id", id) != null;
        return View(bill);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var bills = GetBills();
        var bill = bills.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();

        // Reverse whatever this bill previously posted to the budget before applying the new mapping
        var oldBudgetId = bill.GetValueOrDefault("budgetId")?.ToString();
        var oldMonthKey = bill.GetValueOrDefault("monthKey")?.ToString();
        double.TryParse(bill.GetValueOrDefault("totalAmount")?.ToString(), out var oldTotal);
        if (!string.IsNullOrEmpty(oldBudgetId) && !string.IsNullOrEmpty(oldMonthKey))
            PostToBudget(oldBudgetId, oldMonthKey, -oldTotal);

        double.TryParse(form["amount"].ToString(), out var amount);
        double.TryParse(form["gstAmount"].ToString(), out var gstAmount);
        DateTime.TryParse(form["billDate"].ToString(), out var billDate);
        var totalAmount = amount + gstAmount;
        var poNumber = form["poNumber"].ToString();
        var budgetId = form["budgetId"].ToString();
        var monthKey = billDate != default ? MonthKey(billDate) : "";

        bill["poNumber"]    = poNumber;
        bill["vendorName"]  = form["vendorName"].ToString();
        bill["billNo"]      = form["billNo"].ToString();
        bill["billDate"]    = form["billDate"].ToString();
        bill["amount"]      = amount;
        bill["gstAmount"]   = gstAmount;
        bill["totalAmount"] = totalAmount;
        bill["budgetId"]    = budgetId;
        bill["monthKey"]    = monthKey;
        bill["notes"]       = form["notes"].ToString();
        bill["status"]      = (!string.IsNullOrEmpty(poNumber) || !string.IsNullOrEmpty(budgetId)) ? "Mapped" : "Unmapped";
        bill["mappedBy"]    = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy";
        bill["mappedOn"]    = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        SaveBills(bills);

        if (!string.IsNullOrEmpty(budgetId) && !string.IsNullOrEmpty(monthKey))
            PostToBudget(budgetId, monthKey, totalAmount);

        TempData["Success"] = "Bill mapped and updated.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Create(string? poNumber)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        Dictionary<string,object?>? po = null;
        if (!string.IsNullOrEmpty(poNumber))
        {
            var raw = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id = Uri.UnescapeDataString(poNumber) });
            if (raw != null) po = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw);
        }
        ViewBag.PO = po;
        ViewBag.AllPOs = _db.GetPOs();
        ViewBag.BudgetItems = _db.GetBudget();
        return View();
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        double.TryParse(form["amount"].ToString(), out var amount);
        double.TryParse(form["gstAmount"].ToString(), out var gstAmount);
        DateTime.TryParse(form["billDate"].ToString(), out var billDate);
        var totalAmount = amount + gstAmount;
        var budgetId = form["budgetId"].ToString();
        var monthKey = billDate != default ? MonthKey(billDate) : "";

        var bill = new Dictionary<string,object?>
        {
            ["id"]          = Guid.NewGuid().ToString("N")[..8],
            ["poNumber"]    = form["poNumber"].ToString(),
            ["vendorName"]  = form["vendorName"].ToString(),
            ["billNo"]      = form["billNo"].ToString(),
            ["billDate"]    = form["billDate"].ToString(),
            ["amount"]      = amount,
            ["gstAmount"]   = gstAmount,
            ["totalAmount"] = totalAmount,
            ["budgetId"]    = budgetId,
            ["monthKey"]    = monthKey,
            ["notes"]       = form["notes"].ToString(),
            ["status"]      = "Mapped",
            ["createdBy"]   = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy",
            ["createdOn"]   = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };

        var bills = GetBills();
        bills.Add(bill);
        SaveBills(bills);

        if (!string.IsNullOrEmpty(budgetId) && !string.IsNullOrEmpty(monthKey))
            PostToBudget(budgetId, monthKey, totalAmount);

        TempData["Success"] = $"Purchase bill recorded (₹{totalAmount:N2}).";
        return RedirectToAction("Index", new { poNumber = bill["poNumber"] });
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var bills = GetBills();
        var bill = bills.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == id);
        if (bill == null) return NotFound();

        var budgetId = bill.GetValueOrDefault("budgetId")?.ToString();
        var monthKey = bill.GetValueOrDefault("monthKey")?.ToString();
        double.TryParse(bill.GetValueOrDefault("totalAmount")?.ToString(), out var totalAmount);
        if (!string.IsNullOrEmpty(budgetId) && !string.IsNullOrEmpty(monthKey))
            PostToBudget(budgetId, monthKey, -totalAmount);

        bills.RemoveAll(b => b.GetValueOrDefault("id")?.ToString() == id);
        SaveBills(bills);
        TempData["Success"] = "Purchase bill deleted (budget actual reversed).";
        return RedirectToAction("Index");
    }

    void PostToBudget(string budgetId, string monthKey, double delta)
    {
        var budget = _db.GetBudget();
        var item = budget.FirstOrDefault(b => b.GetValueOrDefault("id")?.ToString() == budgetId);
        if (item == null) return;

        var monthlyRaw = item.GetValueOrDefault("monthly");
        JObject monthly = monthlyRaw as JObject ?? (monthlyRaw != null ? JObject.FromObject(monthlyRaw) : new JObject());

        double curActual = 0, curProjected = 0;
        if (monthly[monthKey] is JObject curMo)
        {
            double.TryParse(curMo["actual"]?.ToString(), out curActual);
            double.TryParse(curMo["projected"]?.ToString(), out curProjected);
        }
        monthly[monthKey] = new JObject { ["projected"] = curProjected, ["actual"] = curActual + delta };
        item["monthly"] = monthly;

        double totalActual = 0, totalProjected = 0;
        foreach (var prop in monthly.Properties())
        {
            var moItem = prop.Value as JObject;
            double.TryParse(moItem?["actual"]?.ToString(), out var a); totalActual += a;
            double.TryParse(moItem?["projected"]?.ToString(), out var p); totalProjected += p;
        }
        item["TotalActual"]    = totalActual;
        item["TotalProjected"] = totalProjected;
        item["Variance"]       = totalProjected - totalActual;

        _db.SaveBudget(budget);
    }
}
