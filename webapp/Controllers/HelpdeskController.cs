using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class HelpdeskController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public HelpdeskController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(string? status, string? priority)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var tickets = _db.GetTickets(status);
        if (!string.IsNullOrEmpty(priority))
            tickets = tickets.Where(t => t.GetValueOrDefault("priority")?.ToString() == priority).ToList();
        ViewBag.Status = status;
        ViewBag.Priority = priority;
        return View(tickets);
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
        var ticket = new Dictionary<string,object?>
        {
            ["ticketId"]    = "TKT-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
            ["title"]       = form["title"].ToString(),
            ["description"] = form["description"].ToString(),
            ["empName"]     = form["empName"].ToString(),
            ["empId"]       = form["empId"].ToString(),
            ["empDept"]     = form["empDept"].ToString(),
            ["empEmail"]    = form["empEmail"].ToString(),
            ["empMobile"]   = form["empMobile"].ToString(),
            ["priority"]    = form["priority"].ToString(),
            ["issueType"]   = form["issueType"].ToString(),
            ["category"]    = form["category"].ToString(),
            ["assignedTo"]  = form["assignedTo"].ToString(),
            ["status"]      = "Open",
            ["dateRaised"]  = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["raisedBy"]    = HttpContext.Request.Cookies["ampm_name"] ?? "IT Admin"
        };
        _db.SaveTicket(ticket);
        TempData["Success"] = "Ticket created: " + ticket["ticketId"];
        return RedirectToAction("Index");
    }

    public IActionResult Details(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new());
    }

    [HttpPost]
    public IActionResult UpdateStatus(string id, string status, string? resolution)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        ticket["status"] = status;
        if (!string.IsNullOrEmpty(resolution)) ticket["resolution"] = resolution;
        if (status == "Resolved" || status == "Closed")
        {
            ticket["dateResolved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (DateTime.TryParse(ticket.GetValueOrDefault("dateRaised")?.ToString(), out var dr))
                ticket["resolutionHrs"] = Math.Round((DateTime.Now - dr).TotalHours, 2);
        }
        _db.SaveTicket(ticket);
        return Json(new { ok = true });
    }

    [HttpGet("/api/tickets")]
    public IActionResult ApiList() => Json(_db.GetTickets());
}

// ── Export / Report ──────────────────────────────────────────
[HttpGet("/Helpdesk/Export")]
public IActionResult Export()
{
    var tickets = _db.GetTickets();
    var csv = new System.Text.StringBuilder();
    csv.AppendLine("Ticket ID,Date Raised,Employee,Department,Mobile,Title,Issue Type,Priority,Assigned To,Status,Date Resolved,Resolution Hours");
    foreach (var t in tickets)
    {
        csv.AppendLine(string.Join(",",
            CsvEsc(t.GetValueOrDefault("ticketId")?.ToString()),
            CsvEsc(t.GetValueOrDefault("dateRaised")?.ToString()),
            CsvEsc(t.GetValueOrDefault("empName")?.ToString()),
            CsvEsc(t.GetValueOrDefault("empDept")?.ToString()),
            CsvEsc(t.GetValueOrDefault("empMobile")?.ToString()),
            CsvEsc(t.GetValueOrDefault("title")?.ToString()),
            CsvEsc(t.GetValueOrDefault("issueType")?.ToString()),
            CsvEsc(t.GetValueOrDefault("priority")?.ToString()),
            CsvEsc(t.GetValueOrDefault("assignedTo")?.ToString()),
            CsvEsc(t.GetValueOrDefault("status")?.ToString()),
            CsvEsc(t.GetValueOrDefault("dateResolved")?.ToString()),
            CsvEsc(t.GetValueOrDefault("resolutionHrs")?.ToString())
        ));
    }
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
    return File(bytes, "text/csv", $"Helpdesk_Report_{DateTime.Now:yyyyMMdd}.csv");
}

static string CsvEsc(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
