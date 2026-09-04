using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class ITStoreController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ITStoreController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? type)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var items = new List<Dictionary<string,object?>>();
        var issues = new List<Dictionary<string,object?>>();
        try {
            var sql = string.IsNullOrEmpty(type) ? "SELECT data FROM it_stock_items ORDER BY item_type, name" : "SELECT data FROM it_stock_items WHERE item_type=@t ORDER BY name";
            items = _db.Query<string>(sql, new { t=type }).Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        } catch {}
        try {
            var sql2 = string.IsNullOrEmpty(type) ? "SELECT data FROM stock_items ORDER BY item_type, name" : "SELECT data FROM stock_items WHERE item_type=@t ORDER BY name";
            var items2 = _db.Query<string>(sql2, new { t=type }).Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
            items.AddRange(items2.Where(i2 => !items.Any(i1 => i1.GetValueOrDefault("name")?.ToString() == i2.GetValueOrDefault("name")?.ToString())));
        } catch {}
        try { issues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY rowid DESC").Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList(); } catch {}
        try {
            var i2 = _db.Query<string>("SELECT data FROM stock_issues ORDER BY ts DESC").Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
            issues.AddRange(i2.Where(x => !issues.Any(y => y.GetValueOrDefault("issueNo")?.ToString() == x.GetValueOrDefault("issueNo")?.ToString())));
        } catch {}
        if (!string.IsNullOrEmpty(type)) issues = issues.Where(i => i.GetValueOrDefault("itemType")?.ToString() == type).ToList();
        ViewBag.Issues = issues;
        ViewBag.TypeFilter = type;
        return View(items);
    }
}
