using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

// Employee-to-employee chat (WhatsApp style, 1-to-1) for the website AND the Helpdesk
// Android app. Rules:
//  * Only the two people in a conversation can read it (checked on every call against
//    the signed-in user - nobody, not even an admin, can open someone else's chat here).
//  * Messages are kept for ever (chat history is never auto-deleted).
//  * Attached files are stored in the database and deleted automatically 2 days after
//    they were sent (so storage does not fill up); the message itself stays, marked
//    "file expired".
// The website page uses the login cookie; the app sends its usual HTTP Basic login.
// Exempted from ModulePermissionFilter (see Filters/ModulePermissionFilter.cs) - every
// logged-in user may chat, and this controller does its own login check.
public class ChatController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public ChatController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    public const long MaxFileBytes = 25L * 1024 * 1024;   // per file
    const int FileKeepDays = 2;

    // ---- who is calling ------------------------------------------------------
    // Browser: login cookie. App: HTTP Basic (username/employee code + password). The app
    // polls every few seconds, and a bcrypt check on every poll would be heavy on a small
    // server, so a successful Basic login is remembered for 2 minutes.
    static readonly ConcurrentDictionary<string, (UserSession User, DateTime Expires)> _basicCache = new();

    UserSession? Me()
    {
        var u = _auth.GetCurrentUser(HttpContext);
        if (u != null) return u;

        var header = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header)));
        if (_basicCache.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow) return hit.User;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6).Trim()));
            var idx = decoded.IndexOf(':');
            if (idx < 0) return null;
            var s = _auth.Login(decoded.Substring(0, idx), decoded.Substring(idx + 1));
            if (s == null) return null;
            if (_basicCache.Count > 500) _basicCache.Clear();
            _basicCache[key] = (s, DateTime.UtcNow.AddMinutes(2));
            return s;
        }
        catch { return null; }
    }

    // ---- tables + 2-day file clean-up ---------------------------------------
    static volatile bool _ready;
    static readonly object _lock = new();
    static DateTime _lastCleanup = DateTime.MinValue;

    void Ensure()
    {
        if (!_ready)
        {
            lock (_lock)
            {
                if (!_ready)
                {
                    _db.Execute(@"CREATE TABLE IF NOT EXISTS chat_files (
                        id TEXT PRIMARY KEY, file_name TEXT, content_type TEXT, size BIGINT, data BYTEA, uploaded_at TEXT)");
                    _db.Execute(@"CREATE TABLE IF NOT EXISTS chat_messages (
                        id BIGSERIAL PRIMARY KEY, sender TEXT NOT NULL, receiver TEXT NOT NULL, body TEXT,
                        file_id TEXT, file_name TEXT, file_size BIGINT, created_at TEXT, read_at TEXT)");
                    _db.Execute("CREATE INDEX IF NOT EXISTS chat_msg_pair ON chat_messages (sender, receiver, id)");
                    _db.Execute("CREATE INDEX IF NOT EXISTS chat_msg_recv ON chat_messages (receiver, read_at)");
                    _ready = true;
                }
            }
        }
        // Files older than 2 days are removed (messages stay). Checked at most every 10 minutes.
        if ((DateTime.UtcNow - _lastCleanup).TotalMinutes >= 10)
        {
            _lastCleanup = DateTime.UtcNow;
            try { _db.Execute("DELETE FROM chat_files WHERE uploaded_at < @c", new { c = IstTime.Now.AddDays(-FileKeepDays).ToString("yyyy-MM-dd HH:mm:ss") }); }
            catch { /* try again next time */ }
        }
    }

    static string Now() => IstTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    static string Cut(string? s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) : s; }

    // ---- web page -------------------------------------------------------------
    public IActionResult Index()
    {
        var user = _auth.GetCurrentUser(HttpContext);
        if (user == null) return RedirectToAction("Login", "Account");
        ViewBag.User = user;
        ViewBag.MaxMb = MaxFileBytes / 1024 / 1024;
        return View();
    }

    // ---- JSON API (website + app) ---------------------------------------------
    // People + last message + unread count. Everyone with an active login is listed;
    // people you already chat with come first (latest conversation on top).
    [HttpGet("/api/chat/contacts")]
    public IActionResult Contacts()
    {
        var me = Me(); if (me == null) return Unauthorized();
        Ensure();

        var last = new Dictionary<string, (long Id, long Unread)>(StringComparer.OrdinalIgnoreCase);
        var info = new Dictionary<long, (string Body, string FileName, string At, string From)>();
        using (var conn = _db.GetConn())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT CASE WHEN sender=@me THEN receiver ELSE sender END AS peer,
                        MAX(id) AS last_id,
                        SUM(CASE WHEN receiver=@me AND read_at IS NULL THEN 1 ELSE 0 END) AS unread
                    FROM chat_messages WHERE sender=@me OR receiver=@me GROUP BY 1";
                cmd.Parameters.AddWithValue("@me", me.Username);
                using var r = cmd.ExecuteReader();
                while (r.Read()) last[r.GetString(0)] = (r.GetInt64(1), r.IsDBNull(2) ? 0 : Convert.ToInt64(r.GetValue(2)));
            }
            if (last.Count > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, COALESCE(body,''), COALESCE(file_name,''), COALESCE(created_at,''), sender FROM chat_messages WHERE id = ANY(@ids)";
                cmd.Parameters.AddWithValue("@ids", last.Values.Select(v => v.Id).ToArray());
                using var r = cmd.ExecuteReader();
                while (r.Read()) info[r.GetInt64(0)] = (r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4));
            }
        }

        var list = new List<Dictionary<string, object?>>();
        foreach (var u in _db.GetUsers())
        {
            if (u.IsActive == 0 || string.Equals(u.Username, me.Username, StringComparison.OrdinalIgnoreCase)) continue;
            var d = new Dictionary<string, object?>
            {
                ["username"] = u.Username,
                ["name"] = string.IsNullOrWhiteSpace(u.Name) ? u.Username : u.Name,
                ["dept"] = u.Department ?? "",
                ["last"] = "", ["lastAt"] = "", ["unread"] = 0L, ["mine"] = false,
            };
            if (last.TryGetValue(u.Username, out var l))
            {
                d["unread"] = l.Unread;
                if (info.TryGetValue(l.Id, out var m))
                {
                    d["last"] = m.Body.Length > 0 ? Cut(m.Body, 80) : (m.FileName.Length > 0 ? "\U0001F4CE " + m.FileName : "");
                    d["lastAt"] = m.At;
                    d["mine"] = string.Equals(m.From, me.Username, StringComparison.OrdinalIgnoreCase);
                }
            }
            list.Add(d);
        }
        var ordered = list.OrderByDescending(x => (string)x["lastAt"]!)
            .ThenBy(x => (string)x["name"]!, StringComparer.OrdinalIgnoreCase).ToList();
        return Json(new { ok = true, me = me.Username, contacts = ordered });
    }

    // Messages of one conversation, oldest first. after=0 -> the latest 200;
    // after=<id> -> only newer ones (used for polling); before=<id> -> 200 older ones.
    [HttpGet("/api/chat/messages")]
    public IActionResult Messages(string? peer, long after = 0, long before = 0)
    {
        var me = Me(); if (me == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(peer)) return BadRequest(new { ok = false, error = "peer missing" });
        Ensure();

        var rows = new List<Dictionary<string, object?>>();
        using (var conn = _db.GetConn())
        {
            using (var cmd = conn.CreateCommand())
            {
                var cond = "((m.sender=@me AND m.receiver=@p) OR (m.sender=@p AND m.receiver=@me))";
                if (after > 0) { cond += " AND m.id > @after"; cmd.Parameters.AddWithValue("@after", after); }
                if (before > 0) { cond += " AND m.id < @before"; cmd.Parameters.AddWithValue("@before", before); }
                cmd.CommandText = $@"SELECT * FROM (
                        SELECT m.id, m.sender, COALESCE(m.body,'') AS body, m.file_id, COALESCE(m.file_name,'') AS file_name,
                               COALESCE(m.file_size,0) AS file_size, COALESCE(m.created_at,'') AS created_at,
                               (m.file_id IS NOT NULL AND f.id IS NULL) AS expired, (m.read_at IS NOT NULL) AS is_read
                        FROM chat_messages m LEFT JOIN chat_files f ON f.id = m.file_id
                        WHERE {cond} ORDER BY m.id DESC LIMIT 200) t ORDER BY id ASC";
                cmd.Parameters.AddWithValue("@me", me.Username);
                cmd.Parameters.AddWithValue("@p", peer.Trim());
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var sender = r.GetString(1);
                    rows.Add(new Dictionary<string, object?>
                    {
                        ["id"] = r.GetInt64(0),
                        ["mine"] = string.Equals(sender, me.Username, StringComparison.OrdinalIgnoreCase),
                        ["text"] = r.GetString(2),
                        ["fileId"] = r.IsDBNull(3) ? "" : r.GetString(3),
                        ["fileName"] = r.GetString(4),
                        ["fileSize"] = r.GetInt64(5),
                        ["at"] = r.GetString(6),
                        ["fileExpired"] = r.GetBoolean(7),
                        ["read"] = r.GetBoolean(8),
                    });
                }
            }
            // the other person's messages in this conversation are now read
            using (var up = conn.CreateCommand())
            {
                up.CommandText = "UPDATE chat_messages SET read_at=@t WHERE receiver=@me AND sender=@p AND read_at IS NULL";
                up.Parameters.AddWithValue("@t", Now());
                up.Parameters.AddWithValue("@me", me.Username);
                up.Parameters.AddWithValue("@p", peer.Trim());
                up.ExecuteNonQuery();
            }
        }
        return Json(new { ok = true, messages = rows });
    }

    // Total unread messages (nav badge / app badge).
    [HttpGet("/api/chat/unread")]
    public IActionResult Unread()
    {
        var me = Me(); if (me == null) return Unauthorized();
        Ensure();
        var n = _db.QueryFirst<long>("SELECT COUNT(*) FROM chat_messages WHERE receiver=@me AND read_at IS NULL", new { me = me.Username });
        return Json(new { ok = true, unread = n });
    }

    bool PeerExists(string peer, UserSession me)
    {
        if (string.IsNullOrWhiteSpace(peer) || string.Equals(peer.Trim(), me.Username, StringComparison.OrdinalIgnoreCase)) return false;
        return _db.GetUsers().Any(u => u.IsActive == 1 && string.Equals(u.Username, peer.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    long Insert(UserSession me, string to, string body, string? fileId, string? fileName, long fileSize)
    {
        using var conn = _db.GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO chat_messages (sender, receiver, body, file_id, file_name, file_size, created_at)
            VALUES (@s,@r,@b,@fid,@fn,@fs,@t) RETURNING id";
        cmd.Parameters.AddWithValue("@s", me.Username);
        cmd.Parameters.AddWithValue("@r", to);
        cmd.Parameters.AddWithValue("@b", body);
        cmd.Parameters.AddWithValue("@fid", (object?)fileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fn", (object?)fileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fs", fileSize);
        cmd.Parameters.AddWithValue("@t", Now());
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public class ChatSendDto { public string? To { get; set; } public string? Text { get; set; } }

    [HttpPost("/api/chat/send")]
    public IActionResult Send([FromBody] ChatSendDto data)
    {
        var me = Me(); if (me == null) return Unauthorized();
        Ensure();
        var text = (data?.Text ?? "").Trim();
        var to = (data?.To ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { ok = false, error = "Message is empty." });
        if (!PeerExists(to, me)) return BadRequest(new { ok = false, error = "Unknown person." });
        var id = Insert(me, _db.GetUsers().First(u => string.Equals(u.Username, to, StringComparison.OrdinalIgnoreCase)).Username, Cut(text, 4000), null, null, 0);
        return Json(new { ok = true, id });
    }

    // multipart/form-data: to, file, text (optional caption)
    [HttpPost("/api/chat/upload")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] string? to, [FromForm] string? text, IFormFile? file)
    {
        var me = Me(); if (me == null) return Unauthorized();
        Ensure();
        to = (to ?? "").Trim();
        if (!PeerExists(to, me)) return BadRequest(new { ok = false, error = "Unknown person." });
        if (file == null || file.Length == 0) return BadRequest(new { ok = false, error = "No file." });
        if (file.Length > MaxFileBytes) return BadRequest(new { ok = false, error = $"File is too big (max {MaxFileBytes / 1024 / 1024} MB)." });

        byte[] bytes;
        using (var ms = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        var name = Path.GetFileName(file.FileName ?? "file");
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        name = Cut(name, 200);
        var fid = Guid.NewGuid().ToString("N");

        using (var conn = _db.GetConn())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO chat_files (id, file_name, content_type, size, data, uploaded_at) VALUES (@i,@n,@c,@s,@d,@t)";
            cmd.Parameters.AddWithValue("@i", fid);
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@c", Cut(file.ContentType, 100));
            cmd.Parameters.AddWithValue("@s", (long)bytes.Length);
            cmd.Parameters.Add(new NpgsqlParameter("@d", NpgsqlTypes.NpgsqlDbType.Bytea) { Value = bytes });
            cmd.Parameters.AddWithValue("@t", Now());
            cmd.ExecuteNonQuery();
        }
        var peerName = _db.GetUsers().First(u => string.Equals(u.Username, to, StringComparison.OrdinalIgnoreCase)).Username;
        var id = Insert(me, peerName, Cut((text ?? "").Trim(), 4000), fid, name, bytes.Length);
        return Json(new { ok = true, id });
    }

    // Download (or inline preview for pictures with ?inline=1). Only the sender and the
    // receiver of the message the file belongs to may open it.
    [HttpGet("/api/chat/file/{id}")]
    public IActionResult Download(string id, bool inline = false)
    {
        var me = Me(); if (me == null) return Unauthorized();
        Ensure();
        string name, type; byte[] data;
        using (var conn = _db.GetConn())
        {
            using (var chk = conn.CreateCommand())
            {
                chk.CommandText = "SELECT 1 FROM chat_messages WHERE file_id=@i AND (sender=@me OR receiver=@me) LIMIT 1";
                chk.Parameters.AddWithValue("@i", id ?? "");
                chk.Parameters.AddWithValue("@me", me.Username);
                if (chk.ExecuteScalar() == null) return NotFound(new { ok = false, error = "Not found." });
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT file_name, content_type, data FROM chat_files WHERE id=@i";
            cmd.Parameters.AddWithValue("@i", id ?? "");
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return StatusCode(410, new { ok = false, error = $"This file was deleted automatically (files are kept for {FileKeepDays} days)." });
            name = r.IsDBNull(0) ? "file" : r.GetString(0);
            type = r.IsDBNull(1) ? "" : r.GetString(1);
            data = (byte[])r.GetValue(2);
        }
        Response.Headers["Cache-Control"] = "private, no-store";
        var isImage = type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !type.Contains("svg", StringComparison.OrdinalIgnoreCase);
        if (inline && isImage) return File(data, type);
        return File(data, "application/octet-stream", name);
    }
}
