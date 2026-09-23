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

    // Original per-sheet CSS for the Morning Checklist report (cyan theme).
    // Used unscoped for the standalone ChecklistExport download, and scoped
    // under #checklistSheet (see ChecklistScopedCss below) when this same
    // sheet body is embedded as the second tab of the combined Todo export.
    const string ChecklistCss = @"
table{border-collapse:collapse;width:100%}
th{background:#0891B2;color:#FFF;padding:7px 5px;text-align:center;font-size:10px;border:1px solid #155E75}
td{padding:5px 6px;border:1px solid #CBD5E1;vertical-align:middle;font-size:10px}
.hdr{background:#0891B2;color:#FFF;font-size:15px;font-weight:bold;padding:10px 14px}
.sub{background:#164E63;color:#A5F3FC;font-size:10px;padding:5px 14px;letter-spacing:1px}
.wki{background:#ECFEFF;padding:7px 14px;font-size:10px;color:#374151;border:1px solid #E2E8F0}
.datehdr{background:#ECFEFF;color:#155E75;font-weight:bold;padding:6px 8px;font-size:11px}
.checked{background:#F0FDF4} .pending{background:#FFFBEB} .issue{background:#FEF2F2}
.high{background:#FEE2E2;color:#991B1B;font-weight:bold;text-align:center}
.medium{background:#FEF3C7;color:#92400E;font-weight:bold;text-align:center}
.low{background:#D1FAE5;color:#065F46;font-weight:bold;text-align:center}
.sh{background:#0891B2;color:#FFF;font-weight:bold;text-align:center;padding:7px}
.sl{background:#F1F5F9;font-weight:bold;color:#374151;padding:6px 10px}
.sv{text-align:center;font-weight:bold;padding:6px}
.green{color:#059669} .amber{color:#D97706} .red{color:#DC2626}
";

    // Builds just the Morning Checklist report's tables (info block + data
    // table + summary table + signature) for a date range — no <html>/<style>
    // wrapper, so it can be dropped either into its own standalone document
    // (ChecklistExport) or embedded as one sheet of a combined workbook
    // (Todos Export, alongside the Daily To-Do sheet, for the same dates).
    string BuildChecklistSheetBody(DateTime fromD, DateTime toD, string fromS, string toS)
    {
        // Only items that were actually clicked (Checked/Pending/Issue Found)
        // get a row in checklist_log — an item nobody touched on a given day
        // has NO log row at all, it just silently defaults to "Pending" in the
        // live Morning Checklist tab. Looping over `logRows` alone (the old
        // approach) therefore skipped every untouched item entirely, which is
        // why Sandy saw the Excel export missing most items' Checked/Pending/
        // Issue Found status. Fixed by looping every ACTIVE item × every date
        // in the range ourselves, defaulting to "Pending" when no log row
        // exists for that item+date — same rule the live tab already uses.
        var items = EnsureChecklistSeed()
            .Where(i => (i.GetValueOrDefault("active")?.ToString() ?? "True") != "False").ToList();
        var logRows = _db.GetChecklistLogRange(fromS, toS);
        var logByDate = logRows
            .GroupBy(r => r.GetValueOrDefault("checkDate")?.ToString() ?? "")
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.GetValueOrDefault("itemId")?.ToString() ?? "", r => r));

        int total = 0, checkedCnt = 0, issueCnt = 0, pendingCnt = 0;

        var sb = new System.Text.StringBuilder();
        sb.Append($@"<table style='margin-bottom:14px;border:1px solid #0891B2'>
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
        for (var day = fromD; day <= toD; day = day.AddDays(1))
        {
            var cdt = day.ToString("yyyy-MM-dd");
            logByDate.TryGetValue(cdt, out var dayLog);
            sb.Append($"<tr><td colspan='10' class='datehdr'>📅 {cdt}</td></tr>");

            foreach (var itm in items)
            {
                var itemId = itm.GetValueOrDefault("id")?.ToString() ?? "";
                Dictionary<string, object?>? r = null;
                dayLog?.TryGetValue(itemId, out r);

                sno++;
                var status = r?.GetValueOrDefault("status")?.ToString() ?? "Pending";
                var priority = itm.GetValueOrDefault("priority")?.ToString() ?? "Medium";
                total++;
                if (status == "Checked") checkedCnt++;
                else if (status == "Issue Found") issueCnt++;
                else pendingCnt++;

                string rowCls = status switch { "Checked" => "checked", "Issue Found" => "issue", _ => "pending" };
                string prioCls = priority switch { "High" => "high", "Medium" => "medium", "Low" => "low", _ => "" };
                string statusStyle = status switch { "Checked" => "color:#059669;font-weight:bold", "Issue Found" => "color:#DC2626;font-weight:bold", _ => "color:#D97706;font-weight:bold" };
                sb.Append($@"<tr class='{rowCls}'>
  <td style='text-align:center'>{sno}</td>
  <td style='text-align:center'>{cdt}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm.GetValueOrDefault("location")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm.GetValueOrDefault("category")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(itm.GetValueOrDefault("task")?.ToString() ?? "")}</td>
  <td class='{prioCls}'>{priority}</td>
  <td style='{statusStyle};text-align:center'>{status}</td>
  <td>{System.Net.WebUtility.HtmlEncode(r?.GetValueOrDefault("note")?.ToString() ?? "")}</td>
  <td>{System.Net.WebUtility.HtmlEncode(r?.GetValueOrDefault("updatedBy")?.ToString() ?? "")}</td>
  <td style='text-align:center'>{r?.GetValueOrDefault("updatedAt")}</td>
</tr>");
            }
        }
        int pct = total > 0 ? (int)Math.Round(checkedCnt * 100.0 / total) : 0;
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
</div>");
        return sb.ToString();
    }

    // ── Checklist Excel Report (standalone download, own tab's "Excel Report" button) ──
    [HttpGet("/Todos/ChecklistExport")]
    public IActionResult ChecklistExport(string? from, string? to)
    {
        var current = _auth.GetCurrentUser(HttpContext);
        if (current == null) return RedirectToAction("Login", "Account");

        DateTime fromD = DateTime.TryParse(from, out var f) ? f.Date : IstTime.Today;
        DateTime toD   = DateTime.TryParse(to, out var tt) ? tt.Date : IstTime.Today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        string fromS = fromD.ToString("yyyy-MM-dd"), toS = toD.ToString("yyyy-MM-dd");

        var body = BuildChecklistSheetBody(fromD, toD, fromS, toS);
        var sb = new System.Text.StringBuilder();
        sb.Append($@"<html><head><meta charset='UTF-8'><style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:12px}}
{ChecklistCss}
</style></head><body>{body}</body></html>");

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

    // ── Combined Todo+Checklist workbook (genuine SpreadsheetML / "Excel 2003
    // XML" format) ───────────────────────────────────────────────────────────
    // The old combined Export() built one HTML document and relied on the
    // <x:ExcelWorkbook><x:ExcelWorksheets> mso-comment convention to split it
    // into two sheet tabs. That convention turned out to NOT be reliably
    // honored by real Excel — Sandy's Excel showed a "Problems During Load"
    // error specifically on the Morning Checklist tab. SpreadsheetML is a
    // genuine, fully-documented Microsoft XML schema (this is literally what
    // Excel itself writes out when you pick "XML Spreadsheet 2003" from Save
    // As) with first-class multi-<Worksheet> support, so it doesn't depend on
    // Excel's much less predictable HTML-to-workbook importer at all.
    static string XEsc(object? s)
    {
        var str = s?.ToString() ?? "";
        return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
    static string XCell(object? text, string? style = null, int mergeAcross = 0)
    {
        var styleAttr = style != null ? $" ss:StyleID='{style}'" : "";
        var mergeAttr = mergeAcross > 0 ? $" ss:MergeAcross='{mergeAcross}'" : "";
        return $"<Cell{styleAttr}{mergeAttr}><Data ss:Type='String'>{XEsc(text)}</Data></Cell>";
    }
    static string XRow(string cells, int? height = null)
    {
        var h = height.HasValue ? $" ss:Height='{height}'" : "";
        return $"<Row{h}>{cells}</Row>";
    }

    // Every style used by both sheets. ss:Parent='Default' means anything not
    // explicitly overridden here (font family/size, mainly) is inherited from
    // the "Default"/"Normal" style below, so we don't have to repeat
    // Font ss:FontName='Arial' on every single one.
    const string XlBorder = "<Borders><Border ss:Position='Bottom' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Left' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Right' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Top' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/></Borders>";
    static readonly string XlStyles = $@"
<Style ss:ID='Default' ss:Name='Normal'><Font ss:FontName='Arial' ss:Size='10'/></Style>
<Style ss:ID='titleTodo' ss:Parent='Default'><Interior ss:Color='#4F46E5' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='14'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='subTodo' ss:Parent='Default'><Interior ss:Color='#312E81' ss:Pattern='Solid'/><Font ss:Color='#A5B4FC' ss:Size='9'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='infoTodo' ss:Parent='Default'><Interior ss:Color='#F5F3FF' ss:Pattern='Solid'/><Font ss:Color='#374151' ss:Size='9'/><Alignment ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='dateHdrTodo' ss:Parent='Default'><Interior ss:Color='#EEF2FF' ss:Pattern='Solid'/><Font ss:Color='#3730A3' ss:Bold='1' ss:Size='10'/><Alignment ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='colHdrTodo' ss:Parent='Default'><Interior ss:Color='#4F46E5' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='titleChecklist' ss:Parent='Default'><Interior ss:Color='#0891B2' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='14'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='subChecklist' ss:Parent='Default'><Interior ss:Color='#164E63' ss:Pattern='Solid'/><Font ss:Color='#A5F3FC' ss:Size='9'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='infoChecklist' ss:Parent='Default'><Interior ss:Color='#ECFEFF' ss:Pattern='Solid'/><Font ss:Color='#374151' ss:Size='9'/><Alignment ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='dateHdrChecklist' ss:Parent='Default'><Interior ss:Color='#ECFEFF' ss:Pattern='Solid'/><Font ss:Color='#155E75' ss:Bold='1' ss:Size='10'/><Alignment ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='colHdrChecklist' ss:Parent='Default'><Interior ss:Color='#0891B2' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='rowDoneC' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='rowDoneL' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Alignment ss:Horizontal='Left' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='rowInprogC' ss:Parent='Default'><Interior ss:Color='#EFF6FF' ss:Pattern='Solid'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='rowInprogL' ss:Parent='Default'><Interior ss:Color='#EFF6FF' ss:Pattern='Solid'/><Alignment ss:Horizontal='Left' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='rowPendingC' ss:Parent='Default'><Interior ss:Color='#FFFBEB' ss:Pattern='Solid'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='rowPendingL' ss:Parent='Default'><Interior ss:Color='#FFFBEB' ss:Pattern='Solid'/><Alignment ss:Horizontal='Left' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='rowIssueC' ss:Parent='Default'><Interior ss:Color='#FEF2F2' ss:Pattern='Solid'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='rowIssueL' ss:Parent='Default'><Interior ss:Color='#FEF2F2' ss:Pattern='Solid'/><Alignment ss:Horizontal='Left' ss:Vertical='Center' ss:WrapText='1'/>{XlBorder}</Style>
<Style ss:ID='pHigh' ss:Parent='Default'><Interior ss:Color='#FEE2E2' ss:Pattern='Solid'/><Font ss:Color='#991B1B' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='pMedium' ss:Parent='Default'><Interior ss:Color='#FEF3C7' ss:Pattern='Solid'/><Font ss:Color='#92400E' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='pLow' ss:Parent='Default'><Interior ss:Color='#D1FAE5' ss:Pattern='Solid'/><Font ss:Color='#065F46' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='statusDone' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Font ss:Color='#059669' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='statusInprog' ss:Parent='Default'><Interior ss:Color='#EFF6FF' ss:Pattern='Solid'/><Font ss:Color='#2563EB' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='statusPending' ss:Parent='Default'><Interior ss:Color='#FFFBEB' ss:Pattern='Solid'/><Font ss:Color='#D97706' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='statusIssue' ss:Parent='Default'><Interior ss:Color='#FEF2F2' ss:Pattern='Solid'/><Font ss:Color='#DC2626' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='vYes' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Font ss:Color='#059669' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='vNo' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Font ss:Color='#DC2626' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumHeaderTodo' ss:Parent='Default'><Interior ss:Color='#4F46E5' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumHeaderChecklist' ss:Parent='Default'><Interior ss:Color='#0891B2' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumLabel' ss:Parent='Default'><Interior ss:Color='#F1F5F9' ss:Pattern='Solid'/><Font ss:Color='#374151' ss:Bold='1'/><Alignment ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumValue' ss:Parent='Default'><Font ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumGreen' ss:Parent='Default'><Interior ss:Color='#F0FDF4' ss:Pattern='Solid'/><Font ss:Color='#059669' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumBlue' ss:Parent='Default'><Font ss:Color='#2563EB' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumAmber' ss:Parent='Default'><Font ss:Color='#D97706' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
<Style ss:ID='sumRed' ss:Parent='Default'><Font ss:Color='#DC2626' ss:Bold='1'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{XlBorder}</Style>
";

    // Builds the <Row>...</Row> XML for the Daily To-Do sheet plus the
    // <Column ss:Width=.../> definitions for its table, for a date range.
    string BuildTodoSheetRowsXml(DateTime fromD, DateTime toD, string fromS, string toS, bool allUsers, string targetUser, out string colDefsXml)
    {
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

        int lastColIdx = (allUsers ? 11 : 10) - 1;
        var widths = allUsers
            ? new[] { 30, 65, 85, 180, 90, 50, 65, 130, 55, 90, 90 }
            : new[] { 30, 65, 180, 90, 50, 65, 130, 55, 90, 90 };
        colDefsXml = string.Concat(widths.Select(w => $"<Column ss:Width='{w}'/>"));

        var sb = new System.Text.StringBuilder();
        sb.Append(XRow(XCell("AMPM FASHIONS PVT. LTD. — DAILY TO-DO / WORK REPORT", "titleTodo", lastColIdx), 22));
        sb.Append(XRow(XCell($"IT ASSET MANAGEMENT SYSTEM · GENERATED: {IstTime.Now:dd-MMM-yyyy HH:mm}", "subTodo", lastColIdx)));
        sb.Append(XRow(XCell($"Period: {fromD:dd-MMM-yyyy} to {toD:dd-MMM-yyyy}    Scope: {scope}    Prepared By: Sandeep Kumar Singh Kushwaha — IT System Administrator", "infoTodo", lastColIdx)));

        var headerCells = new List<string> { XCell("S.No.", "colHdrTodo"), XCell("Date", "colHdrTodo") };
        if (allUsers) headerCells.Add(XCell("Employee", "colHdrTodo"));
        headerCells.Add(XCell("Task / Work Done", "colHdrTodo"));
        headerCells.Add(XCell("Time", "colHdrTodo"));
        headerCells.Add(XCell("Priority", "colHdrTodo"));
        headerCells.Add(XCell("Status", "colHdrTodo"));
        headerCells.Add(XCell("Step / Last Update", "colHdrTodo"));
        headerCells.Add(XCell("Verified", "colHdrTodo"));
        headerCells.Add(XCell("Added At", "colHdrTodo"));
        headerCells.Add(XCell("Completed At", "colHdrTodo"));
        sb.Append(XRow(string.Concat(headerCells), 26));

        int sno = 0;
        string? lastDate = null;
        foreach (var g in rows)
        {
            var td = g.GetValueOrDefault("taskDate")?.ToString() ?? "";
            if (td != lastDate)
            {
                sb.Append(XRow(XCell($"📅 {td}", "dateHdrTodo", lastColIdx)));
                lastDate = td;
            }
            sno++;
            var status = g.GetValueOrDefault("status")?.ToString() ?? "Pending";
            var priority = g.GetValueOrDefault("priority")?.ToString() ?? "Medium";
            string rowC = status switch { "Done" => "rowDoneC", "In Progress" => "rowInprogC", _ => "rowPendingC" };
            string rowL = status switch { "Done" => "rowDoneL", "In Progress" => "rowInprogL", _ => "rowPendingL" };
            string prioStyle = priority switch { "High" => "pHigh", "Medium" => "pMedium", "Low" => "pLow", _ => rowC };
            string statusStyle = status switch { "Done" => "statusDone", "In Progress" => "statusInprog", _ => "statusPending" };
            var st1 = g.GetValueOrDefault("startTime")?.ToString(); var et1 = g.GetValueOrDefault("endTime")?.ToString();
            string timeRange = (!string.IsNullOrEmpty(st1) || !string.IsNullOrEmpty(et1)) ? $"{st1} - {et1}" : "";
            string stepNote = status switch {
                "Done" => g.GetValueOrDefault("lastStepNote")?.ToString() ?? "",
                "In Progress" => g.GetValueOrDefault("progressNote")?.ToString() ?? "",
                _ => ""
            };
            string verifiedVal = status == "Done" ? (g.GetValueOrDefault("verified")?.ToString() ?? "No") : "";
            string verifiedStyle = verifiedVal == "Yes" ? "vYes" : (verifiedVal == "No" ? "vNo" : rowC);

            var cells = new List<string> { XCell(sno, rowC), XCell(td, rowC) };
            if (allUsers) cells.Add(XCell(g.GetValueOrDefault("userName")?.ToString() ?? "", rowL));
            cells.Add(XCell(g.GetValueOrDefault("task")?.ToString() ?? "", rowL));
            cells.Add(XCell(timeRange, rowC));
            cells.Add(XCell(priority, prioStyle));
            cells.Add(XCell(status, statusStyle));
            cells.Add(XCell(stepNote, rowL));
            cells.Add(XCell(verifiedVal, verifiedStyle));
            cells.Add(XCell(g.GetValueOrDefault("createdAt")?.ToString() ?? "", rowC));
            cells.Add(XCell(g.GetValueOrDefault("completedAt")?.ToString() ?? "", rowC));
            sb.Append(XRow(string.Concat(cells)));
        }

        sb.Append(XRow(XCell("", null, lastColIdx)));
        sb.Append(XRow(XCell("REPORT SUMMARY", "sumHeaderTodo", 1)));
        sb.Append(XRow(XCell("Total Tasks", "sumLabel") + XCell(total, "sumValue")));
        sb.Append(XRow(XCell("Done", "sumLabel") + XCell(done, "sumGreen")));
        sb.Append(XRow(XCell("In Progress", "sumLabel") + XCell(inprog, "sumBlue")));
        sb.Append(XRow(XCell("Pending", "sumLabel") + XCell(pending, "sumAmber")));
        sb.Append(XRow(XCell("Completion Rate", "sumLabel") + XCell($"{pct}%", "sumGreen")));

        return sb.ToString();
    }

    // Builds the <Row>...</Row> XML for the Morning Checklist sheet plus its
    // <Column ss:Width=.../> definitions, for a date range — mirrors
    // BuildChecklistSheetBody's data logic (every active item × every date,
    // defaulting untouched items to "Pending") but emits SpreadsheetML cells
    // instead of HTML <td>s.
    string BuildChecklistSheetRowsXml(DateTime fromD, DateTime toD, string fromS, string toS, out string colDefsXml)
    {
        var items = EnsureChecklistSeed()
            .Where(i => (i.GetValueOrDefault("active")?.ToString() ?? "True") != "False").ToList();
        var logRows = _db.GetChecklistLogRange(fromS, toS);
        var logByDate = logRows
            .GroupBy(r => r.GetValueOrDefault("checkDate")?.ToString() ?? "")
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.GetValueOrDefault("itemId")?.ToString() ?? "", r => r));

        int total = 0, checkedCnt = 0, issueCnt = 0, pendingCnt = 0;
        const int lastColIdx = 9; // 10 columns: S.No, Date, Location, Category, Task, Priority, Status, Note, Updated By, Updated At

        var widths = new[] { 30, 65, 85, 120, 120, 50, 65, 130, 90, 90 };
        colDefsXml = string.Concat(widths.Select(w => $"<Column ss:Width='{w}'/>"));

        var sb = new System.Text.StringBuilder();
        sb.Append(XRow(XCell("AMPM FASHIONS PVT. LTD. — MORNING IT CHECKLIST REPORT", "titleChecklist", lastColIdx), 22));
        sb.Append(XRow(XCell($"IT ASSET MANAGEMENT SYSTEM · GENERATED: {IstTime.Now:dd-MMM-yyyy HH:mm}", "subChecklist", lastColIdx)));
        sb.Append(XRow(XCell($"Period: {fromD:dd-MMM-yyyy} to {toD:dd-MMM-yyyy}    Prepared By: Sandeep Kumar Singh Kushwaha — IT System Administrator", "infoChecklist", lastColIdx)));

        sb.Append(XRow(
            XCell("S.No.", "colHdrChecklist") + XCell("Date", "colHdrChecklist") + XCell("Location", "colHdrChecklist") +
            XCell("Category", "colHdrChecklist") + XCell("Task", "colHdrChecklist") + XCell("Priority", "colHdrChecklist") +
            XCell("Status", "colHdrChecklist") + XCell("Note", "colHdrChecklist") + XCell("Updated By", "colHdrChecklist") +
            XCell("Updated At", "colHdrChecklist"), 26));

        int sno = 0;
        for (var day = fromD; day <= toD; day = day.AddDays(1))
        {
            var cdt = day.ToString("yyyy-MM-dd");
            logByDate.TryGetValue(cdt, out var dayLog);
            sb.Append(XRow(XCell($"📅 {cdt}", "dateHdrChecklist", lastColIdx)));

            foreach (var itm in items)
            {
                var itemId = itm.GetValueOrDefault("id")?.ToString() ?? "";
                Dictionary<string, object?>? r = null;
                dayLog?.TryGetValue(itemId, out r);

                sno++;
                var status = r?.GetValueOrDefault("status")?.ToString() ?? "Pending";
                var priority = itm.GetValueOrDefault("priority")?.ToString() ?? "Medium";
                total++;
                if (status == "Checked") checkedCnt++;
                else if (status == "Issue Found") issueCnt++;
                else pendingCnt++;

                string rowC = status switch { "Checked" => "rowDoneC", "Issue Found" => "rowIssueC", _ => "rowPendingC" };
                string rowL = status switch { "Checked" => "rowDoneL", "Issue Found" => "rowIssueL", _ => "rowPendingL" };
                string prioStyle = priority switch { "High" => "pHigh", "Medium" => "pMedium", "Low" => "pLow", _ => rowC };
                string statusStyle = status switch { "Checked" => "statusDone", "Issue Found" => "statusIssue", _ => "statusPending" };

                var cells = new List<string>
                {
                    XCell(sno, rowC),
                    XCell(cdt, rowC),
                    XCell(itm.GetValueOrDefault("location")?.ToString() ?? "", rowL),
                    XCell(itm.GetValueOrDefault("category")?.ToString() ?? "", rowL),
                    XCell(itm.GetValueOrDefault("task")?.ToString() ?? "", rowL),
                    XCell(priority, prioStyle),
                    XCell(status, statusStyle),
                    XCell(r?.GetValueOrDefault("note")?.ToString() ?? "", rowL),
                    XCell(r?.GetValueOrDefault("updatedBy")?.ToString() ?? "", rowL),
                    XCell(r?.GetValueOrDefault("updatedAt")?.ToString() ?? "", rowC),
                };
                sb.Append(XRow(string.Concat(cells)));
            }
        }
        int pct = total > 0 ? (int)Math.Round(checkedCnt * 100.0 / total) : 0;

        sb.Append(XRow(XCell("", null, lastColIdx)));
        sb.Append(XRow(XCell("REPORT SUMMARY", "sumHeaderChecklist", 1)));
        sb.Append(XRow(XCell("Total Entries", "sumLabel") + XCell(total, "sumValue")));
        sb.Append(XRow(XCell("Checked", "sumLabel") + XCell(checkedCnt, "sumGreen")));
        sb.Append(XRow(XCell("Pending", "sumLabel") + XCell(pendingCnt, "sumAmber")));
        sb.Append(XRow(XCell("Issue Found", "sumLabel") + XCell(issueCnt, "sumRed")));
        sb.Append(XRow(XCell("Checked Rate", "sumLabel") + XCell($"{pct}%", "sumGreen")));

        return sb.ToString();
    }

    // ── Colorful Excel Report (genuine SpreadsheetML workbook) ──────────────
    // One .xls download with TWO real sheet tabs: "Daily To-Do" (this
    // module's own list) and "Morning Checklist" (the recurring IT checks
    // tab), both for the same date range Sandy picked here — so downloading
    // the Todo list always brings that day's Morning Checklist along on its
    // own sheet. Uses real SpreadsheetML <Worksheet> elements (see comment
    // above XlStyles) rather than the old HTML mso-comment trick, which
    // Excel was failing to load correctly.
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

        string todoRows      = BuildTodoSheetRowsXml(fromD, toD, fromS, toS, allUsers, targetUser, out var todoCols);
        string checklistRows = BuildChecklistSheetRowsXml(fromD, toD, fromS, toS, out var checklistCols);

        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version='1.0'?>\n");
        sb.Append("<?mso-application progid='Excel.Sheet'?>\n");
        sb.Append(@"<Workbook xmlns='urn:schemas-microsoft-com:office:spreadsheet'
 xmlns:o='urn:schemas-microsoft-com:office:office'
 xmlns:x='urn:schemas-microsoft-com:office:excel'
 xmlns:ss='urn:schemas-microsoft-com:office:spreadsheet'>
<Styles>");
        sb.Append(XlStyles);
        sb.Append("</Styles>\n");
        sb.Append($"<Worksheet ss:Name='Daily To-Do'><Table ss:DefaultColumnWidth='60'>{todoCols}{todoRows}</Table></Worksheet>\n");
        sb.Append($"<Worksheet ss:Name='Morning Checklist'><Table ss:DefaultColumnWidth='60'>{checklistCols}{checklistRows}</Table></Worksheet>\n");
        sb.Append("</Workbook>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/vnd.ms-excel", $"AMPM_Todo_Report_{fromD:yyyyMMdd}_{toD:yyyyMMdd}.xls");
    }
}
