using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class VendorsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public VendorsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? search)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var vendors = _db.GetVendors();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            vendors = vendors.Where(v =>
                v.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s)==true ||
                v.GetValueOrDefault("category")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        ViewBag.Search = search;
        return View(vendors);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View(new Dictionary<string,object?>());
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var vendor = new Dictionary<string,object?> { ["vendorId"] = id };
        foreach (var key in form.Keys) vendor[key] = form[key].ToString();
        _db.Execute("INSERT INTO vendors (vendor_id,name,data,ts) VALUES (@id,@name,@data,@ts)",
            new { id, name=form["name"].ToString(), data=JsonConvert.SerializeObject(vendor), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Vendor '{form["name"]}' added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM vendors WHERE vendor_id=@id", new { id });
        if (raw == null) return NotFound();
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new());
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM vendors WHERE vendor_id=@id", new { id });
        var vendor = raw != null ? JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new() : new();
        foreach (var key in form.Keys) vendor[key] = form[key].ToString();
        _db.Execute("UPDATE vendors SET name=@name, data=@data WHERE vendor_id=@id",
            new { name=form["name"].ToString(), data=JsonConvert.SerializeObject(vendor), id });
        TempData["Success"] = "Vendor updated!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        _db.Execute("DELETE FROM vendors WHERE vendor_id=@id", new { id });
        TempData["Success"] = "Vendor deleted.";
        return RedirectToAction("Index");
    }

    [HttpGet("/Vendors/Export")]
    public IActionResult Export()
    {
        var vendors = _db.GetVendors();
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial;font-size:11px}}table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:white;padding:6px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px;border:1px solid #CBD5E1;font-size:10px}}
.hdr{{background:#0A192F;color:white;font-size:14px;font-weight:bold;padding:10px}}
</style></head><body>
<table style='margin-bottom:12px'><tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — VENDOR MASTER</td></tr>
<tr><td style='padding:5px;font-size:10px'>Generated: {DateTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha</td></tr></table>
<table><thead><tr><th>#</th><th>Name</th><th>Category</th><th>GST</th><th>Contact Person</th><th>Phone</th><th>Email</th><th>Payment Terms</th><th>Address</th></tr></thead><tbody>");
        int sno = 0;
        foreach (var v in vendors)
        {
            sno++;
            sb.Append($"<tr><td style='text-align:center'>{sno}</td><td><b>{v.GetValueOrDefault("name")}</b></td><td>{v.GetValueOrDefault("category")}</td><td>{v.GetValueOrDefault("gst")}</td><td>{v.GetValueOrDefault("contactPerson")}</td><td>{v.GetValueOrDefault("phone")}</td><td>{v.GetValueOrDefault("email")}</td><td>{v.GetValueOrDefault("paymentTerms")}</td><td>{v.GetValueOrDefault("address")}</td></tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_Vendors_{DateTime.Now:yyyyMMdd}.xls");
    }
}
