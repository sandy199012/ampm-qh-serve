using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

// Self-service employee portal for the web. Any logged-in account lands
// here to raise their own tickets and see only their own ticket history
// and status — never other employees' tickets, and never the admin
// dashboard. Scoping is by the account's linked EmpId (set on the user's
// login in User Management → "Select Employee"), the same field the
// mobile app already uses for the exact same purpose (see ApiController).
// Exempted from the module-permission check (see ModulePermissionFilter)
// so every logged-in user can reach it regardless of their per-module
// permissions — this page never shows anyone else's data.
public class MyHelpdeskController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public MyHelpdeskController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    public IActionResult Index()
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login", "Account");
        var user = _auth.GetCurrentUser(HttpContext);
        if (user == null) return RedirectToAction("Login", "Account");
        ViewBag.User = user;

        var empId = user.EmpId ?? "";
        var tickets = string.IsNullOrWhiteSpace(empId)
            ? new List<Dictionary<string, object?>>()
            : _db.GetTickets()
                 .Where(t => (t.GetValueOrDefault("empId")?.ToString() ?? "") == empId)
                 .OrderByDescending(t => t.GetValueOrDefault("ticketId")?.ToString())
                 .ToList();

        ViewBag.NoEmployeeLinked = string.IsNullOrWhiteSpace(empId);
        return View(tickets);
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var user = _auth.GetCurrentUser(HttpContext);
        if (user == null) return RedirectToAction("Login", "Account");

        var empId = user.EmpId ?? "";
        var emp = string.IsNullOrWhiteSpace(empId) ? null : _db.GetEmployeeByCode(empId);
        if (emp == null)
        {
            TempData["Error"] = "Your login isn't linked to an Employee record yet — contact IT so they can raise this ticket for you.";
            return RedirectToAction("Index");
        }

        var ticket = new Dictionary<string, object?>
        {
            ["ticketId"] = "TKT-" + IstTime.Now.ToString("yyyyMMddHHmmss"),
            ["title"] = form["title"].ToString(),
            ["description"] = form["description"].ToString(),
            ["empId"] = empId,
            ["empName"] = emp.GetValueOrDefault("name")?.ToString() ?? user.Name,
            ["empDept"] = emp.GetValueOrDefault("dept")?.ToString() ?? user.Department,
            ["empDesig"] = emp.GetValueOrDefault("designation")?.ToString() ?? "",
            ["empHod"] = emp.GetValueOrDefault("manager")?.ToString() ?? "",
            ["empEmail"] = emp.GetValueOrDefault("email")?.ToString() ?? "",
            ["empMobile"] = emp.GetValueOrDefault("mobile")?.ToString() ?? "",
            ["priority"] = form["priority"].ToString(),
            ["issueType"] = form["issueType"].ToString(),
            ["category"] = form["category"].ToString(),
            ["assignedTo"] = "",
            ["status"] = "Open",
            ["dateRaised"] = IstTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["raisedBy"] = user.Name
        };
        _db.SaveTicket(ticket);
        TempData["Success"] = "Ticket raised: " + ticket["ticketId"];
        return RedirectToAction("Index");
    }
}
