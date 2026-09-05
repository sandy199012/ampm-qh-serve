using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class HelpdeskController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    private readonly EmailService _email;
    public HelpdeskController(DbService db, AuthService auth, EmailService email) { _db=db; _auth=auth; _email=email; }

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

    [HttpGet] public IActionResult Create() { ViewBag.User = _auth.GetCurrentUser(HttpContext); return View(); }

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
            ["empDesig"]    = form["empDesig"].ToString(),
            ["empHod"]      = form["empHod"].ToString(),
            ["empEmail"]    = form["empEmail"].ToString(),
            ["empMobile"]   = form["empMobile"].ToString(),
            ["priority"]    = form["priority"].ToString(),
            ["issueType"]   = form["issueType"].ToString(),
            ["category"]    = form["category"].ToString(),
            ["assignedTo"]  = form["assignedTo"].ToString(),
            ["status"]      = "Open",
            ["dateRaised"]  = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["raisedBy"]    = HttpContext.Request.Cookies["ampm_name"] ?? "Sandy"
        };
        _db.SaveTicket(ticket);
        TempData["Success"] = "Ticket created: " + ticket["ticketId"];
        var es = GetEmailSettingsObj();
        var sendOnRaise = es.GetValueOrDefault("sendOnRaise")?.ToString()?.ToLower() != "false";
        return RedirectToAction("Details", new { id = ticket["ticketId"], openEmail = sendOnRaise ? "1" : null });
    }

    public IActionResult Details(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var es = GetEmailSettingsObj();
        ViewBag.ItEmail = es.GetValueOrDefault("itEmail")?.ToString() ?? "itsupport@ampm.in";
        ViewBag.CcEmails = es.GetValueOrDefault("ccEmails")?.ToString() ?? "";
        ViewBag.SendOnClose = es.GetValueOrDefault("sendOnClose")?.ToString()?.ToLower() != "false";
        ViewBag.EmailConfigured = _email.IsConfigured;
        return View(JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new());
    }

    [HttpGet]
    public IActionResult EmailPreview(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        return Content(BuildTicketHtml(ticket), "text/html");
    }

    [HttpPost]
    public async Task<IActionResult> SendTicketEmail(string id, string? to)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return Json(new { ok = false, error = "Ticket not found." });
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();

        var toEmail = !string.IsNullOrWhiteSpace(to) ? to : ticket.GetValueOrDefault("empEmail")?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(to) && to != ticket.GetValueOrDefault("empEmail")?.ToString())
        {
            ticket["empEmail"] = to;
            _db.SaveTicket(ticket);
        }

        var es = GetEmailSettingsObj();
        var itEmail = es.GetValueOrDefault("itEmail")?.ToString() ?? "itsupport@ampm.in";
        var ccEmails = es.GetValueOrDefault("ccEmails")?.ToString() ?? "";
        var cc = string.Join(",", new[] { itEmail, ccEmails }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var subject = $"[{ticket.GetValueOrDefault("ticketId")}] [{ticket.GetValueOrDefault("priority")}] {ticket.GetValueOrDefault("title")}";
        var html = BuildTicketHtml(ticket);

        var (ok, error) = await _email.SendAsync(toEmail, cc, subject, html);
        return Json(new { ok, error });
    }

    static string BuildTicketHtml(Dictionary<string,object?> t)
    {
        string S(string k) => System.Net.WebUtility.HtmlEncode(t.GetValueOrDefault(k)?.ToString() ?? "");
        string SOr(string k, string fallback) { var v = t.GetValueOrDefault(k)?.ToString(); return string.IsNullOrWhiteSpace(v) ? fallback : System.Net.WebUtility.HtmlEncode(v); }
        var status = t.GetValueOrDefault("status")?.ToString() ?? "Open";
        var priority = t.GetValueOrDefault("priority")?.ToString() ?? "Medium";
        string priBg = priority switch { "Critical" => "#FEE2E2", "High" => "#FFEDD5", "Medium" => "#FEF3C7", _ => "#DCFCE7" };
        string priColor = priority switch { "Critical" => "#DC2626", "High" => "#EA580C", "Medium" => "#D97706", _ => "#16A34A" };
        string stBg = status switch { "Open" => "#FEE2E2", "In Progress" => "#DBEAFE", "Resolved" => "#DCFCE7", _ => "#F3F4F6" };
        string stColor = status switch { "Open" => "#DC2626", "In Progress" => "#2563EB", "Resolved" => "#16A34A", _ => "#4B5563" };

        string resolutionBlock = !string.IsNullOrWhiteSpace(t.GetValueOrDefault("resolution")?.ToString()) ? $@"
        <div style='font-size:11px;font-weight:700;color:#6B7280;letter-spacing:1px;margin-bottom:6px'>&#9989; RESOLUTION / FIX APPLIED</div>
        <div style='border-top:1px solid #E5E7EB;margin-bottom:12px'></div>
        <div style='border-left:3px solid #16A34A;padding:10px 14px;background:#F0FDF4;font-size:13px;color:#1F2937;white-space:pre-wrap;margin-bottom:16px'>{S("resolution")}</div>" : "";

        return $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:0 auto;padding:24px;background:#ffffff'>
  <table width='100%' cellpadding='0' cellspacing='0'><tr>
    <td style='vertical-align:top'>
      <div style='font-size:20px;font-weight:700;color:#4B5563'>AMPM Fashions Pvt Ltd</div>
      <div style='font-size:12px;color:#0EA5E9;letter-spacing:1px;margin-top:2px'>IT HELPDESK &mdash; SUPPORT TICKET</div>
    </td>
    <td style='text-align:right;vertical-align:top'>
      <div style='font-size:11px;color:#9CA3AF;letter-spacing:1px'>TICKET ID</div>
      <div style='font-size:15px;font-weight:700;color:#111827'>{S("ticketId")}</div>
    </td>
  </tr></table>
  <div style='border-top:1px solid #E5E7EB;margin:14px 0'></div>
  <table width='100%' cellpadding='0' cellspacing='0'><tr>
    <td>
      <span style='display:inline-block;background:{stBg};color:{stColor};font-size:11px;font-weight:700;padding:4px 10px;border-radius:3px;letter-spacing:.5px'>{S("status").ToUpper()}</span>
      &nbsp;
      <span style='display:inline-block;background:{priBg};color:{priColor};font-size:11px;font-weight:700;padding:4px 10px;border-radius:3px;letter-spacing:.5px'>{S("priority").ToUpper()} PRIORITY</span>
    </td>
    <td style='text-align:right;font-size:12px;color:#6B7280'>{DateTime.Now:dd MMM yyyy, hh:mm tt}</td>
  </tr></table>
  <div style='font-size:19px;font-weight:700;color:#111827;margin:18px 0 14px 0'>{S("title")}</div>

  <div style='font-size:11px;font-weight:700;color:#6B7280;letter-spacing:1px;margin-bottom:6px'>&#128100; EMPLOYEE DETAILS</div>
  <div style='border-top:1px solid #E5E7EB;margin-bottom:12px'></div>
  <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:16px'>
    <tr>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>NAME</div><div style='font-size:13px;color:#111827;font-weight:600;margin-top:2px'>{S("empName")}</div></td>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>EMPLOYEE ID</div><div style='font-size:13px;color:#111827;font-weight:600;margin-top:2px'>{S("empId")}</div></td>
    </tr>
    <tr>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>DEPARTMENT</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("empDept")}</div></td>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>HOD / MANAGER</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("empHod")}</div></td>
    </tr>
    <tr>
      <td width='50%' style='vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>MOBILE</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("empMobile")}</div></td>
      <td width='50%' style='vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>EMAIL</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("empEmail")}</div></td>
    </tr>
  </table>

  <div style='font-size:11px;font-weight:700;color:#6B7280;letter-spacing:1px;margin-bottom:6px'>&#127991; TICKET DETAILS</div>
  <div style='border-top:1px solid #E5E7EB;margin-bottom:12px'></div>
  <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:16px'>
    <tr>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>ISSUE TYPE</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("issueType")}</div></td>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>CATEGORY</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("category")}</div></td>
    </tr>
    <tr>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>ASSIGNED TO</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("assignedTo")}</div></td>
      <td width='50%' style='padding-bottom:12px;vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>DATE RAISED</div><div style='font-size:13px;color:#111827;margin-top:2px'>{S("dateRaised")}</div></td>
    </tr>
    <tr>
      <td width='50%' style='vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>ACKNOWLEDGED</div><div style='font-size:13px;color:#111827;margin-top:2px'>{SOr("dateAcknowledged","&mdash;")}</div></td>
      <td width='50%' style='vertical-align:top'><div style='font-size:10px;color:#9CA3AF;letter-spacing:.5px'>RESOLVED ON</div><div style='font-size:13px;color:#111827;margin-top:2px'>{SOr("dateResolved","&mdash;")}</div></td>
    </tr>
  </table>

  <div style='font-size:11px;font-weight:700;color:#6B7280;letter-spacing:1px;margin-bottom:6px'>&#128203; ISSUE DESCRIPTION</div>
  <div style='border-top:1px solid #E5E7EB;margin-bottom:12px'></div>
  <div style='border-left:3px solid #2563EB;padding:10px 14px;background:#F8FAFC;font-size:13px;color:#1F2937;white-space:pre-wrap;margin-bottom:16px'>{S("description")}</div>
  {resolutionBlock}

  <div style='border-top:1px solid #E5E7EB;margin:20px 0 14px 0'></div>
  <div style='font-size:13px;font-weight:700;color:#111827'>Sandeep Kumar Singh Kushwaha</div>
  <div style='font-size:12px;color:#2563EB;margin-top:2px'>IT &mdash; System Administrator</div>
  <div style='font-size:11px;color:#6B7280;margin-top:6px'>AMPM Fashions Pvt Ltd, B-144, Sector 10, Noida - 201301</div>
  <div style='font-size:10px;color:#9CA3AF;margin-top:10px'>This is a system-generated email from AMPM IT Helpdesk. Please do not reply directly to this email.</div>
</div>";
    }

    [HttpPost]
    public IActionResult SetEmpEmail(string id, string email)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        ticket["empEmail"] = email;
        _db.SaveTicket(ticket);
        return Json(new { ok = true });
    }

    // ── Email Settings (IT/CC address used when emailing tickets) ────
    Dictionary<string,object?> GetEmailSettingsObj()
        => _db.KGetObj<Dictionary<string,object?>>("helpdesk_email_settings")
           ?? new Dictionary<string,object?> { ["itEmail"]="itsupport@ampm.in", ["ccEmails"]="", ["sendOnRaise"]=true, ["sendOnClose"]=true };

    [HttpGet]
    public IActionResult EmailSettings()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Smtp = _db.KGetObj<Dictionary<string,object?>>("smtp_settings") ?? new();
        ViewBag.EmailConfigured = _email.IsConfigured;
        ViewBag.SmtpFromEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_USER"));
        return View(GetEmailSettingsObj());
    }

    [HttpPost]
    public IActionResult EmailSettings(IFormCollection form)
    {
        var settings = new Dictionary<string,object?> {
            ["itEmail"] = form["itEmail"].ToString(),
            ["ccEmails"] = form["ccEmails"].ToString(),
            ["sendOnRaise"] = form["sendOnRaise"] == "on",
            ["sendOnClose"] = form["sendOnClose"] == "on"
        };
        _db.KSet("helpdesk_email_settings", settings);

        if (!string.IsNullOrWhiteSpace(form["smtpUser"]))
        {
            var smtp = new Dictionary<string,object?> {
                ["smtpHost"] = string.IsNullOrWhiteSpace(form["smtpHost"].ToString()) ? "smtp.office365.com" : form["smtpHost"].ToString(),
                ["smtpPort"] = string.IsNullOrWhiteSpace(form["smtpPort"].ToString()) ? "587" : form["smtpPort"].ToString(),
                ["smtpUser"] = form["smtpUser"].ToString(),
                ["smtpFrom"] = string.IsNullOrWhiteSpace(form["smtpFrom"].ToString()) ? form["smtpUser"].ToString() : form["smtpFrom"].ToString(),
                ["smtpFromName"] = string.IsNullOrWhiteSpace(form["smtpFromName"].ToString()) ? "AMPM IT Helpdesk" : form["smtpFromName"].ToString()
            };
            // Keep the existing saved password if the field was left blank (so re-saving other settings doesn't wipe it)
            if (!string.IsNullOrWhiteSpace(form["smtpPass"].ToString()))
                smtp["smtpPass"] = form["smtpPass"].ToString();
            else
            {
                var existing = _db.KGetObj<Dictionary<string,object?>>("smtp_settings");
                if (existing != null && existing.TryGetValue("smtpPass", out var oldPass)) smtp["smtpPass"] = oldPass;
            }
            _db.KSet("smtp_settings", smtp);
        }

        TempData["Success"] = "Email settings saved.";
        return RedirectToAction("EmailSettings");
    }

    [HttpPost]
    public async Task<IActionResult> SendTestEmail(string to)
    {
        var html = "<div style='font-family:Segoe UI,Arial,sans-serif;padding:16px'><h3>AMPM IT Helpdesk &mdash; Test Email</h3><p>Agar ye email aapko mil raha hai, to SMTP settings sahi se kaam kar rahi hain.</p></div>";
        var (ok, error) = await _email.SendAsync(to, null, "AMPM Helpdesk — Test Email", html);
        return Json(new { ok, error });
    }

    [HttpPost]
    public IActionResult UpdateStatus(string id, string status, string? resolution)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        ticket["status"] = status;
        if (!string.IsNullOrEmpty(resolution)) ticket["resolution"] = resolution;

        if (status == "In Progress" && string.IsNullOrEmpty(ticket.GetValueOrDefault("dateAcknowledged")?.ToString()))
            ticket["dateAcknowledged"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        if (status == "Resolved" || status == "Closed")
        {
            ticket["dateResolved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (DateTime.TryParse(ticket.GetValueOrDefault("dateRaised")?.ToString(), out var dr))
                ticket["resolutionHrs"] = Math.Round((DateTime.Now - dr).TotalHours, 2);
        }
        if (status == "Closed")
            ticket["dateClosed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        _db.SaveTicket(ticket);
        return Json(new { ok = true });
    }

    [HttpGet("/Helpdesk/Export")]
    public IActionResult Export()
    {
        var tickets = _db.GetTickets();
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Ticket ID,Date Raised,Employee,Designation,HOD,Department,Mobile,Title,Issue Type,Priority,Assigned To,Status,Date Acknowledged,Date Resolved,Date Closed,Resolution Hours");
        foreach (var t in tickets)
            csv.AppendLine(string.Join(",",
                CsvE(t.GetValueOrDefault("ticketId")?.ToString()),
                CsvE(t.GetValueOrDefault("dateRaised")?.ToString()),
                CsvE(t.GetValueOrDefault("empName")?.ToString()),
                CsvE(t.GetValueOrDefault("empDesig")?.ToString()),
                CsvE(t.GetValueOrDefault("empHod")?.ToString()),
                CsvE(t.GetValueOrDefault("empDept")?.ToString()),
                CsvE(t.GetValueOrDefault("empMobile")?.ToString()),
                CsvE(t.GetValueOrDefault("title")?.ToString()),
                CsvE(t.GetValueOrDefault("issueType")?.ToString()),
                CsvE(t.GetValueOrDefault("priority")?.ToString()),
                CsvE(t.GetValueOrDefault("assignedTo")?.ToString()),
                CsvE(t.GetValueOrDefault("status")?.ToString()),
                CsvE(t.GetValueOrDefault("dateAcknowledged")?.ToString()),
                CsvE(t.GetValueOrDefault("dateResolved")?.ToString()),
                CsvE(t.GetValueOrDefault("dateClosed")?.ToString()),
                CsvE(t.GetValueOrDefault("resolutionHrs")?.ToString())
            ));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"Helpdesk_{DateTime.Now:yyyyMMdd}.csv");
    }

    static string CsvE(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";

    [HttpGet("/api/tickets")]
    public IActionResult ApiList() => Json(_db.GetTickets());
}
