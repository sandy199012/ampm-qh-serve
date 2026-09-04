using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

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
            vendors = vendors.Where(v => v.GetValueOrDefault("name")?.ToString()?.ToLower().Contains(s)==true).ToList();
        }
        ViewBag.Search = search;
        return View(vendors);
    }
}
