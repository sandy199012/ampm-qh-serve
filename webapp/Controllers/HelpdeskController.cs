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

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Employees = _db.GetEmployees()
            .OrderBy(e => e.GetValueOrDefault("name")?.ToString())
            .ToList();
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
        var html = "<div style='font-family:Segoe UI,Arial,sans-serif;padding:16px'><h3>AMPM IT Helpdesk &mdash; Test Email</h3><p>If you are receiving this email, your SMTP settings are working correctly.</p></div>";
        var (ok, error) = await _email.SendAsync(to, null, "AMPM Helpdesk — Test Email", html);
        return Json(new { ok, error });
    }

    [HttpPost]
    public IActionResult UpdateStatus(string id, string status, string? resolution, string? ackComment, string? closeWorkDone, string? closeIssue)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM tickets WHERE ticket_id=@id", new { id });
        if (raw == null) return NotFound();
        var ticket = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        ticket["status"] = status;
        if (!string.IsNullOrEmpty(resolution)) ticket["resolution"] = resolution;

        if (status == "In Progress" && string.IsNullOrEmpty(ticket.GetValueOrDefault("dateAcknowledged")?.ToString()))
            ticket["dateAcknowledged"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (status == "In Progress" && !string.IsNullOrWhiteSpace(ackComment))
            ticket["ackComment"] = ackComment.Trim();

        if (status == "Resolved" || status == "Closed")
        {
            ticket["dateResolved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (DateTime.TryParse(ticket.GetValueOrDefault("dateRaised")?.ToString(), out var dr))
                ticket["resolutionHrs"] = Math.Round((DateTime.Now - dr).TotalHours, 2);
        }
        if (status == "Closed")
        {
            ticket["dateClosed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (!string.IsNullOrWhiteSpace(closeWorkDone)) ticket["closeWorkDone"] = closeWorkDone.Trim();
            if (!string.IsNullOrWhiteSpace(closeIssue)) ticket["closeIssue"] = closeIssue.Trim();
        }

        _db.SaveTicket(ticket);
        return Json(new { ok = true });
    }

    // ── Colorful Excel Report (HTML table, opens directly in Excel) ─────────
    [HttpGet("/Helpdesk/Export")]
    public IActionResult Export(string? status, string? priority)
    {
        var tickets = _db.GetTickets(string.IsNullOrEmpty(status) ? null : status);
        if (!string.IsNullOrEmpty(priority))
            tickets = tickets.Where(t => t.GetValueOrDefault("priority")?.ToString() == priority).ToList();

        int total    = tickets.Count;
        int open     = tickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "Open");
        int inprog   = tickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "In Progress");
        int resolved = tickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "Resolved");
        int closed   = tickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "Closed");
        var hrsList  = tickets.Select(t => double.TryParse(t.GetValueOrDefault("resolutionHrs")?.ToString(), out var h) ? h : (double?)null)
            .Where(h => h.HasValue).Select(h => h!.Value).ToList();
        double avgHrs = hrsList.Any() ? Math.Round(hrsList.Average(), 1) : 0;
        int closedPct = total > 0 ? (int)Math.Round((resolved + closed) * 100.0 / total) : 0;
        string scope = (string.IsNullOrEmpty(status) ? "All Status" : status) + (string.IsNullOrEmpty(priority) ? "" : $" · {priority} Priority");

        string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
table{{border-collapse:collapse;width:100%}}
th{{background:#DC2626;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #991B1B}}
td{{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}}
.hdr{{background:linear-gradient(90deg,#0A192F,#1E3A5F);background-color:#0A192F;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px}}
.sub{{background:#1E293B;color:#94A3B8;font-size:10px;padding:5px 14px;letter-spacing:1px}}
.wki{{background:#FEF2F2;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0}}
.open{{background:#FEF2F2}} .inprog{{background:#EFF6FF}} .resolved{{background:#F0FDF4}} .closedRow{{background:#F8FAFC}}
.critical{{background:#FEE2E2;color:#991B1B;font-weight:bold;text-align:center}}
.high{{background:#FFEDD5;color:#C2410C;font-weight:bold;text-align:center}}
.medium{{background:#FEF3C7;color:#92400E;font-weight:bold;text-align:center}}
.low{{background:#D1FAE5;color:#065F46;font-weight:bold;text-align:center}}
.sh{{background:#DC2626;color:#FFF;font-weight:bold;text-align:center;padding:7px}}
.sl{{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}}
.sv{{text-align:center;font-weight:bold;padding:6px}}
.red{{color:#DC2626}} .green{{color:#059669}} .blue{{color:#2563EB}} .gray{{color:#4B5563}}
</style></head><body>
<table style='margin-bottom:14px;border:1px solid #DC2626'>
  <tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — IT HELPDESK TICKET REPORT</td></tr>
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {DateTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
  <tr><td class='wki'><b>Filter:</b> {E(scope)} &nbsp;&nbsp; <b>Total Tickets:</b> {total} &nbsp;&nbsp; <b>Prepared By:</b> Sandeep Kumar Singh Kushwaha — IT System Administrator</td></tr>
</table>
<table>
<thead><tr>
  <th style='width:26px'>S.No.</th>
  <th style='width:110px'>Ticket ID</th>
  <th style='width:80px'>Date Raised</th>
  <th style='width:110px'>Employee</th>
  <th style='width:90px'>Department</th>
  <th style='width:180px'>Issue Title</th>
  <th style='width:110px'>Issue Type</th>
  <th style='width:55px'>Priority</th>
  <th style='width:75px'>Status</th>
  <th style='width:80px'>Acknowledged</th>
  <th style='width:150px'>Ack Comment</th>
  <th style='width:80px'>Resolved</th>
  <th style='width:150px'>Resolution</th>
  <th style='width:80px'>Closed</th>
  <th style='width:150px'>Work Done (Close)</th>
  <th style='width:150px'>Issue (Close)</th>
  <th style='width:60px'>Res. Hrs</th>
</tr></thead><tbody>");

        int sno = 0;
        foreach (var t in tickets)
        {
            sno++;
            var st = t.GetValueOrDefault("status")?.ToString() ?? "Open";
            var pr = t.GetValueOrDefault("priority")?.ToString() ?? "Medium";
            string rowCls = st switch { "Open" => "open", "In Progress" => "inprog", "Resolved" => "resolved", "Closed" => "closedRow", _ => "" };
            string prioCls = pr switch { "Critical" => "critical", "High" => "high", "Medium" => "medium", "Low" => "low", _ => "" };
            string statusStyle = st switch { "Open" => "color:#DC2626;font-weight:bold", "In Progress" => "color:#2563EB;font-weight:bold", "Resolved" => "color:#059669;font-weight:bold", _ => "color:#4B5563;font-weight:bold" };
            sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td style='text-align:center;font-weight:bold;color:#0A192F'>{E(t.GetValueOrDefault("ticketId")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("dateRaised")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("empName")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("empDept")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("title")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("issueType")?.ToString())}</td>
  <td class='{prioCls}'>{E(pr)}</td>
  <td style='{statusStyle};text-align:center'>{E(st)}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("dateAcknowledged")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("ackComment")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("dateResolved")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("resolution")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("dateClosed")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("closeWorkDone")?.ToString())}</td>
  <td>{E(t.GetValueOrDefault("closeIssue")?.ToString())}</td>
  <td style='text-align:center'>{E(t.GetValueOrDefault("resolutionHrs")?.ToString())}</td>
</tr>");
        }
        sb.Append($@"</tbody></table>
<br>
<table style='width:360px;margin-top:14px;border:1px solid #DC2626'>
  <tr><td colspan='2' class='sh'>REPORT SUMMARY</td></tr>
  <tr><td class='sl'>Total Tickets</td><td class='sv'>{total}</td></tr>
  <tr class='open'><td class='sl'>Open</td><td class='sv red'>{open}</td></tr>
  <tr class='inprog'><td class='sl'>In Progress</td><td class='sv blue'>{inprog}</td></tr>
  <tr class='resolved'><td class='sl'>Resolved</td><td class='sv green'>{resolved}</td></tr>
  <tr class='closedRow'><td class='sl'>Closed</td><td class='sv gray'>{closed}</td></tr>
  <tr><td class='sl'>Avg. Resolution Time</td><td class='sv'>{avgHrs} hrs</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Resolved / Closed Rate</td><td class='sv green' style='font-size:13px'>{closedPct}%</td></tr>
</table>
<br>
<div style='font-size:10px;color:#6B7280;border-top:1px solid #E2E8F0;padding-top:6px'>
  <b>Sandeep Kumar Singh Kushwaha</b> | IT System Administrator | AMPM Fashions Pvt Ltd<br>
  +91 93156 31188 | B-144, Sector 10, Noida - 201301
</div></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_Helpdesk_Report_{DateTime.Now:yyyyMMdd}.xls");
    }
}
