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
    public IActionResult Index(string? date, string? user, string? cdate)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");
        bool canApprove = current.CanApprove("Todos");

        DateTime selDate = DateTime.TryParse(date, out var d) ? d.Date : IstTime.Today;
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

        // ── Morning Checklist tab (recurring daily IT check items) ──
        DateTime cSelDate = DateTime.TryParse(cdate, out var cd) ? cd.Date : IstTime.Today;
        string cDateStr = cSelDate.ToString("yyyy-MM-dd");
        var items = EnsureChecklistSeed();
        var log = _db.GetChecklistLogForDate(cDateStr);
        ViewBag.ChecklistItems  = items;
        ViewBag.ChecklistLog    = log;
        ViewBag.CDateStr        = cDateStr;
        ViewBag.CSelDate        = cSelDate;
        int cTotal = items.Count(i => (i.GetValueOrDefault("active")?.ToString() ?? "True") != "False");
        int cChecked = log.Values.Count(v => v.GetValueOrDefault("status")?.ToString() == "Checked");
        int cIssue   = log.Values.Count(v => v.GetValueOrDefault("status")?.ToString() == "Issue Found");
        ViewBag.CTotal   = cTotal;
        ViewBag.CChecked = cChecked;
        ViewBag.CIssue   = cIssue;
        ViewBag.CPending = Math.Max(0, cTotal - cChecked - cIssue);

        return View(todos);
    }

    // Default recurring items, seeded once (first time the checklist tab is
    // opened and no items exist yet) from Sandy's real morning-check routine
    // across AMPM's locations (HO, DLF Emporio, The Kila, Khan Market) — mirrors
    // the "Daily IT Task Tracker" sheet in his reference Excel. After the first
    // seed, everything is fully editable/addable/removable from the UI.
    static readonly (string loc, string cat)[] SeedItems = new[]
    {
        ("AMPM HO","Internet"), ("AMPM HO","Emails"), ("AMPM HO","ERP-Wondersoft"),
        ("AMPM HO","Busy Accounting Software"), ("AMPM HO","CCTV"), ("AMPM HO","EPBAX"),
        ("AMPM HO","Crome Cast"), ("AMPM HO","UPS"), ("AMPM HO","Music System"),
        ("Emporio","Internet"), ("Emporio","Emails"), ("Emporio","ERP-Wondersoft"),
        ("Emporio","CCTV"), ("Emporio","UPS"), ("Emporio","Music System"),
        ("Kila","Internet"), ("Kila","Emails"), ("Kila","ERP-Wondersoft"),
        ("Kila","CCTV"), ("Kila","UPS"), ("Kila","Music System"),
        ("Khan","Internet"), ("Khan","Emails"), ("Khan","ERP-Wondersoft"),
        ("Khan","CCTV"), ("Khan","UPS"), ("Khan","Music System"),
    };

    List<Dictionary<string,object?>> EnsureChecklistSeed()
    {
        var items = _db.GetChecklistItems();
        if (items.Any()) return items;

        var seeded = SeedItems.Select(s => new Dictionary<string,object?>
        {
            ["id"]       = Guid.NewGuid().ToString("N")[..8],
            ["location"] = s.loc,
            ["category"] = s.cat,
            ["task"]     = "Checking",
            ["priority"] = "High",
            ["active"]   = true,
            ["createdAt"]= IstTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
        }).ToList();
        _db.SaveChecklistItems(seeded);
        return seeded;
    }

    [HttpPost]
    public IActionResult ChecklistAddItem(string location, string category, string? task, string? priority, string? cdate)
    {
        if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(category))
        {
            TempData["Error"] = "Location and Category are required.";
            return RedirectToAction("Index", new { cdate });
        }
        var items = EnsureChecklistSeed();
        items.Add(new Dictionary<string,object?>
        {
            ["id"]       = Guid.NewGuid().ToString("N")[..8],
            ["location"] = location.Trim(),
            ["category"] = category.Trim(),
            ["task"]     = string.IsNullOrWhiteSpace(task) ? "Checking" : task.Trim(),
            ["priority"] = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority,
            ["active"]   = true,
            ["createdAt"]= IstTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
        });
        _db.SaveChecklistItems(items);
        TempData["Success"] = "Checklist item added.";
        return RedirectToAction("Index", new { cdate });
    }

    [HttpPost]
    public IActionResult ChecklistDeleteItem(string id, string? cdate)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });
        if (!current.CanApprove("Todos")) return Json(new { ok = false, error = "Not allowed." });

        var items = _db.GetChecklistItems();
        items.RemoveAll(i => i.GetValueOrDefault("id")?.ToString() == id);
        _db.SaveChecklistItems(items);
        return Json(new { ok = true });
    }

    [HttpPost]
    public IActionResult ChecklistSetStatus(string itemId, string date, string status, string? note)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(status))
            return Json(new { ok = false, error = "Missing data." });

        _db.SetChecklistStatus(itemId, date, status, note, current.Name);
        return Json(new { ok = true });
    }

    // ── Checklist Excel Report ────────────────────────────────
    [HttpGet("/Todos/ChecklistExport")]
    public IActionResult ChecklistExport(string? from, string? to)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");

        DateTime fromD = DateTime.TryParse(from, out var f) ? f.Date : IstTime.Today;
        DateTime toD   = DateTime.TryParse(to, out var tt) ? tt.Date : IstTime.Today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        string fromS = fromD.ToString("yyyy-MM-dd"), toS = toD.ToString("yyyy-MM-dd");

        var items = EnsureChecklistSeed().ToDictionary(i => i.GetValueOrDefault("id")?.ToString() ?? "", i => i);
        var logRows = _db.GetChecklistLogRange(fromS, toS);

        int total = logRows.Count;
        int checkedCnt = logRows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Checked");
        int issueCnt   = logRows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Issue Found");
        int pendingCnt = logRows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Pending");
        int pct = total > 0 ? (int)Math.Round(checkedCnt * 100.0 / total) : 0;

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
table{{border-collapse:collapse;width:100%}}
th{{background:#0891B2;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #155E75}}
td{{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}}
.hdr{{background:#0891B2;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px}}
.sub{{background:#164E63;color:#A5F3FC;font-size:10px;padding:5px 14px;letter-spacing:1px}}
.wki{{background:#ECFEFF;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0}}
.datehdr{{background:#ECFEFF;color:#155E75;font-weight:bold;padding:6px 8px;font-size:11px}}
.checked{{background:#F0FDF4}} .pending{{background:#FFFBEB}} .issue{{background:#FEF2F2}}
.high{{background:#FEE2E2;color:#991B1B;font-weight:bold;text-align:center}}
.medium{{background:#FEF3C7;color:#92400E;font-weight:bold;text-align:center}}
.low{{background:#D1FAE5;color:#065F46;font-weight:bold;text-align:center}}
.sh{{background:#0891B2;color:#FFF;font-weight:bold;text-align:center;padding:7px}}
.sl{{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}}
.sv{{text-align:center;font-weight:bold;padding:6px}}
.green{{color:#059669}} .amber{{color:#D97706}} .red{{color:#DC2626}}
</style></head><body>
<table style='margin-bottom:14px;border:1px solid #0891B2'>
  <tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — MORNING IT CHECKLIST REPORT</td></tr>
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {IstTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
  <tr><td class='wki'><b>Period:</b> {fromD:dd-MMM-yyyy} to {toD:dd-MMM-yyyy} &nbsp;&nbsp; <b>Prepared By:</b> Sandeep Kumar Singh Kushwaha — IT System Administrator</td></tr>
</table>
<table>
<thead><tr>
  <th style='width:28px'>S.No.</th>
  <th style='width:80px'>Date</th>
  <th style='width:100px'>Location</th>
  <th style='width:150px'>Category</th>
  <th style='width:150px'>Task</th>
  <th style='width:55px'>Priority</th>
  <th style='width:80px'>Status</th>
  <th style='width:160px'>Note</th>
  <th style='width:110px'>Updated By</th>
  <th style='width:110px'>Updated At</th>
</tr></thead><tbody>");

        int sno = 0;
        string? lastDate = null;
        foreach (var r in logRows)
        {
            var cdt = r.GetValueOrDefault("checkDate")?.ToString() ?? "";
            if (cdt != lastDate)
            {
                sb.Append($"<tr><td colspan='10' class='datehdr'>📅 {cdt}</td></tr>");
                lastDate = cdt;
            }
            sno++;
            var itemId = r.GetValueOrDefault("itemId")?.ToString() ?? "";
            items.TryGetValue(itemId, out var itm);
            var status = r.GetValueOrDefault("status")?.ToString() ?? "Pending";
            var priority = itm?.GetValueOrDefault("priority")?.ToString() ?? "Medium";
            string rowCls = status switch { "Checked" => "checked", "Issue Found" => "issue", _ => "pending" };
            string prioCls = priority switch { "High" => "high", "Medium" => "medium", "Low" => "low", _ => "" };
            string statusStyle = status switch { "Checked" => "color:#059669;font-weight:bold", "Issue Found" => "color:#DC2626;font-weight:bold", _ => "color:#D97706;font-weight:bold" };
            sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td style='text-align:center'>{cdt}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm?.GetValueOrDefault("location")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm?.GetValueOrDefault("category")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm?.GetValueOrDefault("task")?.ToString() ?? "")}</td>
  <td class='{prioCls}'>{priority}</td>
  <td style='{statusStyle};text-align:center'>{status}</td>
  <td>{System.Net.WebUtility.HtmlEncode(r.GetValueOrDefault("note")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(r.GetValueOrDefault("updatedBy")?.ToString() ?? "")}</td>
  <td style='text-align:center'>{r.GetValueOrDefault("updatedAt")}</td>
</tr>");
        }
        sb.Append($@"</tbody></table>
<br>
<table style='width:360px;margin-top:14px;border:1px solid #0891B2'>
  <tr><td colspan='2' class='sh'>REPORT SUMMARY</td></tr>
  <tr><td class='sl'>Total Entries</td><td class='sv'>{total}</td></tr>
  <tr class='checked'><td class='sl'>Checked</td><td class='sv green'>{checkedCnt}</td></tr>
  <tr class='pending'><td class='sl'>Pending</td><td class='sv amber'>{pendingCnt}</td></tr>
  <tr class='issue'><td class='sl'>Issue Found</td><td class='sv red'>{issueCnt}</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Checked Rate</td><td class='sv green' style='font-size:13px'>{pct}%</td></tr>
</table>
<br>
<div style='font-size:10px;color:#6B7280;border-top:1px solid #E2E8F0;padding-top:6px'>
  <b>Sandeep Kumar Singh Kushwaha</b> | IT System Administrator | AMPM Fashions Pvt Ltd<br>
  +91 93156 31188 | B-144, Sector 10, Noida - 201301
</div></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_Morning_Checklist_{fromD:yyyyMMdd}_{toD:yyyyMMdd}.xls");
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
        string taskDate = DateTime.TryParse(date, out var d) ? d.Date.ToString("yyyy-MM-dd") : IstTime.Today.ToString("yyyy-MM-dd");

        if (string.IsNullOrWhiteSpace(task))
        {
            TempData["Error"] = "Task description is required.";
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
            ["createdAt"]   = IstTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
            ["completedAt"] = null,
        };
        _db.Execute("INSERT INTO todos (id,username,task_date,data,ts) VALUES (@id,@u,@d,@data,@ts)",
            new { id, u = current.Username, d = taskDate, data = JsonConvert.SerializeObject(todo), ts = IstTime.Now.ToString("o") });

        TempData["Success"] = "Task added successfully!";
        return RedirectToAction("Index", new { date = taskDate });
    }

    [HttpPost]
    public IActionResult SetStatus(string id, string status, string? note, string? verified)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });

        var raw = _db.QueryFirst<string>("SELECT data FROM todos WHERE id=@id", new { id });
        if (raw == null) return Json(new { ok = false, error = "Task not found." });
        var t = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        var owner = t.GetValueOrDefault("username")?.ToString() ?? "";
        if (owner != current.Username && !current.CanApprove("Todos"))
            return Json(new { ok = false, error = "You can only update your own tasks." });

        t["status"] = status;
        t["completedAt"] = status == "Done" ? IstTime.Now.ToString("dd-MMM-yyyy hh:mm tt") : null;

        // "In Progress" asks what step is being taken; "Done" asks the final step
        // and whether it was verified — both surface on the card and in the Excel export.
        if (status == "In Progress" && !string.IsNullOrWhiteSpace(note))
            t["progressNote"] = note.Trim();
        if (status == "Done")
        {
            if (!string.IsNullOrWhiteSpace(note)) t["lastStepNote"] = note.Trim();
            t["verified"] = verified == "Yes" ? "Yes" : "No";
        }

        _db.Execute("UPDATE todos SET data=@d WHERE id=@id", new { d = JsonConvert.SerializeObject(t), id });
        return Json(new { ok = true });
    }

    [HttpPost]
    public IActionResult Delete(string id)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return Json(new { ok = false, error = "Login required." });

        var raw = _db.QueryFirst<string>("SELECT data FROM todos WHERE id=@id", new { id });
        if (raw == null) return Json(new { ok = false, error = "Task not found." });
        var t = JsonConvert.DeserializeObject<Dictionary<string, object?>>(raw) ?? new();
        var owner = t.GetValueOrDefault("username")?.ToString() ?? "";
        if (owner != current.Username && !current.CanApprove("Todos"))
            return Json(new { ok = false, error = "You can only delete your own tasks." });

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

        DateTime fromD = DateTime.TryParse(from, out var f) ? f.Date : IstTime.Today;
        DateTime toD   = DateTime.TryParse(to, out var tt) ? tt.Date : IstTime.Today;
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
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {IstTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
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
  <th style='width:160px'>Step / Last Update</th>
  <th style='width:70px'>Verified</th>
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
                sb.Append($"<tr><td colspan='{(allUsers ? 11 : 10)}' class='datehdr'>📅 {td}</td></tr>");
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
            string stepNote = status switch {
                "Done" => g.GetValueOrDefault("lastStepNote")?.ToString() ?? "",
                "In Progress" => g.GetValueOrDefault("progressNote")?.ToString() ?? "",
                _ => ""
            };
            string verifiedVal = status == "Done" ? (g.GetValueOrDefault("verified")?.ToString() ?? "No") : "";
            string verifiedStyle = verifiedVal == "Yes" ? "color:#059669;font-weight:bold" : (verifiedVal == "No" ? "color:#DC2626;font-weight:bold" : "");
            sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td style='text-align:center'>{td}</td>
  {(allUsers ? $"<td>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("userName")?.ToString())}</td>" : "")}
  <td>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("task")?.ToString())}</td>
  <td style='text-align:center;white-space:nowrap'>{timeRange}</td>
  <td class='{prioCls}'>{priority}</td>
  <td style='{statusStyle};text-align:center'>{status}</td>
  <td>{System.Net.WebUtility.HtmlEncode(stepNote)}</td>
  <td style='{verifiedStyle};text-align:center'>{verifiedVal}</td>
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
