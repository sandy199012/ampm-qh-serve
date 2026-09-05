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
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(po) ?? new());
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
}
