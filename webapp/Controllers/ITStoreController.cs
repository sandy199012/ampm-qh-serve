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
            issues = _db.Query<string>("SELECT data FROM it_stock_issues ORDER BY rowid DESC LIMIT 50")
                .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        } catch {}
        if (!string.IsNullOrEmpty(type))
            issues = issues.Where(i => i.GetValueOrDefault("itemType")?.ToString() == type).ToList();
        ViewBag.Issues = issues;
        ViewBag.TypeFilter = type;
        ViewBag.TotalIssued = issues.Sum(i => { int.TryParse(i.GetValueOrDefault("qty")?.ToString(), out var q); return q; });
        return View(items);
    }

    [HttpGet]
    public IActionResult AddItem()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }

    [HttpPost]
    public IActionResult AddItem(IFormCollection form)
    {
        var item = new Dictionary<string,object?>
        {
            ["id"]           = Guid.NewGuid().ToString("N")[..8],
            ["itemType"]     = form["itemType"].ToString(),
            ["name"]         = form["name"].ToString(),
            ["brand"]        = form["brand"].ToString(),
            ["model"]        = form["model"].ToString(),
            ["specs"]        = form["specs"].ToString(),
            ["totalQty"]     = int.TryParse(form["totalQty"].ToString(), out var q) ? q : 0,
            ["issuedQty"]    = 0,
            ["vendor"]       = form["vendor"].ToString(),
            ["unitCost"]     = double.TryParse(form["unitCost"].ToString(), out var c) ? c : 0,
            ["location"]     = "IT Store",
        };
        _db.Execute("INSERT INTO it_stock_items (id,item_type,data,ts) VALUES (@id,@type,@data,@ts)",
            new { id=item["id"], type=item["itemType"], data=JsonConvert.SerializeObject(item), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Item '{item["name"]}' added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult StockIn(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        item["id"] = id;
        return View(item);
    }

    [HttpPost]
    public IActionResult StockIn(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        int.TryParse(item.GetValueOrDefault("totalQty")?.ToString(), out var cur);
        int.TryParse(form["qty"].ToString(), out var add);
        item["totalQty"] = cur + add;
        if (!string.IsNullOrEmpty(form["vendor"].ToString())) item["vendor"] = form["vendor"].ToString();
        _db.Execute("UPDATE it_stock_items SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(item), id });
        TempData["Success"] = $"Stock added! New balance: {(cur+add) - (int.TryParse(item.GetValueOrDefault("issuedQty")?.ToString(), out var iq) ? iq : 0)}";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Issue(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        item["id"] = id;
        return View(item);
    }

    [HttpPost]
    public IActionResult Issue(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM it_stock_items WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var item = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        int.TryParse(item.GetValueOrDefault("issuedQty")?.ToString(), out var cur);
        int.TryParse(form["qty"].ToString(), out var qty);
        item["issuedQty"] = cur + qty;
        _db.Execute("UPDATE it_stock_items SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(item), id });

        // Save issue record
        var issueNo = $"ISS-{DateTime.Now:yyyyMMddHHmmss}";
        var issue = new Dictionary<string,object?>
        {
            ["issueNo"]   = issueNo,
            ["itemId"]    = id,
            ["itemName"]  = item.GetValueOrDefault("name"),
            ["itemType"]  = item.GetValueOrDefault("itemType"),
            ["qty"]       = qty,
            ["dept"]      = form["dept"].ToString(),
            ["empName"]   = form["empName"].ToString(),
            ["purpose"]   = form["purpose"].ToString(),
            ["issueDate"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["issuedBy"]  = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy",
        };
        _db.Execute("INSERT INTO it_stock_issues (id,issue_no,emp_id,emp_name,dept,item_id,status,data,ts) VALUES (@id2,@ino,'','',@dept,@iid,'Issued',@data,@ts)",
            new { id2=Guid.NewGuid().ToString("N")[..8], ino=issueNo, dept=form["dept"].ToString(), iid=id, data=JsonConvert.SerializeObject(issue), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = $"Issued {qty} items! Issue No: {issueNo}";
        return RedirectToAction("Index");
    }
}
