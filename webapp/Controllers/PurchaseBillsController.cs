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

    public IActionResult Index(string? poNumber)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var bills = GetBills();
        if (!string.IsNullOrEmpty(poNumber))
            bills = bills.Where(b => b.GetValueOrDefault("poNumber")?.ToString() == poNumber).ToList();
        bills = bills.OrderByDescending(b => b.GetValueOrDefault("billDate")?.ToString()).ToList();
        ViewBag.PoFilter = poNumber;
        ViewBag.Total = bills.Sum(b => { double.TryParse(b.GetValueOrDefault("totalAmount")?.ToString(), out var a); return a; });
        return View(bills);
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
        if (monthly[monthKey] is JObject mo)
        {
            double.TryParse(mo["actual"]?.ToString(), out curActual);
            double.TryParse(mo["projected"]?.ToString(), out curProjected);
        }
        monthly[monthKey] = new JObject { ["projected"] = curProjected, ["actual"] = curActual + delta };
        item["monthly"] = monthly;

        double totalActual = 0, totalProjected = 0;
        foreach (var prop in monthly.Properties())
        {
            var mo = prop.Value as JObject;
            double.TryParse(mo?["actual"]?.ToString(), out var a); totalActual += a;
            double.TryParse(mo?["projected"]?.ToString(), out var p); totalProjected += p;
        }
        item["TotalActual"]    = totalActual;
        item["TotalProjected"] = totalProjected;
        item["Variance"]       = totalProjected - totalActual;

        _db.SaveBudget(budget);
    }
}
