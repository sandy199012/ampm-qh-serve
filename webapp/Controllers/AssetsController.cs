using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class AssetsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public AssetsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? search, string? type)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var assets = _db.GetAssets();
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            assets = assets.Where(a =>
                a.GetValueOrDefault("assetTag")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("brand")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("model")?.ToString()?.ToLower().Contains(s)==true ||
                a.GetValueOrDefault("assignedToName")?.ToString()?.ToLower().Contains(s)==true
            ).ToList();
        }
        if (!string.IsNullOrEmpty(type))
            assets = assets.Where(a => a.GetValueOrDefault("assetType")?.ToString() == type).ToList();
        ViewBag.Search = search;
        ViewBag.TypeFilter = type;
        ViewBag.Types = _db.GetAssets().Select(a => a.GetValueOrDefault("assetType")?.ToString() ?? "").Distinct().OrderBy(t => t).ToList();
        return View(assets);
    }
}
