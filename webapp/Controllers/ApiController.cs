using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;
using System.Text;

namespace AMPMWeb.Controllers;

// Mobile app entry point (Flutter AMPMHelpdeskApp). No cookie/session — every
// call carries the login's username+password via HTTP Basic Auth, verified
// fresh against the users table (bcrypt) on every request. Exempted from
// ModulePermissionFilter (see Filters/ModulePermissionFilter.cs) since that
// filter only understands the cookie-based web login.
[Route("api")]
public class ApiController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ApiController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    UserSession? AuthenticateRequest()
    {
        var header = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6).Trim()));
            var idx = decoded.IndexOf(':');
            if (idx < 0) return null;
            var username = decoded.Substring(0, idx);
            var password = decoded.Substring(idx + 1);
            return _auth.Login(username, password);
        }
        catch { return null; }
    }

    static async Task<Dictionary<string, object?>> ReadBody(HttpRequest req)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body)) return new();
        try { return JsonConvert.DeserializeObject<Dictionary<string, object?>>(body) ?? new(); }
        catch { return new(); }
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Json(new { ok = true, service = "AMPM_WEB" });

    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        var data = await ReadBody(Request);
        var username = data.GetValueOrDefault("username")?.ToString() ?? Request.Query["username"].ToString();
        var password = data.GetValueOrDefault("password")?.ToString() ?? Request.Query["password"].ToString();
        var user = _auth.Login(username ?? "", password ?? "");
        if (user == null) return Json(new { ok = false, error = "Username ya password galat hai." });

        string desig = "";
        if (!string.IsNullOrWhiteSpace(user.EmpId))
        {
            var emp = _db.GetEmployeeByCode(user.EmpId);
            desig = emp?.GetValueOrDefault("designation")?.ToString() ?? "";
        }

        return Json(new
        {
            ok = true,
            username = user.Username,
            name = user.Name,
            dept = user.Department,
            desig,
            empId = user.EmpId ?? "",
            isAdmin = user.IsAdmin,
        });
    }

    [HttpGet("tickets")]
    public IActionResult GetTickets()
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        var tickets = _db.GetTickets();
        if (!user.IsAdmin)
        {
            var empId = user.EmpId ?? "";
            tickets = tickets.Where(t => t.GetValueOrDefault("empId")?.ToString() == empId).ToList();
        }
        return Json(tickets);
    }

    [HttpGet("tickets/{id}")]
    public IActionResult GetTicket(string id)
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var t = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        if (!user.IsAdmin && t.GetValueOrDefault("empId")?.ToString() != (user.EmpId ?? ""))
            return Unauthorized();
        return Json(t);
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> RaiseTicket()
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        var data = await ReadBody(Request);

        var empId = !string.IsNullOrWhiteSpace(user.EmpId) ? user.EmpId! : (data.GetValueOrDefault("empId")?.ToString() ?? "");
        var emp = string.IsNullOrWhiteSpace(empId) ? null : _db.GetEmployeeByCode(empId);

        var ticket = new Dictionary<string, object?>
        {
            ["ticketId"] = "TKT-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
            ["title"] = data.GetValueOrDefault("title")?.ToString() ?? "",
            ["description"] = data.GetValueOrDefault("description")?.ToString() ?? "",
            ["empId"] = empId,
            ["empName"] = emp?.GetValueOrDefault("name")?.ToString() ?? user.Name,
            ["empDept"] = emp?.GetValueOrDefault("dept")?.ToString() ?? user.Department,
            ["empDesig"] = emp?.GetValueOrDefault("designation")?.ToString() ?? "",
            ["empHod"] = emp?.GetValueOrDefault("manager")?.ToString() ?? data.GetValueOrDefault("empHod")?.ToString() ?? "",
            ["empEmail"] = emp?.GetValueOrDefault("email")?.ToString() ?? "",
            ["empMobile"] = emp?.GetValueOrDefault("mobile")?.ToString() ?? data.GetValueOrDefault("empMobile")?.ToString() ?? "",
            ["priority"] = data.GetValueOrDefault("priority")?.ToString() ?? "Medium",
            ["issueType"] = data.GetValueOrDefault("issueType")?.ToString() ?? "",
            ["category"] = data.GetValueOrDefault("category")?.ToString() ?? "",
            ["assignedTo"] = "",
            ["status"] = "Open",
            ["dateRaised"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["raisedBy"] = "Mobile App (" + user.Username + ")",
        };
        _db.SaveTicket(ticket);
        return Json(ticket);
    }

    [HttpPost("tickets/{id}/update")]
    public async Task<IActionResult> UpdateTicket(string id)
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        // Only admin/superadmin (or someone with Helpdesk approve rights) can update
        // ticket status from the mobile app — regular employees can only raise/view.
        if (!user.IsAdmin && !user.CanApprove("Helpdesk")) return Unauthorized();

        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        var data = await ReadBody(Request);

        if (data.TryGetValue("status", out var st) && !string.IsNullOrWhiteSpace(st?.ToString())) ticket["status"] = st!.ToString();
        if (data.TryGetValue("assignedTo", out var at) && at != null) ticket["assignedTo"] = at.ToString();
        if (data.TryGetValue("resolution", out var res) && !string.IsNullOrWhiteSpace(res?.ToString())) ticket["resolution"] = res!.ToString();
        if (data.TryGetValue("remarks", out var rm) && rm != null) ticket["remarks"] = rm.ToString();

        var status = ticket.GetValueOrDefault("status")?.ToString() ?? "";
        if (status == "In Progress" && string.IsNullOrEmpty(ticket.GetValueOrDefault("dateAcknowledged")?.ToString()))
            ticket["dateAcknowledged"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (status == "Resolved" || status == "Closed")
        {
            ticket["dateResolved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (DateTime.TryParse(ticket.GetValueOrDefault("dateRaised")?.ToString(), out var dr))
                ticket["resolutionHrs"] = Math.Round((DateTime.Now - dr).TotalHours, 2);
        }
        if (status == "Closed") ticket["dateClosed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        _db.SaveTicket(ticket);
        return Json(new { ok = true });
    }

    [HttpDelete("tickets/{id}")]
    public IActionResult DeleteTicket(string id)
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        if (!user.IsAdmin) return Unauthorized();
        _db.Execute("DELETE FROM tickets WHERE ticket_id=@id", new { id });
        return Json(new { ok = true });
    }

    // Admin-only employee directory — mapped to the field names the mobile
    // app already expects (empId/name/dept/designation/mobile/email).
    [HttpGet("employees/full")]
    public IActionResult GetEmployeesFull()
    {
        var user = AuthenticateRequest();
        if (user == null) return Unauthorized();
        if (!user.IsAdmin) return Unauthorized();
        var emps = _db.GetEmployees().Select(e => new Dictionary<string, object?>
        {
            ["empId"] = e.GetValueOrDefault("emp")?.ToString() ?? "",
            ["name"] = e.GetValueOrDefault("name")?.ToString() ?? "",
            ["dept"] = e.GetValueOrDefault("dept")?.ToString() ?? "",
            ["designation"] = e.GetValueOrDefault("designation")?.ToString() ?? "",
            ["mobile"] = e.GetValueOrDefault("mobile")?.ToString() ?? "",
            ["email"] = e.GetValueOrDefault("email")?.ToString() ?? "",
        }).ToList();
        return Json(emps);
    }
}
