using Microsoft.AspNetCore.Mvc;
using Npgsql;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

// Print Monitor: every PC runs AMPM_Print_Agent.ps1 which reads the Windows print
// history (PrintService/Operational, event 307) and posts it to ReportPrints.
// The page (Monitor -> Print Log) and the Excel download are here too.
// Same "Endpoints" module permission as the rest of Monitor.
public partial class EndpointsController
{
    static volatile bool _printTableReady;
    static readonly object _printLock = new();

    void EnsurePrintTable()
    {
        if (_printTableReady) return;
        lock (_printLock)
        {
            if (_printTableReady) return;
            _db.Execute(@"CREATE TABLE IF NOT EXISTS print_jobs (
                event_key TEXT PRIMARY KEY,
                pc TEXT, ip TEXT, user_name TEXT, printer TEXT, document TEXT, port TEXT,
                pages INTEGER, bytes BIGINT, printed_at TEXT, received_at TEXT)");
            _db.Execute("CREATE INDEX IF NOT EXISTS print_jobs_printed_at_idx ON print_jobs (printed_at)");
            _printTableReady = true;
        }
    }

    // ---- agent -> website ---------------------------------------------------
    // No cookie session (see the exemption in Filters/ModulePermissionFilter.cs);
    // the shared agent key is checked instead. All values arrive as text.
    [HttpPost("/api/endpoints/report-prints")]
    public IActionResult ReportPrints([FromBody] PrintReportDto data)
    {
        if (data == null || !KeyOk(data.Key, AgentKey))
            return Unauthorized(new { ok = false, error = "Invalid or missing key" });

        try { EnsurePrintTable(); }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "db: " + ex.Message }); }

        static string Cut(string? v, int n) { v = (v ?? "").Trim(); return v.Length > n ? v.Substring(0, n) : v; }

        var pc = Cut(data.Hostname, 100);
        if (pc.Length == 0) return BadRequest(new { ok = false, error = "hostname missing" });
        var ip = Cut(data.Ip, 60);
        var received = IstTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        int added = 0, dup = 0, bad = 0;

        var jobs = (data.Jobs ?? new List<PrintJobDto>()).Take(500).ToList();
        if (jobs.Count > 0)
        {
            using var conn = _db.GetConn();
            using var tx = conn.BeginTransaction();
            foreach (var j in jobs)
            {
                if (!DateTime.TryParseExact((j.Time ?? "").Trim(), "yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when))
                { bad++; continue; }
                int.TryParse((j.Pages ?? "").Trim(), out var pages);
                long.TryParse((j.Bytes ?? "").Trim(), out var bytes);
                if (pages < 0) pages = 0; if (pages > 100000) pages = 100000;
                if (bytes < 0) bytes = 0;
                var key = pc + "|" + Cut(j.RecordId, 30) + "|" + when.ToString("yyyyMMddHHmmss");

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO print_jobs (event_key, pc, ip, user_name, printer, document, port, pages, bytes, printed_at, received_at)
                    VALUES (@k,@pc,@ip,@u,@pr,@d,@po,@pg,@b,@t,@r) ON CONFLICT (event_key) DO NOTHING";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@pc", pc);
                cmd.Parameters.AddWithValue("@ip", ip);
                cmd.Parameters.AddWithValue("@u", Cut(j.User, 120));
                cmd.Parameters.AddWithValue("@pr", Cut(j.Printer, 200));
                cmd.Parameters.AddWithValue("@d", Cut(j.Document, 300));
                cmd.Parameters.AddWithValue("@po", Cut(j.Port, 100));
                cmd.Parameters.AddWithValue("@pg", pages);
                cmd.Parameters.AddWithValue("@b", bytes);
                cmd.Parameters.AddWithValue("@t", when.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@r", received);
                if (cmd.ExecuteNonQuery() > 0) added++; else dup++;
            }
            tx.Commit();
        }

        // heartbeat: which PCs have the print agent and when it last reported
        try
        {
            var agents = _db.KGetObj<Dictionary<string, Dictionary<string, string>>>("print_agents") ?? new();
            agents[pc] = new Dictionary<string, string>
            {
                ["ip"] = ip, ["lastSeen"] = received, ["version"] = Cut(data.AgentVersion, 20)
            };
            _db.KSet("print_agents", agents);
        }
        catch { /* heartbeat is best-effort */ }

        return Ok(new { ok = true, added, duplicates = dup, rejected = bad });
    }

    // ---- web page -----------------------------------------------------------
    sealed class PrintRow
    {
        public string Pc = "", User = "", Printer = "", Document = "", Port = "", When = "";
        public int Pages; public long Bytes;
    }

    // Reads the filtered rows. Dates are text (yyyy-MM-dd HH:mm:ss, India time), so
    // plain string comparison is a correct date comparison.
    List<PrintRow> LoadPrints(string? dateFrom, string? dateTo, string? user, string? printer, string? pc, string? q, int limit)
    {
        EnsurePrintTable();
        var conds = new List<string>();
        using var conn = _db.GetConn();
        using var cmd = conn.CreateCommand();
        if (DateTime.TryParse(dateFrom, out var f)) { conds.Add("printed_at >= @f"); cmd.Parameters.AddWithValue("@f", f.ToString("yyyy-MM-dd") + " 00:00:00"); }
        if (DateTime.TryParse(dateTo, out var t)) { conds.Add("printed_at < @t"); cmd.Parameters.AddWithValue("@t", t.AddDays(1).ToString("yyyy-MM-dd") + " 00:00:00"); }
        if (!string.IsNullOrWhiteSpace(user)) { conds.Add("user_name = @u"); cmd.Parameters.AddWithValue("@u", user.Trim()); }
        if (!string.IsNullOrWhiteSpace(printer)) { conds.Add("printer = @p"); cmd.Parameters.AddWithValue("@p", printer.Trim()); }
        if (!string.IsNullOrWhiteSpace(pc)) { conds.Add("pc = @c"); cmd.Parameters.AddWithValue("@c", pc.Trim()); }
        if (!string.IsNullOrWhiteSpace(q)) { conds.Add("document ILIKE @q"); cmd.Parameters.AddWithValue("@q", "%" + q.Trim() + "%"); }
        cmd.CommandText = "SELECT pc, user_name, printer, document, port, pages, bytes, printed_at FROM print_jobs"
            + (conds.Count > 0 ? " WHERE " + string.Join(" AND ", conds) : "")
            + " ORDER BY printed_at DESC" + (limit > 0 ? " LIMIT " + limit : "");
        var list = new List<PrintRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PrintRow
            {
                Pc = r.IsDBNull(0) ? "" : r.GetString(0),
                User = r.IsDBNull(1) ? "" : r.GetString(1),
                Printer = r.IsDBNull(2) ? "" : r.GetString(2),
                Document = r.IsDBNull(3) ? "" : r.GetString(3),
                Port = r.IsDBNull(4) ? "" : r.GetString(4),
                Pages = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                Bytes = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                When = r.IsDBNull(7) ? "" : r.GetString(7),
            });
        }
        return list;
    }

    List<string> DistinctPrint(string col)
    {
        EnsurePrintTable();
        // col is one of our own constants, never user input
        return _db.Query<string>($"SELECT DISTINCT {col} FROM print_jobs WHERE {col} <> '' ORDER BY {col}");
    }

    [HttpGet("/Endpoints/PrintLog")]
    public IActionResult PrintLog([FromQuery(Name = "from")] string? dateFrom, [FromQuery(Name = "to")] string? dateTo, string? user, string? printer, string? pc, string? q)
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        // default range: this month so far
        if (string.IsNullOrWhiteSpace(dateFrom) && string.IsNullOrWhiteSpace(dateTo))
        {
            var today = IstTime.Today;
            dateFrom = new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd");
            dateTo = today.ToString("yyyy-MM-dd");
        }
        var all = LoadPrints(dateFrom, dateTo, user, printer, pc, q, 0);
        ViewBag.From = dateFrom; ViewBag.To = dateTo; ViewBag.FUser = user ?? ""; ViewBag.FPrinter = printer ?? "";
        ViewBag.FPc = pc ?? ""; ViewBag.FQ = q ?? "";
        ViewBag.Users = DistinctPrint("user_name");
        ViewBag.Printers = DistinctPrint("printer");
        ViewBag.Pcs = DistinctPrint("pc");
        ViewBag.TotalJobs = all.Count;
        ViewBag.TotalPages = all.Sum(x => (long)x.Pages);
        ViewBag.ByUser = all.GroupBy(x => x.User)
            .Select(g => new object[] { g.Key, g.Count(), g.Sum(x => (long)x.Pages), g.Max(x => x.When) })
            .OrderByDescending(a => (long)a[2]).ToList();
        ViewBag.ByPrinter = all.GroupBy(x => x.Printer)
            .Select(g => new object[] { g.Key, g.Count(), g.Sum(x => (long)x.Pages), g.Select(x => x.User).Distinct().Count() })
            .OrderByDescending(a => (long)a[2]).ToList();
        ViewBag.Rows = all.Take(500).Select(x => new object[] { x.When, x.User, x.Pc, x.Printer, x.Document, x.Pages, x.Bytes }).ToList();
        ViewBag.Shown = Math.Min(500, all.Count);
        var agents = _db.KGetObj<Dictionary<string, Dictionary<string, string>>>("print_agents") ?? new();
        ViewBag.Agents = agents.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return View("PrintLog");
    }

    // ---- Excel download -----------------------------------------------------
    // Genuine SpreadsheetML 2003 workbook (opens in Excel), same look as the Assets export.
    [HttpGet("/Endpoints/PrintLogExport")]
    public IActionResult PrintLogExport([FromQuery(Name = "from")] string? dateFrom, [FromQuery(Name = "to")] string? dateTo, string? user, string? printer, string? pc, string? q)
    {
        var all = LoadPrints(dateFrom, dateTo, user, printer, pc, q, 200000);
        string range = (string.IsNullOrWhiteSpace(dateFrom) ? "start" : dateFrom) + " to " + (string.IsNullOrWhiteSpace(dateTo) ? "today" : dateTo);
        string Kb(long b) => b <= 0 ? "" : Math.Max(1, (long)Math.Round(b / 1024.0)).ToString();

        var detHeads = new[] { "#", "Date", "Time", "User", "PC", "Printer", "Document", "Pages", "Size (KB)" };
        var detW = new[] { 30, 75, 60, 110, 110, 190, 300, 50, 65 };
        var detRows = new List<string[]>();
        foreach (var x in all.OrderBy(a => a.When, StringComparer.Ordinal))
        {
            var dt = DateTime.TryParse(x.When, out var d) ? d : (DateTime?)null;
            detRows.Add(new[] { (detRows.Count + 1).ToString(), dt?.ToString("dd-MMM-yyyy") ?? x.When, dt?.ToString("hh:mm tt") ?? "",
                x.User, x.Pc, x.Printer, x.Document, x.Pages.ToString(), Kb(x.Bytes) });
        }

        var uHeads = new[] { "#", "User", "Print jobs", "Total pages", "Printers used", "First print", "Last print" };
        var uW = new[] { 30, 140, 70, 80, 220, 120, 120 };
        var uRows = new List<string[]>();
        foreach (var g in all.GroupBy(x => x.User).OrderByDescending(g => g.Sum(x => (long)x.Pages)))
            uRows.Add(new[] { (uRows.Count + 1).ToString(), g.Key, g.Count().ToString(), g.Sum(x => (long)x.Pages).ToString(),
                string.Join(", ", g.Select(x => x.Printer).Distinct().OrderBy(s => s)), Dt(g.Min(x => x.When)), Dt(g.Max(x => x.When)) });

        var upHeads = new[] { "#", "User", "Printer", "Print jobs", "Total pages" };
        var upW = new[] { 30, 140, 230, 70, 80 };
        var upRows = new List<string[]>();
        foreach (var g in all.GroupBy(x => new { x.User, x.Printer }).OrderBy(g => g.Key.User, StringComparer.OrdinalIgnoreCase).ThenByDescending(g => g.Sum(x => (long)x.Pages)))
            upRows.Add(new[] { (upRows.Count + 1).ToString(), g.Key.User, g.Key.Printer, g.Count().ToString(), g.Sum(x => (long)x.Pages).ToString() });

        var dHeads = new[] { "#", "Date", "User", "Printer", "Print jobs", "Total pages" };
        var dW = new[] { 30, 90, 140, 230, 70, 80 };
        var dRows = new List<string[]>();
        foreach (var g in all.GroupBy(x => new { Day = x.When.Length >= 10 ? x.When.Substring(0, 10) : x.When, x.User, x.Printer })
                             .OrderBy(g => g.Key.Day, StringComparer.Ordinal).ThenBy(g => g.Key.User, StringComparer.OrdinalIgnoreCase))
        {
            var day = DateTime.TryParse(g.Key.Day, out var dd) ? dd.ToString("dd-MMM-yyyy") : g.Key.Day;
            dRows.Add(new[] { (dRows.Count + 1).ToString(), day, g.Key.User, g.Key.Printer, g.Count().ToString(), g.Sum(x => (long)x.Pages).ToString() });
        }

        var pHeads = new[] { "#", "Printer", "Print jobs", "Total pages", "Users" };
        var pW = new[] { 30, 240, 70, 80, 60 };
        var pRows = new List<string[]>();
        foreach (var g in all.GroupBy(x => x.Printer).OrderByDescending(g => g.Sum(x => (long)x.Pages)))
            pRows.Add(new[] { (pRows.Count + 1).ToString(), g.Key, g.Count().ToString(), g.Sum(x => (long)x.Pages).ToString(), g.Select(x => x.User).Distinct().Count().ToString() });

        var cHeads = new[] { "#", "PC", "Print jobs", "Total pages", "Last print" };
        var cW = new[] { 30, 140, 70, 80, 120 };
        var cRows = new List<string[]>();
        foreach (var g in all.GroupBy(x => x.Pc).OrderByDescending(g => g.Sum(x => (long)x.Pages)))
            cRows.Add(new[] { (cRows.Count + 1).ToString(), g.Key, g.Count().ToString(), g.Sum(x => (long)x.Pages).ToString(), Dt(g.Max(x => x.When)) });

        string title = "AMPM FASHIONS PVT. LTD. — PRINT LOG (" + range + ")";
        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version='1.0'?>\n<?mso-application progid='Excel.Sheet'?>\n");
        sb.Append(@"<Workbook xmlns='urn:schemas-microsoft-com:office:spreadsheet'
 xmlns:o='urn:schemas-microsoft-com:office:office'
 xmlns:x='urn:schemas-microsoft-com:office:excel'
 xmlns:ss='urn:schemas-microsoft-com:office:spreadsheet'>
<Styles>");
        sb.Append(AssetXlStylesShared);
        sb.Append("</Styles>\n");
        sb.Append(AssetSheetShared("Print Details", title + " — every print job", detHeads, detW, detRows));
        sb.Append(AssetSheetShared("By User", title + " — by user", uHeads, uW, uRows));
        sb.Append(AssetSheetShared("User x Printer", title + " — which user printed on which printer", upHeads, upW, upRows));
        sb.Append(AssetSheetShared("Day wise", title + " — day wise", dHeads, dW, dRows));
        sb.Append(AssetSheetShared("By Printer", title + " — by printer", pHeads, pW, pRows));
        sb.Append(AssetSheetShared("By PC", title + " — by PC", cHeads, cW, cRows));
        sb.Append("</Workbook>");

        Response.Headers["Cache-Control"] = "no-store";
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.ms-excel", $"AMPM_PrintLog_{IstTime.Now:yyyyMMdd_HHmm}.xls");

        static string Dt(string s) => DateTime.TryParse(s, out var v) ? v.ToString("dd-MMM-yyyy hh:mm tt") : s;
    }

    // Small self-contained SpreadsheetML helpers (the Assets export has its own private ones).
    static string PX(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch) { case '&': sb.Append("&amp;"); break; case '<': sb.Append("&lt;"); break; case '>': sb.Append("&gt;"); break; default: sb.Append(ch); break; }
        }
        return sb.ToString();
    }

    static string PCell(string? text, string style, int mergeAcross = 0, bool number = false)
    {
        var m = mergeAcross > 0 ? $" ss:MergeAcross='{mergeAcross}'" : "";
        var isNum = number && !string.IsNullOrEmpty(text) && long.TryParse(text, out _);
        return $"<Cell ss:StyleID='{style}'{m}><Data ss:Type='{(isNum ? "Number" : "String")}'>{PX(text)}</Data></Cell>";
    }

    static string AssetSheetShared(string name, string title, string[] heads, int[] widths, List<string[]> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"<Worksheet ss:Name='{PX(name)}'><Table ss:DefaultColumnWidth='70'>");
        foreach (var w in widths) sb.Append($"<Column ss:Width='{w}'/>");
        sb.Append($"<Row ss:Height='26'>{PCell(title, "aTitle", heads.Length - 1)}</Row>");
        sb.Append($"<Row>{PCell($"Generated: {IstTime.Now:dd-MMM-yyyy HH:mm} | IT Admin: Sandeep Kumar Singh Kushwaha | Rows: {rows.Count}", "aInfo", heads.Length - 1)}</Row>");
        sb.Append("<Row ss:Height='22'>");
        foreach (var h in heads) sb.Append(PCell(h, "aHead"));
        sb.Append("</Row>");
        foreach (var r in rows)
        {
            sb.Append("<Row>");
            for (int c = 0; c < r.Length; c++)
            {
                // # column centred; "Pages", "Print jobs", "Total pages", "Users", "Size (KB)" numeric
                var h = heads[c];
                bool numeric = h == "Pages" || h == "Print jobs" || h == "Total pages" || h == "Users" || h == "Size (KB)";
                string st = c == 0 ? "aCenter" : numeric ? "aCenter" : (h == "User" || h == "Printer" || h == "PC") ? "aBold" : "aCell";
                sb.Append(PCell(r[c], st, 0, numeric));
            }
            sb.Append("</Row>");
        }
        if (rows.Count == 0)
            sb.Append($"<Row>{PCell("No prints recorded for this selection.", "aCell", Math.Min(3, heads.Length - 1))}</Row>");
        sb.Append("</Table></Worksheet>\n");
        return sb.ToString();
    }

    const string PBorder = "<Borders><Border ss:Position='Bottom' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Left' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Right' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/><Border ss:Position='Top' ss:LineStyle='Continuous' ss:Weight='1' ss:Color='#CBD5E1'/></Borders>";
    static readonly string AssetXlStylesShared = $@"
<Style ss:ID='Default' ss:Name='Normal'><Font ss:FontName='Arial' ss:Size='10'/></Style>
<Style ss:ID='aTitle' ss:Parent='Default'><Interior ss:Color='#0A192F' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='14'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='aInfo' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Vertical='Center'/></Style>
<Style ss:ID='aHead' ss:Parent='Default'><Interior ss:Color='#0A192F' ss:Pattern='Solid'/><Font ss:Color='#FFFFFF' ss:Bold='1' ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center' ss:WrapText='1'/>{PBorder}</Style>
<Style ss:ID='aCell' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Vertical='Center'/>{PBorder}</Style>
<Style ss:ID='aCenter' ss:Parent='Default'><Font ss:Size='9'/><Alignment ss:Horizontal='Center' ss:Vertical='Center'/>{PBorder}</Style>
<Style ss:ID='aBold' ss:Parent='Default'><Font ss:Size='9' ss:Bold='1'/><Alignment ss:Vertical='Center'/>{PBorder}</Style>
";
}

// JSON bodies from the print agent. Everything is text (like PrinterReportDto).
public class PrintReportDto
{
    public string? Key { get; set; }
    public string? Hostname { get; set; }
    public string? Ip { get; set; }
    public string? AgentVersion { get; set; }
    public List<PrintJobDto>? Jobs { get; set; }
}

public class PrintJobDto
{
    public string? RecordId { get; set; }
    public string? Time { get; set; }
    public string? User { get; set; }
    public string? Client { get; set; }
    public string? Printer { get; set; }
    public string? Port { get; set; }
    public string? Document { get; set; }
    public string? Bytes { get; set; }
    public string? Pages { get; set; }
}
