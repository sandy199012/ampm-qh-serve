using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

// Personal performance dashboard — pulls together a user's Daily To-Do
// completion, Weekly Goals progress, and Helpdesk ticket resolution into
// one view. Any logged-in user sees their own numbers by default;
// admins/superadmins can switch to view anyone else's, same pattern as
// the Todos page's "sandy / sandeep / All Employees" dropdown.
public class PerformanceStats
{
    public int TodoTotal, TodoDone, TodoInProg, TodoPending, TodoVerified, TodoPct;
    public int GoalsTotal, GoalsDone, GoalsInProg, GoalsNotStarted, GoalsOnHold, GoalsPct;
    public int TkTotal, TkResolved, TkOpen, TkInProg, TkPct;
    public double AvgResolutionHrs;
    public int OverallPct;
}

public class PerformanceController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public PerformanceController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    (string viewUsername, string viewName, bool canViewOthers, DateTime fromD, DateTime toD) Resolve(
        UserSession current, string? user, string? from, string? to)
    {
        bool canViewOthers = current.IsAdmin;
        string viewUsername = canViewOthers && !string.IsNullOrEmpty(user) ? user : current.Username;
        var row = _db.GetUserByUsername(viewUsername);
        string viewName = !string.IsNullOrWhiteSpace(row?.Name) ? row!.Name! : current.Name;

        DateTime fromD = DateTime.TryParse(from, out var f) ? f.Date : DateTime.Today.AddDays(-29);
        DateTime toD = DateTime.TryParse(to, out var t) ? t.Date : DateTime.Today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        return (viewUsername, viewName, canViewOthers, fromD, toD);
    }

    PerformanceStats Compute(string viewUsername, string viewName, DateTime fromD, DateTime toD)
    {
        string fromS = fromD.ToString("yyyy-MM-dd"), toS = toD.ToString("yyyy-MM-dd");
        var s = new PerformanceStats();

        // ── Daily To-Do ──────────────────────────────────────
        var todos = _db.Query<string>(
                "SELECT data FROM todos WHERE task_date>=@f AND task_date<=@t AND username=@u ORDER BY task_date, ts",
                new { f = fromS, t = toS, u = viewUsername })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string, object?>>(r) ?? new()).ToList();
        s.TodoTotal = todos.Count;
        s.TodoDone = todos.Count(x => x.GetValueOrDefault("status")?.ToString() == "Done");
        s.TodoInProg = todos.Count(x => x.GetValueOrDefault("status")?.ToString() == "In Progress");
        s.TodoPending = todos.Count(x => (x.GetValueOrDefault("status")?.ToString() ?? "Pending") == "Pending");
        s.TodoVerified = todos.Count(x => x.GetValueOrDefault("status")?.ToString() == "Done" && x.GetValueOrDefault("verified")?.ToString() == "Yes");
        s.TodoPct = s.TodoTotal > 0 ? (int)Math.Round(s.TodoDone * 100.0 / s.TodoTotal) : 0;

        // ── Weekly Goals ─────────────────────────────────────
        // Goals aren't tagged to a specific employee in this schema (the Create
        // form has no "assigned to" field at all), so this is the whole IT
        // department goal sheet — same for every viewer. Shown all-time, since
        // goals are tracked by week number rather than a calendar date.
        var myGoals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string, object?>>(r) ?? new()).ToList();
        s.GoalsTotal = myGoals.Count;
        s.GoalsDone = myGoals.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed");
        s.GoalsInProg = myGoals.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress");
        s.GoalsNotStarted = myGoals.Count(g => g.GetValueOrDefault("status")?.ToString() == "Not Started");
        s.GoalsOnHold = myGoals.Count(g => g.GetValueOrDefault("status")?.ToString() == "On Hold");
        s.GoalsPct = s.GoalsTotal > 0 ? (int)Math.Round(s.GoalsDone * 100.0 / s.GoalsTotal) : 0;

        // ── Helpdesk tickets ─────────────────────────────────
        // "Assigned To" on a ticket is a free-text field that in practice just
        // says "IT Team" (not a specific technician), so there's no reliable
        // per-employee split here either — this is every ticket raised in the
        // period, i.e. the IT team's overall helpdesk performance.
        var myTickets = _db.GetTickets()
            .Where(t => DateTime.TryParse(t.GetValueOrDefault("dateRaised")?.ToString(), out var dr) && dr.Date >= fromD && dr.Date <= toD)
            .ToList();
        s.TkTotal = myTickets.Count;
        s.TkResolved = myTickets.Count(t => t.GetValueOrDefault("status")?.ToString() is "Resolved" or "Closed");
        s.TkOpen = myTickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "Open");
        s.TkInProg = myTickets.Count(t => t.GetValueOrDefault("status")?.ToString() == "In Progress");
        var hrs = myTickets.Select(t => double.TryParse(t.GetValueOrDefault("resolutionHrs")?.ToString(), out var h) ? h : (double?)null)
            .Where(h => h.HasValue).Select(h => h!.Value).ToList();
        s.AvgResolutionHrs = hrs.Any() ? Math.Round(hrs.Average(), 1) : 0;
        s.TkPct = s.TkTotal > 0 ? (int)Math.Round(s.TkResolved * 100.0 / s.TkTotal) : 0;

        // ── Overall (average of whichever areas actually have data) ──
        var rates = new List<int>();
        if (s.TodoTotal > 0) rates.Add(s.TodoPct);
        if (s.GoalsTotal > 0) rates.Add(s.GoalsPct);
        if (s.TkTotal > 0) rates.Add(s.TkPct);
        s.OverallPct = rates.Any() ? (int)Math.Round(rates.Average()) : 0;

        return s;
    }

    [HttpGet]
    public IActionResult Index(string? user, string? from, string? to)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");

        var (viewUsername, viewName, canViewOthers, fromD, toD) = Resolve(current, user, from, to);
        var s = Compute(viewUsername, viewName, fromD, toD);

        ViewBag.User          = current;
        ViewBag.CanViewOthers = canViewOthers;
        ViewBag.ViewUsername  = viewUsername;
        ViewBag.ViewName      = viewName;
        ViewBag.Users         = canViewOthers ? _db.GetUsers().Where(u => u.IsActive == 1).OrderBy(u => u.Name).ToList() : new List<UserRow>();
        ViewBag.From          = fromD.ToString("yyyy-MM-dd");
        ViewBag.To            = toD.ToString("yyyy-MM-dd");
        ViewBag.Stats         = s;
        return View();
    }

    // ── Excel export of the same numbers ─────────────────────
    [HttpGet("/Performance/Export")]
    public IActionResult Export(string? user, string? from, string? to)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");

        var (viewUsername, viewName, _, fromD, toD) = Resolve(current, user, from, to);
        var s = Compute(viewUsername, viewName, fromD, toD);

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
table{{border-collapse:collapse;width:420px;margin-bottom:16px}}
th{{background:#4F46E5;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #3730A3}}
td{{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}}
.hdr{{background:#4F46E5;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px;width:100%}}
.sub{{background:#312E81;color:#A5B4FC;font-size:10px;padding:5px 14px;letter-spacing:1px;width:100%}}
.wki{{background:#F5F3FF;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0;width:100%}}
.sh{{background:#4F46E5;color:#FFF;font-weight:bold;text-align:center;padding:7px}}
.sl{{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}}
.sv{{text-align:center;font-weight:bold;padding:6px}}
.green{{color:#059669}} .blue{{color:#2563EB}} .amber{{color:#D97706}} .red{{color:#DC2626}}
</style></head><body>
<table style='border:1px solid #4F46E5'>
  <tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — EMPLOYEE PERFORMANCE REPORT</td></tr>
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {DateTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
  <tr><td class='wki'><b>Employee:</b> {System.Net.WebUtility.HtmlEncode(viewName)} &nbsp;&nbsp; <b>Period:</b> {fromD:dd-MMM-yyyy} to {toD:dd-MMM-yyyy}</td></tr>
</table>

<table>
  <tr><td colspan='2' class='sh'>DAILY TO-DO</td></tr>
  <tr><td class='sl'>Total Tasks</td><td class='sv'>{s.TodoTotal}</td></tr>
  <tr><td class='sl'>Done</td><td class='sv green'>{s.TodoDone}</td></tr>
  <tr><td class='sl'>In Progress</td><td class='sv blue'>{s.TodoInProg}</td></tr>
  <tr><td class='sl'>Pending</td><td class='sv amber'>{s.TodoPending}</td></tr>
  <tr><td class='sl'>Verified Done</td><td class='sv green'>{s.TodoVerified}</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Completion Rate</td><td class='sv green' style='font-size:13px'>{s.TodoPct}%</td></tr>
</table>

<table>
  <tr><td colspan='2' class='sh'>WEEKLY GOALS (IT Department — not tracked per employee)</td></tr>
  <tr><td class='sl'>Total Goals</td><td class='sv'>{s.GoalsTotal}</td></tr>
  <tr><td class='sl'>Completed</td><td class='sv green'>{s.GoalsDone}</td></tr>
  <tr><td class='sl'>In Progress</td><td class='sv blue'>{s.GoalsInProg}</td></tr>
  <tr><td class='sl'>Not Started</td><td class='sv red'>{s.GoalsNotStarted}</td></tr>
  <tr><td class='sl'>On Hold</td><td class='sv amber'>{s.GoalsOnHold}</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Completion Rate</td><td class='sv green' style='font-size:13px'>{s.GoalsPct}%</td></tr>
</table>

<table>
  <tr><td colspan='2' class='sh'>HELPDESK TICKETS (IT Department — not tracked per employee)</td></tr>
  <tr><td class='sl'>Total Raised</td><td class='sv'>{s.TkTotal}</td></tr>
  <tr><td class='sl'>Resolved / Closed</td><td class='sv green'>{s.TkResolved}</td></tr>
  <tr><td class='sl'>Open</td><td class='sv red'>{s.TkOpen}</td></tr>
  <tr><td class='sl'>In Progress</td><td class='sv blue'>{s.TkInProg}</td></tr>
  <tr><td class='sl'>Avg. Resolution Time</td><td class='sv'>{s.AvgResolutionHrs} hrs</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Resolution Rate</td><td class='sv green' style='font-size:13px'>{s.TkPct}%</td></tr>
</table>

<table>
  <tr><td colspan='2' class='sh'>OVERALL PERFORMANCE</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Overall Score</td><td class='sv green' style='font-size:15px'>{s.OverallPct}%</td></tr>
</table>

<div style='font-size:10px;color:#6B7280;border-top:1px solid #E2E8F0;padding-top:6px;margin-top:10px'>
  Prepared By: Sandeep Kumar Singh Kushwaha | IT System Administrator | AMPM Fashions Pvt Ltd<br>
  +91 93156 31188 | B-144, Sector 10, Noida - 201301
</div></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_Performance_{viewUsername}_{fromD:yyyyMMdd}_{toD:yyyyMMdd}.xls");
    }
}
