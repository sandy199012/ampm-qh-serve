using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

public class GoalsController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public GoalsController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index(int? week, string? status)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var goals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        int maxWeek = goals.Any() ? goals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        int curWeek = week ?? maxWeek;
        var weekGoals = goals.Where(g => g.GetValueOrDefault("weekNo")?.ToString() == curWeek.ToString()).ToList();
        var filtered = weekGoals.Where(g => string.IsNullOrEmpty(status) || g.GetValueOrDefault("status")?.ToString() == status).ToList();

        ViewBag.WeekStart    = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekStart")?.ToString() ?? "";
        ViewBag.WeekEnd      = weekGoals.FirstOrDefault()?.GetValueOrDefault("weekEnd")?.ToString() ?? "";
        ViewBag.CurrentWeek  = curWeek;
        ViewBag.MaxWeek      = maxWeek;
        ViewBag.StatusFilter = status;
        ViewBag.Total        = filtered.Count;
        ViewBag.Completed    = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed");
        ViewBag.InProg       = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress");
        ViewBag.NotStarted   = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "Not Started");
        ViewBag.OnHold       = filtered.Count(g => g.GetValueOrDefault("status")?.ToString() == "On Hold");
        return View(filtered);
    }

    [HttpPost]
    public IActionResult Complete(string id)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var g = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        g["status"] = "Completed"; g["progress"] = 100;
        g["completedOn"] = DateTime.Now.ToString("dd-MMM-yyyy");
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(g), id });
        return Json(new { ok=true });
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
        var allGoals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no DESC")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        int maxWk = allGoals.Any() ? allGoals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        // Week dates
        var today = DateTime.Today;
        int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        string wkStart = today.AddDays(-diff).ToString("dd-MMM-yyyy");
        string wkEnd   = today.AddDays(-diff+6).ToString("dd-MMM-yyyy");

        var goal = new Dictionary<string,object?>
        {
            ["id"]          = Guid.NewGuid().ToString("N")[..8],
            ["weekNo"]      = maxWk,
            ["weekStart"]   = wkStart,
            ["weekEnd"]     = wkEnd,
            ["title"]       = form["title"].ToString(),
            ["category"]    = form["category"].ToString(),
            ["priority"]    = form["priority"].ToString(),
            ["department"]  = form["department"].ToString(),
            ["requestedBy"] = form["requestedBy"].ToString(),
            ["assignedTo"]  = form["assignedTo"].ToString(),
            ["startDate"]   = form["startDate"].ToString(),
            ["targetDate"]  = form["targetDate"].ToString(),
            ["status"]      = form["status"].ToString(),
            ["progress"]    = int.TryParse(form["progress"].ToString(), out var p) ? p : 0,
            ["remarks"]     = form["remarks"].ToString(),
            ["ticketRef"]   = form["ticketRef"].ToString(),
            ["approval"]    = form["approval"].ToString(),
            ["isPending"]   = false,
        };
        _db.Execute("INSERT INTO goals (id,week_no,data,ts) VALUES (@id,@wk,@data,@ts)",
            new { id=goal["id"], wk=maxWk, data=JsonConvert.SerializeObject(goal), ts=DateTime.Now.ToString("o") });
        TempData["Success"] = "Goal added!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(string id)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        if (raw == null) return NotFound();
        var g = JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new();
        g["id"] = id;
        return View(g);
    }

    [HttpPost]
    public IActionResult Edit(string id, IFormCollection form)
    {
        var raw = _db.QueryFirst<string>("SELECT data FROM goals WHERE id=@id", new { id });
        var g = raw != null ? JsonConvert.DeserializeObject<Dictionary<string,object?>>(raw) ?? new() : new();
        g["title"]      = form["title"].ToString();
        g["category"]   = form["category"].ToString();
        g["priority"]   = form["priority"].ToString();
        g["department"] = form["department"].ToString();
        g["targetDate"] = form["targetDate"].ToString();
        g["status"]     = form["status"].ToString();
        g["remarks"]    = form["remarks"].ToString();
        g["ticketRef"]  = form["ticketRef"].ToString();
        g["approval"]   = form["approval"].ToString();
        int.TryParse(form["progress"].ToString(), out var prog);
        g["progress"]   = prog;
        if (form["status"].ToString() == "Completed") { g["completedOn"] = DateTime.Now.ToString("dd-MMM-yyyy"); g["progress"] = 100; }
        _db.Execute("UPDATE goals SET data=@d WHERE id=@id", new { d=JsonConvert.SerializeObject(g), id });
        TempData["Success"] = "Goal updated!";
        return RedirectToAction("Index");
    }

    // ── Carry Forward pending goals to next week ─────────────
    [HttpPost]
    public IActionResult CarryForward(int weekNo)
    {
        var goals = _db.Query<string>("SELECT data FROM goals WHERE week_no=@w", new { w=weekNo })
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .Where(g => g.GetValueOrDefault("status")?.ToString() is not ("Completed" or "Cancelled")).ToList();
        if (!goals.Any()) return Json(new { ok=false, msg="No pending goals" });

        int nextWk = weekNo + 1;
        var today = DateTime.Today;
        int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        string wkStart = today.AddDays(-diff).ToString("dd-MMM-yyyy");
        string wkEnd   = today.AddDays(-diff+6).ToString("dd-MMM-yyyy");

        foreach (var g in goals)
        {
            var ng = new Dictionary<string,object?>(g)
            {
                ["id"]              = Guid.NewGuid().ToString("N")[..8],
                ["weekNo"]          = nextWk,
                ["weekStart"]       = wkStart,
                ["weekEnd"]         = wkEnd,
                ["status"]          = "Not Started",
                ["progress"]        = 0,
                ["isPending"]       = true,
                ["pendingFromWeek"] = weekNo,
                ["remarks"]         = $"Carried from Week {weekNo}",
            };
            _db.Execute("INSERT INTO goals (id,week_no,data,ts) VALUES (@id,@wk,@data,@ts)",
                new { id=ng["id"], wk=nextWk, data=JsonConvert.SerializeObject(ng), ts=DateTime.Now.ToString("o") });
        }
        return Json(new { ok=true, count=goals.Count, nextWeek=nextWk });
    }

    // ── Export Excel (HTML table, opens in Excel) ────────────
    [HttpGet("/Goals/Export")]
    public IActionResult Export(int? week)
    {
        var allGoals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        int maxWk = allGoals.Any() ? allGoals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        int curWk = week ?? maxWk;
        var goals = allGoals.Where(g => g.GetValueOrDefault("weekNo")?.ToString() == curWk.ToString()).OrderBy(g => g.GetValueOrDefault("title")).ToList();

        int total = goals.Count, comp = goals.Count(g => g.GetValueOrDefault("status")?.ToString() == "Completed"),
            inProg = goals.Count(g => g.GetValueOrDefault("status")?.ToString() == "In Progress"),
            notStart = goals.Count(g => g.GetValueOrDefault("status")?.ToString() == "Not Started"),
            onHold = goals.Count(g => g.GetValueOrDefault("status")?.ToString() == "On Hold");
        int pct = total > 0 ? (int)Math.Round(comp * 100.0 / total) : 0;
        string wkStart = goals.FirstOrDefault()?.GetValueOrDefault("weekStart")?.ToString() ?? "";
        string wkEnd   = goals.FirstOrDefault()?.GetValueOrDefault("weekEnd")?.ToString() ?? "";

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
table{{border-collapse:collapse;width:100%}}
th{{background:#0A192F;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #1e3a5f}}
td{{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}}
.hdr{{background:#0A192F;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px}}
.sub{{background:#1e3a5f;color:#06B6D4;font-size:10px;padding:5px 14px;letter-spacing:1px}}
.wki{{background:#F8FAFC;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0}}
.complete{{background:#F0FDF4}} .inprog{{background:#EFF6FF}} .onhold{{background:#FFFBEB}}
.cancelled{{background:#F9FAFB;color:#9CA3AF}}
.high{{background:#FEE2E2;color:#991B1B;font-weight:bold;text-align:center}}
.medium{{background:#FEF3C7;color:#92400E;font-weight:bold;text-align:center}}
.low{{background:#D1FAE5;color:#065F46;font-weight:bold;text-align:center}}
.pend{{background:#EDE9FE;color:#5B21B6;font-size:9px;font-style:italic}}
.sh{{background:#0A192F;color:#FFF;font-weight:bold;text-align:center;padding:7px}}
.sl{{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}}
.sv{{text-align:center;font-weight:bold;padding:6px}}
.green{{color:#059669}} .blue{{color:#2563EB}} .red{{color:#DC2626}} .amber{{color:#D97706}}
</style></head><body>
<table style='margin-bottom:14px;border:1px solid #0A192F'>
  <tr><td class='hdr'>AMPM FASHIONS PVT. LTD. — IT DEPARTMENT WEEKLY GOAL TRACKER</td></tr>
  <tr><td class='sub'>IT ASSET MANAGEMENT SYSTEM · GENERATED: {DateTime.Now:dd-MMM-yyyy HH:mm}</td></tr>
  <tr><td class='wki'><b>Week No.:</b> {curWk} &nbsp;&nbsp; <b>Week Start:</b> {wkStart} &nbsp;&nbsp; <b>Week End:</b> {wkEnd} &nbsp;&nbsp; <b>Prepared By:</b> Sandeep Kumar Singh Kushwaha — IT System Administrator</td></tr>
</table>
<table>
<thead><tr>
  <th style='width:28px'>S.No.</th>
  <th style='width:220px'>Goal / Task Description</th>
  <th style='width:95px'>Category</th><th style='width:60px'>Priority</th>
  <th style='width:120px'>Department</th><th style='width:85px'>Requested By</th>
  <th style='width:85px'>Assigned To</th><th style='width:75px'>Start Date</th>
  <th style='width:75px'>Target Date</th><th style='width:75px'>Completed</th>
  <th style='width:80px'>Status</th><th style='width:45px'>% Done</th>
  <th style='width:140px'>Remarks</th><th style='width:75px'>Approval</th><th style='width:85px'>Ticket Ref</th>
</tr></thead><tbody>");

        int sno = 0;
        foreach (var g in goals)
        {
            sno++;
            var status = g.GetValueOrDefault("status")?.ToString() ?? "Not Started";
            var priority = g.GetValueOrDefault("priority")?.ToString() ?? "Medium";
            string rowCls = status switch { "Completed"=>"complete","In Progress"=>"inprog","On Hold"=>"onhold","Cancelled"=>"cancelled",_=>"" };
            string prioCls = priority switch { "High"=>"high","Medium"=>"medium","Low"=>"low",_=>"" };
            string statusStyle = status switch { "Completed"=>"color:#059669;font-weight:bold","In Progress"=>"color:#2563EB;font-weight:bold","On Hold"=>"color:#D97706;font-weight:bold","Cancelled"=>"color:#9CA3AF",_=>"color:#DC2626;font-weight:bold" };
            bool isPending = g.GetValueOrDefault("isPending")?.ToString()?.ToLower() == "true";
            string pendNote = isPending ? $"<br><span class='pend'>[Pending from Week {g.GetValueOrDefault("pendingFromWeek")}]</span>" : "";
            int prog = 0; int.TryParse(g.GetValueOrDefault("progress")?.ToString(), out prog);
            sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td><b>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("title")?.ToString())}</b>{pendNote}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("category")}</td>
  <td class='{prioCls}'>{priority}</td>
  <td>{g.GetValueOrDefault("department")}</td>
  <td>{g.GetValueOrDefault("requestedBy")}</td>
  <td>{g.GetValueOrDefault("assignedTo")}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("startDate")}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("targetDate")}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("completedOn")}</td>
  <td style='{statusStyle};text-align:center'>{status}</td>
  <td style='text-align:center'>{prog}%</td>
  <td>{System.Net.WebUtility.HtmlEncode(g.GetValueOrDefault("remarks")?.ToString())}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("approval")}</td>
  <td style='text-align:center'>{g.GetValueOrDefault("ticketRef")}</td>
</tr>");
        }
        sb.Append($@"</tbody></table>
<br>
<table style='width:360px;margin-top:14px;border:1px solid #0A192F'>
  <tr><td colspan='2' class='sh'>WEEK SUMMARY</td></tr>
  <tr><td class='sl'>Total Goals</td><td class='sv'>{total}</td></tr>
  <tr class='complete'><td class='sl'>Completed</td><td class='sv green'>{comp}</td></tr>
  <tr class='inprog'><td class='sl'>In Progress</td><td class='sv blue'>{inProg}</td></tr>
  <tr><td class='sl'>Not Started</td><td class='sv red'>{notStart}</td></tr>
  <tr class='onhold'><td class='sl'>On Hold</td><td class='sv amber'>{onHold}</td></tr>
  <tr style='background:#F0FDF4'><td class='sl'>Overall Completion</td><td class='sv green' style='font-size:13px'>{pct}%</td></tr>
</table>
<br>
<div style='font-size:10px;color:#6B7280;border-top:1px solid #E2E8F0;padding-top:6px'>
  <b>Sandeep Kumar Singh Kushwaha</b> | IT System Administrator | AMPM Fashions Pvt Ltd<br>
  +91 93156 31188 | B-144, Sector 10, Noida - 201301
</div></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_IT_Goals_Week{curWk}_{DateTime.Now:yyyyMMdd}.xls");
    }

    // ── Email Report (mailto link) ────────────────────────────
    [HttpGet("/Goals/EmailReport")]
    public IActionResult EmailReport(int? week)
    {
        var allGoals = _db.Query<string>("SELECT data FROM goals ORDER BY week_no, ts")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
        int maxWk = allGoals.Any() ? allGoals.Max(g => int.TryParse(g.GetValueOrDefault("weekNo")?.ToString(), out var w) ? w : 0) : 1;
        int curWk = week ?? maxWk;
        var goals = allGoals.Where(g => g.GetValueOrDefault("weekNo")?.ToString() == curWk.ToString()).OrderBy(g => g.GetValueOrDefault("title")).ToList();

        int total=goals.Count, comp=goals.Count(g=>g.GetValueOrDefault("status")?.ToString()=="Completed"),
            inProg=goals.Count(g=>g.GetValueOrDefault("status")?.ToString()=="In Progress"),
            notStart=goals.Count(g=>g.GetValueOrDefault("status")?.ToString()=="Not Started"),
            onHold=goals.Count(g=>g.GetValueOrDefault("status")?.ToString()=="On Hold");
        int pct = total>0?(int)Math.Round(comp*100.0/total):0;
        string wkStart = goals.FirstOrDefault()?.GetValueOrDefault("weekStart")?.ToString() ?? "";
        string wkEnd   = goals.FirstOrDefault()?.GetValueOrDefault("weekEnd")?.ToString() ?? "";

        var body = new System.Text.StringBuilder();
        body.AppendLine($"Dear HOD,\r\n");
        body.AppendLine($"Please find below the IT Department Weekly Goals Report for Week {curWk}.\r\n");
        body.AppendLine("══════════════════════════════════════════════════════");
        body.AppendLine($"  AMPM FASHIONS PVT. LTD. — IT WEEKLY GOALS REPORT");
        body.AppendLine($"  Week {curWk}  |  {wkStart} to {wkEnd}");
        body.AppendLine("══════════════════════════════════════════════════════\r\n");
        body.AppendLine("  WEEK SUMMARY");
        body.AppendLine("──────────────────────────────────────────────────────");
        body.AppendLine($"  Total Goals     : {total}");
        body.AppendLine($"  ✅ Completed    : {comp}");
        body.AppendLine($"  🔄 In Progress  : {inProg}");
        body.AppendLine($"  ⏳ Not Started  : {notStart}");
        body.AppendLine($"  ⏸  On Hold      : {onHold}");
        body.AppendLine($"  Overall Progress: {pct}%\r\n");
        body.AppendLine("──────────────────────────────────────────────────────");
        body.AppendLine("  GOAL DETAILS");
        body.AppendLine("──────────────────────────────────────────────────────");
        int sno=0;
        foreach (var g in goals)
        {
            sno++;
            bool isPending = g.GetValueOrDefault("isPending")?.ToString()?.ToLower() == "true";
            string pend = isPending ? $" [⚠ Pending from Wk {g.GetValueOrDefault("pendingFromWeek")}]" : "";
            int prog=0; int.TryParse(g.GetValueOrDefault("progress")?.ToString(), out prog);
            body.AppendLine($"  {sno}. {g.GetValueOrDefault("title")}{pend}");
            body.AppendLine($"     Status: {g.GetValueOrDefault("status")}  |  Priority: {g.GetValueOrDefault("priority")}  |  {prog}%");
            body.AppendLine($"     Dept: {g.GetValueOrDefault("department")}  |  Assigned: {g.GetValueOrDefault("assignedTo")}  |  Target: {g.GetValueOrDefault("targetDate")}");
            string rem = g.GetValueOrDefault("remarks")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(rem)) body.AppendLine($"     Remarks: {rem}");
            body.AppendLine();
        }
        body.AppendLine("══════════════════════════════════════════════════════");
        body.AppendLine("  Sandeep Kumar Singh Kushwaha");
        body.AppendLine("  IT — System Administrator | AMPM Fashions Pvt Ltd");
        body.AppendLine("  +91 93156 31188 | B-144, Sector 10, Noida - 201301");
        body.AppendLine("══════════════════════════════════════════════════════");

        string subject = $"IT Weekly Goals Report — Week {curWk} | {wkStart} to {wkEnd} | {pct}% Complete";
        string mailto = $"mailto:?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body.ToString())}";
        return Redirect(mailto);
    }
}
