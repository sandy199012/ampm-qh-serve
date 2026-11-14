using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

// Software Deploy: push an installer (.exe / .msi on Windows, .pkg on Mac) or an update of the
// AMPM agents to the company PCs from the website.
//
//   * Admin pages (/Deploy) - superadmin only: create a task, choose the targets, watch the result per PC.
//   * Each PC runs the small "Deploy Agent" (wwwroot/tools/AMPM_Deploy_Agent.ps1 / AMPM_Mac_Deploy_Agent.sh),
//     which asks /api/deploy/poll every 5 minutes, downloads what is waiting for it, checks the SHA-256,
//     runs it with administrator rights and reports the result to /api/deploy/result.
//   * The /api/deploy/* calls have no login cookie; they use the shared agent key
//     (see the exemption in Filters/ModulePermissionFilter.cs).
//
// Safety: only a superadmin can create tasks; every task has an expiry (default 7 days) so a new PC
// never receives an old task; the agent refuses a download whose SHA-256 does not match; nothing
// runs twice on the same PC (one result row per task + PC).
public class DeployController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public DeployController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    const long MaxFileBytes = 200L * 1024 * 1024;   // per installer
    const int ChunkBytes = 1024 * 1024;              // files are stored in 1 MB pieces (small memory use)

    static volatile bool _ready;
    static readonly object _lock = new();

    void Ensure()
    {
        if (_ready) return;
        lock (_lock)
        {
            if (_ready) return;
            _db.Execute(@"CREATE TABLE IF NOT EXISTS deploy_tasks (
                id BIGSERIAL PRIMARY KEY, name TEXT, os TEXT, kind TEXT, file_name TEXT, file_size BIGINT, sha256 TEXT,
                args TEXT, agent_type TEXT, target_mode TEXT, target_value TEXT, status TEXT,
                created_by TEXT, created_at TEXT, expires_at TEXT)");
            _db.Execute(@"CREATE TABLE IF NOT EXISTS deploy_files (
                task_id BIGINT NOT NULL, seq INTEGER NOT NULL, data BYTEA, PRIMARY KEY (task_id, seq))");
            _db.Execute(@"CREATE TABLE IF NOT EXISTS deploy_results (
                task_id BIGINT NOT NULL, host TEXT NOT NULL, status TEXT, exit_code INTEGER, output TEXT,
                started_at TEXT, finished_at TEXT, PRIMARY KEY (task_id, host))");
            _db.Execute(@"CREATE TABLE IF NOT EXISTS deploy_hosts (
                host TEXT PRIMARY KEY, ip TEXT, os TEXT, version TEXT, user_name TEXT, last_seen TEXT)");
            _ready = true;
        }
    }

    static string Now() => IstTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    static string Cut(string? v, int n) { v = (v ?? "").Trim(); return v.Length > n ? v.Substring(0, n) : v; }
    static string Clean(string? v, int n)
    {
        var s = new string((v ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length > n ? s.Substring(0, n) : s;
    }

    static bool KeyOk(string? given)
    {
        var a = Encoding.UTF8.GetBytes(given ?? "");
        var b = Encoding.UTF8.GetBytes(EndpointsController.CurrentAgentKey);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }

    UserSession? Admin()
    {
        var u = _auth.GetCurrentUser(HttpContext);
        return (u != null && u.IsSuperAdmin) ? u : null;
    }

    // ------------------------------------------------------------------ data helpers
    sealed class TaskRow
    {
        public long Id, Size;
        public string Name = "", Os = "", Kind = "", FileName = "", Sha = "", Args = "", AgentType = "",
            Mode = "", Value = "", Status = "", CreatedBy = "", CreatedAt = "", Expires = "";
    }

    static string S(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    List<TaskRow> LoadTasks(string whereSql, string? p1Name = null, object? p1Val = null)
    {
        var list = new List<TaskRow>();
        using var conn = _db.GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, os, kind, file_name, file_size, sha256, args, agent_type, target_mode, target_value, status, created_by, created_at, expires_at FROM deploy_tasks "
            + whereSql + " ORDER BY id DESC";
        if (p1Name != null) cmd.Parameters.AddWithValue(p1Name, p1Val ?? "");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TaskRow
            {
                Id = r.GetInt64(0), Name = S(r, 1), Os = S(r, 2), Kind = S(r, 3), FileName = S(r, 4),
                Size = r.IsDBNull(5) ? 0 : r.GetInt64(5), Sha = S(r, 6), Args = S(r, 7), AgentType = S(r, 8),
                Mode = S(r, 9), Value = S(r, 10), Status = S(r, 11), CreatedBy = S(r, 12), CreatedAt = S(r, 13), Expires = S(r, 14)
            });
        }
        return list;
    }

    sealed class ResultRow { public string Host = "", Status = "", Output = "", Started = "", Finished = ""; public int Code; }

    List<ResultRow> LoadResults(long taskId)
    {
        var list = new List<ResultRow>();
        using var conn = _db.GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT host, status, exit_code, output, started_at, finished_at FROM deploy_results WHERE task_id=@t ORDER BY host";
        cmd.Parameters.AddWithValue("@t", taskId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ResultRow { Host = S(r, 0), Status = S(r, 1), Code = r.IsDBNull(2) ? 0 : r.GetInt32(2), Output = S(r, 3), Started = S(r, 4), Finished = S(r, 5) });
        return list;
    }

    sealed class HostRow { public string Host = "", Ip = "", Os = "", Version = "", User = "", LastSeen = ""; }

    List<HostRow> LoadHosts()
    {
        var list = new List<HostRow>();
        using var conn = _db.GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT host, ip, os, version, user_name, last_seen FROM deploy_hosts ORDER BY host";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new HostRow { Host = S(r, 0), Ip = S(r, 1), Os = S(r, 2), Version = S(r, 3), User = S(r, 4), LastSeen = S(r, 5) });
        return list;
    }

    // hostname / asset tag (UPPER) -> department of the employee the asset is assigned to.
    Dictionary<string, string> DeptMap()
    {
        var d = new Dictionary<string, string>();
        foreach (var a in _db.GetAssets())
        {
            var dept = (a.GetValueOrDefault("assignedToDept")?.ToString() ?? "").Trim();
            if (dept.Length == 0) continue;
            foreach (var key in new[] { a.GetValueOrDefault("hostname")?.ToString(), a.GetValueOrDefault("assetTag")?.ToString() })
            {
                var k = (key ?? "").Trim().ToUpperInvariant();
                if (k.Length > 0 && !d.ContainsKey(k)) d[k] = dept;
            }
        }
        return d;
    }

    static bool Matches(TaskRow t, string host, Dictionary<string, string> deptMap)
    {
        var h = (host ?? "").Trim().ToUpperInvariant();
        if (t.Mode == "all") return true;
        if (t.Mode == "hosts")
            return t.Value.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(x => string.Equals(x.Trim(), h, StringComparison.OrdinalIgnoreCase));
        if (t.Mode == "dept")
            return deptMap.TryGetValue(h, out var d) && string.Equals(d, t.Value, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    static string TargetText(TaskRow t)
        => t.Mode == "all" ? "All " + (t.Os == "mac" ? "Macs" : "Windows PCs")
         : t.Mode == "dept" ? "Department: " + t.Value
         : "Computers: " + t.Value;

    static bool IsOnline(string lastSeen)
        => DateTime.TryParse(lastSeen, out var d) && (IstTime.Now - d).TotalMinutes <= 15;

    // ------------------------------------------------------------------ admin pages
    [HttpGet("/Deploy")]
    [HttpGet("/Deploy/Index")]
    public IActionResult Index()
    {
        var me = Admin(); if (me == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();
        ViewBag.User = me;

        var tasks = LoadTasks("");
        var counts = new Dictionary<long, int[]>();   // ok, failed, running
        using (var conn = _db.GetConn())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT task_id, status, COUNT(*) FROM deploy_results GROUP BY task_id, status";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0); var st = S(r, 1); var n = (int)r.GetInt64(2);
                if (!counts.ContainsKey(id)) counts[id] = new int[3];
                if (st == "success") counts[id][0] += n; else if (st == "failed") counts[id][1] += n; else counts[id][2] += n;
            }
        }
        var hosts = LoadHosts();
        var deptMap = DeptMap();
        var now = Now();

        ViewBag.Tasks = tasks.Select(t =>
        {
            var c = counts.ContainsKey(t.Id) ? counts[t.Id] : new int[3];
            var done = new HashSet<string>(LoadResults(t.Id).Select(x => x.Host), StringComparer.OrdinalIgnoreCase);
            var waiting = (t.Status == "active" && string.CompareOrdinal(t.Expires, now) > 0)
                ? hosts.Count(h => h.Os == t.Os && Matches(t, h.Host, deptMap) && !done.Contains(h.Host)) : 0;
            var what = t.Kind == "agent" ? "Agent update: " + t.AgentType : t.FileName;
            var state = t.Status == "cancelled" ? "Cancelled" : (string.CompareOrdinal(t.Expires, now) <= 0 ? "Expired" : "Active");
            return new object[] { t.Id, t.Name, t.Os, what, TargetText(t), t.CreatedAt, t.CreatedBy, t.Expires, state, c[0], c[1], c[2], waiting };
        }).ToList();
        ViewBag.Hosts = hosts.Select(h => new object[] { h.Host, h.Ip, h.Os, h.Version, h.User, h.LastSeen, IsOnline(h.LastSeen) }).ToList();
        ViewBag.Depts = deptMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        ViewBag.MaxMb = MaxFileBytes / (1024 * 1024);
        return View();
    }

    [HttpGet("/Deploy/Task/{id:long}")]
    public IActionResult Details(long id)
    {
        var me = Admin(); if (me == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();
        ViewBag.User = me;
        var t = LoadTasks("WHERE id=@i", "@i", id).FirstOrDefault();
        if (t == null) { TempData["Error"] = "Task not found."; return RedirectToAction("Index"); }

        var results = LoadResults(id);
        var done = new HashSet<string>(results.Select(x => x.Host), StringComparer.OrdinalIgnoreCase);
        var deptMap = DeptMap();
        var waiting = LoadHosts().Where(h => h.Os == t.Os && Matches(t, h.Host, deptMap) && !done.Contains(h.Host)).ToList();
        var now = Now();

        ViewBag.T = new object[] { t.Id, t.Name, t.Os, t.Kind, t.FileName, t.Size, t.Sha, t.Args, t.AgentType, TargetText(t), t.Status,
            t.CreatedBy, t.CreatedAt, t.Expires, string.CompareOrdinal(t.Expires, now) <= 0 };
        ViewBag.Results = results.Select(x => new object[] { x.Host, x.Status, x.Code, x.Output, x.Started, x.Finished }).ToList();
        ViewBag.Waiting = waiting.Select(h => new object[] { h.Host, h.Ip, h.LastSeen, IsOnline(h.LastSeen) }).ToList();
        return View("Details");
    }

    static int FillBuffer(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            var n = s.Read(buf, total, buf.Length - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    [HttpPost("/Deploy/Create")]
    [RequestSizeLimit(230_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 230_000_000)]
    public IActionResult Create(string? name, string? os, string? kind, string? agentType, string? args,
        string? targetMode, string? targetValue, int validDays, IFormFile? file)
    {
        var me = Admin(); if (me == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();

        name = Clean(name, 120);
        os = (os ?? "").Trim().ToLowerInvariant();
        kind = (kind ?? "").Trim().ToLowerInvariant();
        targetMode = (targetMode ?? "all").Trim().ToLowerInvariant();
        if (name.Length == 0) { TempData["Error"] = "Please enter a task name."; return RedirectToAction("Index"); }
        if (os != "windows" && os != "mac") { TempData["Error"] = "Choose Windows or Mac."; return RedirectToAction("Index"); }
        if (kind != "installer" && kind != "agent") { TempData["Error"] = "Choose what to deploy."; return RedirectToAction("Index"); }
        if (targetMode != "all" && targetMode != "hosts" && targetMode != "dept") targetMode = "all";
        if (validDays < 1) validDays = 7; if (validDays > 60) validDays = 60;

        string tv = "";
        if (targetMode == "hosts")
        {
            tv = string.Join(",", (targetValue ?? "").Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Clean(x, 100).ToUpperInvariant()).Where(x => x.Length > 0).Distinct());
            if (tv.Length == 0) { TempData["Error"] = "Enter at least one computer name."; return RedirectToAction("Index"); }
        }
        else if (targetMode == "dept")
        {
            tv = Clean(targetValue, 100);
            if (tv.Length == 0) { TempData["Error"] = "Choose a department."; return RedirectToAction("Index"); }
        }

        string fileName = "", agent = "";
        if (kind == "agent")
        {
            agent = (agentType ?? "").Trim().ToLowerInvariant();
            var okTypes = os == "windows" ? new[] { "pc", "print", "deploy" } : new[] { "mac", "macdeploy" };
            if (!okTypes.Contains(agent)) { TempData["Error"] = "Choose the agent to update."; return RedirectToAction("Index"); }
        }
        else
        {
            if (file == null || file.Length == 0) { TempData["Error"] = "Please choose the installer file."; return RedirectToAction("Index"); }
            if (file.Length > MaxFileBytes) { TempData["Error"] = "The file is too large (max " + (MaxFileBytes / (1024 * 1024)) + " MB)."; return RedirectToAction("Index"); }
            fileName = Clean(Path.GetFileName(file.FileName), 150);
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var okExt = os == "windows" ? new[] { ".exe", ".msi" } : new[] { ".pkg" };
            if (!okExt.Contains(ext)) { TempData["Error"] = os == "windows" ? "Windows installers must be .exe or .msi." : "Mac installers must be .pkg."; return RedirectToAction("Index"); }
        }

        var created = Now();
        var expires = IstTime.Now.AddDays(validDays).ToString("yyyy-MM-dd HH:mm:ss");
        long id;
        try
        {
            id = _db.QueryFirst<long>(@"INSERT INTO deploy_tasks (name, os, kind, file_name, file_size, sha256, args, agent_type, target_mode, target_value, status, created_by, created_at, expires_at)
                VALUES (@n,@o,@k,@f,0,'',@a,@at,@tm,@tv,'pending',@cb,@ca,@ex) RETURNING id",
                new { n = name, o = os, k = kind, f = fileName, a = Clean(args, 300), at = agent, tm = targetMode, tv = tv, cb = me.Username, ca = created, ex = expires });
        }
        catch (Exception ex) { TempData["Error"] = "Could not create the task: " + ex.Message; return RedirectToAction("Index"); }

        long total = 0; string sha = "";
        if (kind == "installer")
        {
            try
            {
                using var conn = _db.GetConn();
                using var tx = conn.BeginTransaction();
                using var inc = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[ChunkBytes];
                int seq = 0;
                using var s = file!.OpenReadStream();
                int n;
                while ((n = FillBuffer(s, buf)) > 0)
                {
                    inc.AppendData(buf, 0, n);
                    var piece = new byte[n];
                    Buffer.BlockCopy(buf, 0, piece, 0, n);
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO deploy_files (task_id, seq, data) VALUES (@t,@s,@d)";
                    cmd.Parameters.AddWithValue("@t", id);
                    cmd.Parameters.AddWithValue("@s", seq++);
                    cmd.Parameters.Add(new NpgsqlParameter("@d", NpgsqlTypes.NpgsqlDbType.Bytea) { Value = piece });
                    cmd.ExecuteNonQuery();
                    total += n;
                }
                sha = Convert.ToHexString(inc.GetHashAndReset()).ToLowerInvariant();
                tx.Commit();
            }
            catch (Exception ex)
            {
                try { _db.Execute("DELETE FROM deploy_files WHERE task_id=@i", new { i = id }); _db.Execute("DELETE FROM deploy_tasks WHERE id=@i", new { i = id }); } catch { }
                TempData["Error"] = "Upload failed: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        _db.Execute("UPDATE deploy_tasks SET file_size=@z, sha256=@h, status='active' WHERE id=@i", new { z = total, h = sha, i = id });
        TempData["Success"] = "Task \"" + name + "\" is ready. Each computer picks it up within about 5 minutes (valid for " + validDays + " days).";
        return RedirectToAction("Details", new { id });
    }

    [HttpPost("/Deploy/Cancel/{id:long}")]
    public IActionResult Cancel(long id)
    {
        if (Admin() == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();
        _db.Execute("UPDATE deploy_tasks SET status='cancelled' WHERE id=@i", new { i = id });
        TempData["Success"] = "Task cancelled. Computers that have not started it yet will skip it.";
        return RedirectToAction("Details", new { id });
    }

    // Lets the computers where it failed (or got stuck "running" for over 3 hours) try again.
    [HttpPost("/Deploy/Retry/{id:long}")]
    public IActionResult Retry(long id)
    {
        if (Admin() == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();
        var cut = IstTime.Now.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss");
        var n = _db.Execute("DELETE FROM deploy_results WHERE task_id=@i AND (status='failed' OR (status='running' AND started_at < @c))", new { i = id, c = cut });
        _db.Execute("UPDATE deploy_tasks SET status='active' WHERE id=@i AND status='cancelled'", new { i = id });
        TempData["Success"] = n + " computer(s) will try again at their next check.";
        return RedirectToAction("Details", new { id });
    }

    [HttpPost("/Deploy/Delete/{id:long}")]
    public IActionResult Delete(long id)
    {
        if (Admin() == null) return RedirectToAction("AccessDenied", "Home");
        Ensure();
        _db.Execute("DELETE FROM deploy_files WHERE task_id=@i", new { i = id });
        _db.Execute("DELETE FROM deploy_results WHERE task_id=@i", new { i = id });
        _db.Execute("DELETE FROM deploy_tasks WHERE id=@i", new { i = id });
        TempData["Success"] = "Task deleted (installer file removed from the database).";
        return RedirectToAction("Index");
    }

    // ------------------------------------------------------------------ agent -> website
    async Task<Dictionary<string, string>> ReadBody()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Request.HasJsonContentType())
        {
            using var sr = new StreamReader(Request.Body, Encoding.UTF8);
            var txt = await sr.ReadToEndAsync();
            var o = JsonConvert.DeserializeObject<Dictionary<string, object?>>(txt) ?? new Dictionary<string, object?>();
            foreach (var kv in o) d[kv.Key] = kv.Value?.ToString() ?? "";
        }
        else if (Request.HasFormContentType)
        {
            foreach (var kv in Request.Form) d[kv.Key] = kv.Value.ToString();
        }
        return d;
    }

    // The agent asks "is anything waiting for me?". JSON for Windows, plain text (fmt=text) for Mac:
    //   first line OK, then one line per task:  id|kind|fileName|sha256|agentType|args
    [HttpPost("/api/deploy/poll")]
    public async Task<IActionResult> Poll()
    {
        var d = await ReadBody();
        if (!KeyOk(d.GetValueOrDefault("key"))) return Unauthorized(new { ok = false, error = "Invalid or missing key" });
        try { Ensure(); } catch (Exception ex) { return StatusCode(500, new { ok = false, error = "db: " + ex.Message }); }

        var host = Cut(d.GetValueOrDefault("hostname"), 100).ToUpperInvariant();
        if (host.Length == 0) return BadRequest(new { ok = false, error = "hostname missing" });
        var os = Cut(d.GetValueOrDefault("os"), 10).ToLowerInvariant();
        if (os != "mac") os = "windows";

        _db.Execute(@"INSERT INTO deploy_hosts (host, ip, os, version, user_name, last_seen) VALUES (@h,@ip,@o,@v,@u,@t)
            ON CONFLICT (host) DO UPDATE SET ip=@ip, os=@o, version=@v, user_name=@u, last_seen=@t",
            new { h = host, ip = Cut(d.GetValueOrDefault("ip"), 60), o = os, v = Cut(d.GetValueOrDefault("agentVersion"), 20), u = Cut(d.GetValueOrDefault("user"), 120), t = Now() });

        var now = Now();
        var tasks = LoadTasks("WHERE status='active' AND os=@o", "@o", os)
            .Where(t => string.CompareOrdinal(t.Expires, now) > 0).OrderBy(t => t.Id).ToList();
        var doneIds = new HashSet<long>(_db.Query<long>("SELECT task_id FROM deploy_results WHERE host=@h", new { h = host }));
        var deptMap = DeptMap();
        var mine = tasks.Where(t => !doneIds.Contains(t.Id) && Matches(t, host, deptMap)).Take(5).ToList();

        static string P(string? v) => (v ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
        if (string.Equals(d.GetValueOrDefault("fmt"), "text", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder("OK\n");
            foreach (var t in mine) sb.Append(t.Id).Append('|').Append(t.Kind).Append('|').Append(P(t.FileName)).Append('|').Append(t.Sha).Append('|').Append(P(t.AgentType)).Append('|').Append(P(t.Args)).Append('\n');
            return Content(sb.ToString(), "text/plain; charset=utf-8");
        }
        return Json(new
        {
            ok = true,
            tasks = mine.Select(t => new { id = t.Id, name = t.Name, kind = t.Kind, fileName = t.FileName, sha256 = t.Sha, size = t.Size, args = t.Args, agentType = t.AgentType })
        });
    }

    // The agent reports "running" when it starts a task, then "success" or "failed".
    [HttpPost("/api/deploy/result")]
    public async Task<IActionResult> Result()
    {
        var d = await ReadBody();
        if (!KeyOk(d.GetValueOrDefault("key"))) return Unauthorized(new { ok = false, error = "Invalid or missing key" });
        Ensure();
        var host = Cut(d.GetValueOrDefault("hostname"), 100).ToUpperInvariant();
        if (host.Length == 0 || !long.TryParse(d.GetValueOrDefault("taskId"), out var taskId)) return BadRequest(new { ok = false, error = "hostname/taskId missing" });
        var status = Cut(d.GetValueOrDefault("status"), 12).ToLowerInvariant();
        if (status != "running" && status != "success" && status != "failed") status = "failed";
        int.TryParse(d.GetValueOrDefault("exitCode"), out var code);
        var output = Cut(d.GetValueOrDefault("output"), 4000);
        var t = Now();
        _db.Execute(@"INSERT INTO deploy_results (task_id, host, status, exit_code, output, started_at, finished_at) VALUES (@i,@h,@s,@c,@o,@t,@f)
            ON CONFLICT (task_id, host) DO UPDATE SET status=@s, exit_code=@c, output=@o, finished_at=@f",
            new { i = taskId, h = host, s = status, c = code, o = output, t = t, f = status == "running" ? "" : t });
        return Ok(new { ok = true });
    }

    // Streams the installer to the agent (piece by piece, so the server uses little memory).
    [HttpGet("/api/deploy/file")]
    public async Task<IActionResult> FileDownload(string? key, long task)
    {
        if (!KeyOk(key)) return Unauthorized("Invalid or missing key");
        Ensure();
        var size = _db.QueryFirst<long>("SELECT file_size FROM deploy_tasks WHERE id=@i AND status='active' AND kind='installer'", new { i = task });
        if (size <= 0) return NotFound("No such file.");
        Response.ContentType = "application/octet-stream";
        Response.ContentLength = size;
        Response.Headers["Cache-Control"] = "no-store";
        using var conn = _db.GetConn();
        for (int seq = 0; ; seq++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM deploy_files WHERE task_id=@t AND seq=@s";
            cmd.Parameters.AddWithValue("@t", task);
            cmd.Parameters.AddWithValue("@s", seq);
            var o = cmd.ExecuteScalar();
            if (o == null || o is DBNull) break;
            var b = (byte[])o;
            await Response.Body.WriteAsync(b, 0, b.Length, HttpContext.RequestAborted);
        }
        return new EmptyResult();
    }

    // Windows agent update: the newest agent files (ZIP with the key filled in).
    [HttpGet("/api/deploy/agent-zip")]
    public IActionResult AgentZip(string? key, string? type, [FromServices] IWebHostEnvironment env)
    {
        if (!KeyOk(key)) return Unauthorized("Invalid or missing key");
        var t = (type ?? "").Trim().ToLowerInvariant();
        if (t != "pc" && t != "print" && t != "deploy") return BadRequest("Unknown agent.");
        var pkg = AgentPackage.Build(env, t, EndpointsController.CurrentAgentKey);
        if (pkg == null) return NotFound("Agent files are not deployed on the server yet.");
        Response.Headers["Cache-Control"] = "no-store";
        return File(pkg.Value.Bytes, "application/zip", pkg.Value.FileName);
    }

    // Mac Deploy Agent one-line installer (Terminal), same idea as the Mac inventory agent.
    //   ?file=agent  -> AMPM_Mac_Deploy_Agent.sh with the key filled in
    //   (default)    -> AMPM_Mac_Deploy_Install.sh (key + this site's address filled in)
    [HttpGet("/api/deploy/mac-install")]
    public IActionResult MacInstall(string? key, string? file, [FromServices] IWebHostEnvironment env)
    {
        if (!KeyOk(key)) return Unauthorized("Invalid or missing key");
        var name = string.Equals(file, "agent", StringComparison.OrdinalIgnoreCase) ? "AMPM_Mac_Deploy_Agent.sh" : "AMPM_Mac_Deploy_Install.sh";
        var path = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "tools", name);
        if (!System.IO.File.Exists(path)) return NotFound("Mac deploy agent files are not deployed on the server yet.");
        var host = Request.Host.Host;
        var proto = (host == "localhost" || host == "127.0.0.1") ? Request.Scheme : "https";
        var text = System.IO.File.ReadAllText(path).Replace("\r\n", "\n")
            .Replace(AgentPackage.Placeholder, EndpointsController.CurrentAgentKey)
            .Replace("__AMPM_BASE_URL__", proto + "://" + Request.Host);
        Response.Headers["Cache-Control"] = "no-store";
        return Content(text, "text/plain; charset=utf-8");
    }
}
