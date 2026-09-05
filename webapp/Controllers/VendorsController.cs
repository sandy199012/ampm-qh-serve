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
}
