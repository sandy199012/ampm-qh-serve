using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

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
        ViewBag.TotalValue = pos.Where(p => p.GetValueOrDefault("status")?.ToString() != "Cancelled")
            .Sum(p => { double.TryParse(p.GetValueOrDefault("grandTotal")?.ToString(), out var g); return g; });
        return View(pos);
    }

    public IActionResult Details(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var po = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id });
        if (po == null) return NotFound();
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(po) ?? new());
    }

    [HttpPost]
    public IActionResult UpdateStatus(string id, string status)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM po_list WHERE po_number=@id", new { id });
        if (raw == null) return NotFound();
        var po = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        po["status"] = status;
        _db.Execute("UPDATE po_list SET data=@d, status=@s WHERE po_number=@id",
            new { d=JsonConvert.SerializeObject(po), s=status, id });
        return Json(new { ok=true });
    }
}
