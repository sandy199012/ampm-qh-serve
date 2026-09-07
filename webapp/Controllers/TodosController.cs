using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

// Daily To-Do — each user logs what they worked on today; admins / anyone with
// "Approve" permission on this module can view everyone's list and export a
// colorful Excel report for any date range.
public class TodosController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public TodosController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    List<Dictionary<string, object?>> LoadForDate(string dateStr, string? username)
    {
        string sql = string.IsNullOrEmpty(username)
            ? "SELECT data FROM todos WHERE task_date=@d ORDER BY username, ts"
            : "SELECT data FROM todos WHERE task_date=@d AND username=@u ORDER BY ts";
        return _db.Query<string>(sql, new { d = dateStr, u = username })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string, object?>>(r) ?? new()).ToList();
    }

    [HttpGet]
    public IActionResult Index(string? date, string? user)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");
        bool canApprove = current.CanApprove("Todos");

        DateTime selDate = DateTime.TryParse(date, out var d) ? d.Date : DateTime.Today;
        string dateStr = selDate.ToString("yyyy-MM-dd");

        string viewUser = canApprove ? (string.IsNullOrEmpty(user) ? current.Username : user) : current.Username;
        bool viewingAll = canApprove && viewUser == "__all__";

        var todos = viewingAll ? LoadForDate(dateStr, null) : LoadForDate(dateStr, viewUser);

        ViewBag.User        = current;
        ViewBag.CanApprove  = canApprove;
        ViewBag.SelDate     = selDate;
        ViewBag.DateStr     = dateStr;
        ViewBag.ViewUser    = viewUser;
        ViewBag.ViewingAll  = viewingAll;
        ViewBag.Users       = canApprove ? _db.GetUsers().Where(u => u.IsActive == 1).OrderBy(u => u.Name).ToList() : new List<UserRow>();
        ViewBag.Total       = todos.Count;
        ViewBag.Pending     = todos.Count(t => (t.GetValueOrDefault("status")?.ToString() ?? "Pending") == "Pending");
        ViewBag.InProg      = todos.Count(t => t.GetValueOrDefault("status")?.ToString() == "In Progress");
        ViewBag.Done        = todos.Count(t => t.GetValueOrDefault("status")?.ToString() == "Done");
        return View(todos);
    }

    // HTML <input type="time"> posts 24-hour "HH:mm" — reformat to a friendly
    // 12-hour display string, or "" if left blank / unparsable.
    static string FormatTime(string? raw)
        => (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out var tm)) ? tm.ToString("hh:mm tt") : "";

    [HttpPost]
    public IActionResult Add(string task, string? priority, string? date, string? startTime, string? endTime)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");
        string taskDate = DateTime.TryParse(date, out var d) ? d.Date.ToString("yyyy-MM-dd") : DateTime.Today.ToString("yyyy-MM-dd");

        if (string.IsNullOrWhiteSpace(task))
        {
            TempData["Error"] = "Task likhna zaroori hai.";
            return RedirectToAction("Index", new { date = taskDate });
        }

        string pr = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority!;
        var id = Guid.NewGuid().ToString("N")[..8];
        var todo = new Dictionary<string, object?>
        {
            ["id"]          = id,
            ["username"]    = current.Username,
            ["userName"]    = current.Name,
            ["task"]        = task.Trim(),
            ["priority"]    = pr,
            ["status"]      = "Pending",
            ["taskDate"]    = taskDate,
            ["startTime"]   = FormatTime(startTime),
            ["endTime"]     = FormatTime(endTime),
            ["createdAt"]   = DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
            ["completedAt"] = null,
        };
        _db.Execute("INSERT INTO todos (id,username,task_date,data,ts) VALUES (@id,@u,@d,@data,@ts)",
            new { id, u = current.Username, d = taskDate, data = JsonConvert.SerializeObject(todo), ts = DateTime.Now.ToString("o") });

        TempData["Success"] = "Task add ho gaya!";
        return RedirectToAction("Index", new { date = taskDate });
    }

    [HttpPost]
    public IActionResult SetStatus(string id, string status)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });

        var raw = _db.QueryFirst<string>("SELECT data FROM todos WHERE id=@id", new { id });
        if (raw == null) return Json(new { ok = false, error = "Task nahi mila." });
        var t = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        var owner = t.GetValueOrDefault("username")?.ToString() ?? "";
        if (owner != current.Username && !current.CanApprove("Todos"))
            return Json(new { ok = false, error = "Sirf apna task update kar sakte ho." });

        t["status"] = status;
        t["completedAt"] = status == "Done" ? DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt") : null;
        _db.Execute("UPDATE todos SET data=@d WHERE id=@id", new { d = JsonConvert.SerializeObject(t), id });
        return Json(new { ok = true });
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });

        var raw = _db.QueryFirst<string>("SELECT data FROM todos WHERE id=@id", new { id });
        if (raw == null) return Json(new { ok = false, error = "Task nahi mila." });
        var t = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        var owner = t.GetValueOrDefault("username")?.ToString() ?? "";
        if (owner != current.Username && !current.CanApprove("Todos"))
            return Json(new { ok = false, error = "Sirf apna task delete kar sakte ho." });

        _db.Execute("DELETE FROM todos WHERE id=@id", new { id });
        return Json(new { ok = true });
    }

    // ── Colorful Excel Report (HTML table, opens directly in Excel) ─────────
    [HttpGet("/Todos/Export")]
    public IActionResult Export(string? from, string? to, string? user)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");
        bool canApprove = current.CanApprove("Todos");

        DateTime fromD = DateTime.TryParse(from, out var f) ? f.Date : DateTime.Today;
        DateTime toD   = DateTime.TryParse(to, out var tt) ? tt.Date : DateTime.Today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        string fromS = fromD.ToString("yyyy-MM-dd"), toS = toD.ToString("yyyy-MM-dd");

        string targetUser = canApprove ? (user ?? "") : current.Username;
        bool allUsers = canApprove && (targetUser == "__all__" || string.IsNullOrEmpty(targetUser));

        string sql = allUsers
            ? "SELECT data FROM todos WHERE task_date>=@f AND task_date<=@t ORDER BY task_date, username, ts"
            : "SELECT data FROM todos WHERE task_date>=@f AND task_date<=@t AND username=@u ORDER BY task_date, ts";
        var rows = _db.Query<string>(sql, new { f = fromS, t = toS, u = targetUser })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string, object?>>(r) ?? new()).ToList();

        int total = rows.Count;
        int done = rows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Done");
        int inprog = rows.Count(r => r.GetValueOrDefault("status")?.ToString() == "In Progress");
        int pending = rows.Count(r => (r.GetValueOrDefault("status")?.ToString() ?? "Pending") == "Pending");
        int pct = total > 0 ? (int)Math.Round(done * 100.0 / total) : 0;
        string scope = allUsers ? "All Employees" : (rows.FirstOrDefault()?.GetValueOrDefault("userName")?.ToString() ?? targetUser);

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
table{{border-collapse:collapse;width:100%}}
th{{background:#4F46E5;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #3730A3}}
td{{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}}
.hdr{{background:linear-gradient(90deg,#4F46E5,#7C3AED);background-color:#4F46E5;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px}}
.sub{{background:#312E81;color:#A5B4FC;font-size:10px;padding:5px 14px;letter-spacing:1px}}
.wki{{background:#F5F3FF;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0}}
.datehdr{{background:#EEF2FF;color:#3730A3;font-weight:bold;padding:6px 8px;font-size:11px}}
.done{{background:#F0FDF4}} .inprog{{background:#EFF6FF}} .pending{{background:#FFFBEB}}
.high{{background:#FEE2E2;color:#991B1B;font-weight:bold;text-align:center}}
.medium{{background:#FEF3C7;color:#92400E;font-weight:bold;text-align:center}}
.low{{background:#D1FAE5;color:#065F46;font-weight:bold;text-align:center}}
.sh{{background:#4F46E5;color:#FFF;font-weight:bold;text-align:center;padding:7px}}
.sl{{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}}
.sv{{text-align:center;font-weight:bold;padding:6px}}
.green{{color:#059669}} .blue{{color:#2563EB}} .amber{{color:#D97706}}
</style></head><body>
<table style='margin-bottom:14px;border:1px solid #4F46E5'>
  <tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — DAILY TO-DO / WORK REPORT</td></tr>
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {DateTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
  <tr><td class='wki'><b>Period:</b> {fromD:dd-MMM-yyyy} to {toD:dd-MMM-yyyy} &nbsp;&nbsp; <b>Scope:</b> {scope} &nbsp;&nbsp; <b>Prepared By:</b> Sandeep Kumar Singh Kushwaha — IT System Administrator</td></tr>
</table>
<table>
<thead><tr>
  <th style='width:28px'>S.No.</th>
  <th style='width:80px'>Date</th>
  {(allUsers ? "<th style='width:100px'>Employee</th>" : "")}
  <th style='width:220px'>Task / Work Done</th>
  <th style='width:110px'>Time</th>
  <th style='width:60px'>Priority</th>
  <th style='width:80px'>Status</th>
  <th style='width:110px'>Added At</th>
  <th style='width:110px'>Completed At</th>
</tr></thead><tbody>");

        int sno = 0;
        string? lastDate = null;
        foreach (var g in rows)
        {
            var td = g.GetValueOrDefault("taskDate")?.ToString() ?? "";
            if (td != lastDate)
            {
                sb.Append($"<tr><td colspan='{(allUsers ? 9 : 8)}' class='datehdr'>📅 {td}</td></tr>");
                lastDate = td;
            }
            sno++;
            var status = g.GetValueOrDefault("status")?.ToString() ?? "Pending";
            var priority = g.GetValueOrDefault("priority")?.ToString() ?? "Medium";
            string rowCls = status switch { "Done" => "done", "In Progress" => "inprog", _ => "pending" };
            string prioCls = priority switch { "High" => "high", "Medium" => "medium", "Low" => "low", _ => "" };
            string statusStyle = status switch { "Done" => "color:#059669;font-weight:bold", "In Progress" => "color:#2563EB;font-weight:bold", _ => "color:#D97706;font-weight:bold" };
            var st1 = g.GetValueOrDefault("startTime")?.ToString(); var et1 = g.GetValueOrDefault("endTime")?.ToString();
            string timeRange = (!string.IsNullOrEmpty(st1) || !string.IsNullOrEmpty(et1)) ? $"{st1} - {et1}" : "";
            sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td style='text-align:center'>{td}</td>
  {(allUsers ? $"<td>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("userName")?.ToString())}</td>" : "")}
  <td>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("task")?.ToString())}</td>
  <td style='text-align:center;white-space:nowrap'>{timeRange}</td>
  <td class='{prioCls}'>{priority}</td>
  <td style='{statusStyle};text-align:center'>{status}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("createdAt")}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("completedAt")}</td>
</tr>");
        }
        sb.Append($@"</tbody></table>
<br>
<table style='width:360px;margin-top:14px;border:1px solid #4F46E5'>
  <tr><td colspan='2' class='sh'>REPORT SUMMARY</td></tr>
  <tr><td class='sl'>Total Tasks</td><td class='sv'>{total}</td></tr>
  <tr class='done'><td class='sl'>Done</td><td class='sv green'>{done}</td></tr>
  <tr class='inprog'><td class='sl'>In Progress</td><td class='sv blue'>{inprog}</td></tr>
  <tr class='pending'><td class='sl'>Pending</td><td class='sv amber'>{pending}</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Completion Rate</td><td class='sv green' style='font-size:13px'>{pct}%</td></tr>
</table>
<br>
<div style='font-size:10px;color:#6B7280;border-top:1px solid #E2E8F0;padding-top:6px'>
  <b>Sandeep Kumar Singh Kushwaha</b> | IT System Administrator | AMPM Fashions Pvt Ltd<br>
  +91 93156 31188 | B-144, Sector 10, Noida - 201301
</div></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_Todo_Report_{fromD:yyyyMMdd}_{toD:yyyyMMdd}.xls");
    }
}
