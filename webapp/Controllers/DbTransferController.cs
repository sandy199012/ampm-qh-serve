using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net;
using System.Text;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

// One-time tool: copy the WHOLE database that this website is using right now (the "old" one)
// into another Supabase project (the "new" one). Superadmin only.
//   * The old database is reached through the connection the website already has - no old password needed.
//   * The new project's database password is typed on the page (it is used for this one request and never stored or logged).
//   * Tables, indexes and sequences are created in the new database, every table is copied with PostgreSQL
//     binary COPY (exact bytes, including scans and files), row counts + a content checksum are compared for every
//     table, and everything is committed only if all tables match.
//   * The old database is only read. Nothing in it is changed.
public class DbTransferController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public DbTransferController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    UserSession? Admin()
    {
        var u = _auth.GetCurrentUser(HttpContext);
        return (u != null && u.IsSuperAdmin) ? u : null;
    }

    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    static string Page(string body) => "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
        + "<title>Database transfer</title><style>body{font-family:Segoe UI,Arial,sans-serif;max-width:860px;margin:24px auto;padding:0 16px;color:#1f2937}"
        + "label{display:block;margin-top:12px;font-size:14px;font-weight:600}input[type=text],input[type=password]{width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:6px;box-sizing:border-box}"
        + "button{margin-top:16px;padding:10px 18px;background:#2563EB;color:#fff;border:0;border-radius:6px;font-size:15px;cursor:pointer}"
        + "pre{background:#0f172a;color:#e2e8f0;padding:14px;border-radius:8px;white-space:pre-wrap;word-break:break-word;font-size:13px}.ok{color:#15803d}.bad{color:#b91c1c}.note{font-size:13px;color:#475569}</style></head><body>"
        + body + "</body></html>";

    [HttpGet("/DbTransfer")]
    [HttpGet("/DbTransfer/Index")]
    public IActionResult Index()
    {
        if (Admin() == null) return RedirectToAction("AccessDenied", "Home");
        var html = @"<h2>Database transfer</h2>
<p class='note'>Copies <b>every table and all data</b> (including scans and files) from the database this website uses now into another Supabase project.
The current database is only read &ndash; nothing in it is changed. The password below is used for this one request only and is not stored.</p>
<form method='post' action='/DbTransfer/Run'>
<label>New project reference</label><input type='text' name='projRef' value='rnucxcbhdmffbaefvplj'/>
<label>New project region</label><input type='text' name='region' value='ap-northeast-1'/>
<label>New project database password</label><input type='password' name='pass' autocomplete='off' required/>
<label><input type='checkbox' name='confirm' value='YES'/> The new database may already contain data &ndash; erase it and copy again</label>
<button type='submit'>Start transfer</button>
<p class='note'>It takes about a minute. Keep this page open until the result appears.</p>
</form>";
        return Content(Page(html), "text/html", Encoding.UTF8);
    }

    [HttpPost("/DbTransfer/Run")]
    [RequestSizeLimit(100_000)]
    public IActionResult Run(string? projRef, string? region, string? pass, string? confirm)
    {
        if (Admin() == null) return RedirectToAction("AccessDenied", "Home");
        var log = new StringBuilder();
        bool ok = false;
        try { ok = Transfer((projRef ?? "").Trim(), (region ?? "").Trim(), pass ?? "", confirm == "YES", log); }
        catch (Exception ex) { log.AppendLine("ERROR: " + ex.Message); }
        var head = ok ? "<h2 class='ok'>Transfer finished &ndash; all tables match</h2>" : "<h2 class='bad'>Transfer did not finish &ndash; nothing was saved in the new database</h2>";
        return Content(Page(head + "<pre>" + H(log.ToString()) + "</pre><p><a href='/DbTransfer'>Back</a></p>"), "text/html", Encoding.UTF8);
    }

    static NpgsqlConnection? OpenNew(string projRef, string region, string pass, StringBuilder log)
    {
        var cands = new List<(string host, string user)>
        {
            ("aws-0-" + region + ".pooler.supabase.com", "postgres." + projRef),
            ("aws-1-" + region + ".pooler.supabase.com", "postgres." + projRef),
            ("db." + projRef + ".supabase.co", "postgres"),
        };
        foreach (var (host, user) in cands)
        {
            try
            {
                var b = new NpgsqlConnectionStringBuilder
                {
                    Host = host, Port = 5432, Username = user, Password = pass, Database = "postgres",
                    SslMode = SslMode.Require, TrustServerCertificate = true, Timeout = 15, Pooling = false
                };
                var c = new NpgsqlConnection(b.ConnectionString);
                c.Open();
                log.AppendLine("New database: connected via " + host);
                return c;
            }
            catch (Exception ex)
            {
                log.AppendLine("New database: " + host + " -> " + ex.Message.Replace("\n", " "));
            }
        }
        return null;
    }

    static long Scalar(NpgsqlConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 120;
        var v = cmd.ExecuteScalar(); return v == null || v is DBNull ? 0 : Convert.ToInt64(v);
    }
    static void Exec(NpgsqlConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 300; cmd.ExecuteNonQuery();
    }
    static string Check(NpgsqlConnection c, string table)
    {
        using var cmd = c.CreateCommand(); cmd.CommandTimeout = 300;
        cmd.CommandText = "select count(*)::text || ' / ' || md5(coalesce(string_agg(x::text, '|' order by x::text), '')) from public.\"" + table + "\" x";
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    bool Transfer(string projRef, string region, string pass, bool erase, StringBuilder log)
    {
        if (projRef.Length < 10 || region.Length < 5 || pass.Length == 0) { log.AppendLine("Project reference, region and password are required."); return false; }
        log.AppendLine("Started " + IstTime.Now.ToString("dd-MM-yyyy HH:mm:ss") + " IST");
        using var oldC = _db.GetConn();
        using var newC = OpenNew(projRef, region, pass, log)!;
        if (newC == null) { log.AppendLine("Could not connect to the new database. Check the project reference, region and password."); return false; }

        Exec(oldC, "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY");   // one consistent snapshot of the old data
        var tables = new List<string>();
        using (var cmd = oldC.CreateCommand())
        {
            cmd.CommandText = "select tablename from pg_tables where schemaname='public' order by 1";
            using var r = cmd.ExecuteReader(); while (r.Read()) tables.Add(r.GetString(0));
        }
        log.AppendLine("Old database has " + tables.Count + " tables.");

        Exec(newC, "BEGIN");
        try
        {
            using (var cmd = newC.CreateCommand())
            {
                cmd.CommandText = "select to_regclass('public.users') is null";
                if ((bool)cmd.ExecuteScalar()!) { log.AppendLine("Creating tables in the new database ..."); Exec(newC, SchemaSql); }
                else log.AppendLine("Tables already exist in the new database - reusing them.");
            }
            var have = new HashSet<string>();
            using (var cmd = newC.CreateCommand())
            {
                cmd.CommandText = "select tablename from pg_tables where schemaname='public'";
                using var r = cmd.ExecuteReader(); while (r.Read()) have.Add(r.GetString(0));
            }
            var missing = tables.Where(t => !have.Contains(t)).ToList();
            if (missing.Count > 0) { log.AppendLine("Missing in the new database: " + string.Join(", ", missing)); Exec(newC, "ROLLBACK"); return false; }

            var filled = tables.Where(t => Scalar(newC, "select count(*) from public.\"" + t + "\"") > 0).ToList();
            if (filled.Count > 0 && !erase)
            {
                log.AppendLine("The new database already has data in: " + string.Join(", ", filled));
                log.AppendLine("Tick the 'erase it and copy again' box and start again if that is what you want.");
                Exec(newC, "ROLLBACK"); return false;
            }
            Exec(newC, "TRUNCATE " + string.Join(", ", tables.Select(t => "public.\"" + t + "\"")));

            foreach (var t in tables)
            {
                long bytes = 0;
                using (var src = oldC.BeginRawBinaryCopy("COPY public.\"" + t + "\" TO STDOUT (FORMAT binary)"))
                using (var dst = newC.BeginRawBinaryCopy("COPY public.\"" + t + "\" FROM STDIN (FORMAT binary)"))
                {
                    var buf = new byte[81920]; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); bytes += n; }
                }
                log.AppendLine(string.Format("  copied {0,-18} {1,9:0.0} KB", t, bytes / 1024.0));
            }

            foreach (var (seq, tbl, col) in new[] { ("users_id_seq", "users", "id"), ("chat_messages_id_seq", "chat_messages", "id"), ("deploy_tasks_id_seq", "deploy_tasks", "id") })
            {
                var m = Scalar(newC, "select coalesce(max(\"" + col + "\"),0) from public.\"" + tbl + "\"");
                Exec(newC, m == 0 ? "select setval('public." + seq + "', 1, false)" : "select setval('public." + seq + "', " + m + ", true)");
            }
            log.AppendLine("Sequences updated.");

            log.AppendLine("Verifying (row count / content checksum per table) ...");
            var bad = new List<string>();
            foreach (var t in tables)
            {
                var a = Check(oldC, t); var b = Check(newC, t);
                var same = a == b;
                log.AppendLine(string.Format("  {0,-18} old={1,-10} new={2,-10} {3}", t, a.Split(' ')[0], b.Split(' ')[0], same ? "OK" : "MISMATCH"));
                if (!same) bad.Add(t);
            }
            if (bad.Count > 0) { log.AppendLine("MISMATCH in: " + string.Join(", ", bad) + " - nothing was saved."); Exec(newC, "ROLLBACK"); return false; }
            Exec(newC, "COMMIT");
            log.AppendLine("");
            log.AppendLine("DONE. All " + tables.Count + " tables copied and verified. The old database was not changed.");
            return true;
        }
        catch
        {
            try { Exec(newC, "ROLLBACK"); } catch { }
            throw;
        }
        finally { try { Exec(oldC, "ROLLBACK"); } catch { } }
    }

    const string SchemaSql = """
-- AMPM IT Tool: schema copied from the old Supabase project (public schema)
CREATE SEQUENCE IF NOT EXISTS public.users_id_seq AS integer;
CREATE SEQUENCE IF NOT EXISTS public.chat_messages_id_seq AS bigint;
CREATE SEQUENCE IF NOT EXISTS public.deploy_tasks_id_seq AS bigint;

CREATE TABLE IF NOT EXISTS public.users (
  id integer DEFAULT nextval('public.users_id_seq'::regclass) NOT NULL,
  username text NOT NULL,
  password_hash text NOT NULL,
  name text,
  role text DEFAULT 'user'::text,
  department text,
  is_active integer DEFAULT 1,
  created_at text,
  permissions text,
  emp_id text,
  must_change_password integer DEFAULT 0
);
CREATE TABLE IF NOT EXISTS public.employees (emp text NOT NULL, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.po_list (po_number text NOT NULL, data text NOT NULL, vendor text, total real, status text, ts text);
CREATE TABLE IF NOT EXISTS public.tickets (ticket_id text NOT NULL, data text NOT NULL, status text, ts text);
CREATE TABLE IF NOT EXISTS public.kv (k text NOT NULL, v text);
CREATE TABLE IF NOT EXISTS public.vendors (vendor_id text NOT NULL, name text NOT NULL, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.goals (id text NOT NULL, week_no integer, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.it_stock_items (id text NOT NULL, item_type text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.it_stock_issues (id text NOT NULL, issue_no text, emp_id text, emp_name text, dept text, item_id text, status text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.stock_items (id text NOT NULL, item_type text, name text NOT NULL, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.stock_issues (id text NOT NULL, item_id text, issue_no text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.cartridges (id text NOT NULL, name text NOT NULL, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.cartridge_issues (id text NOT NULL, cartridge_id text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.it_issue_scans (id text NOT NULL, issue_id text NOT NULL, file_name text, file_data text, uploaded_at text, uploaded_by text);
CREATE TABLE IF NOT EXISTS public.po_scans (id text NOT NULL, po_number text, file_name text, file_data text, content_type text, uploaded_at text, uploaded_by text);
CREATE TABLE IF NOT EXISTS public.todos (id text NOT NULL, username text, task_date text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.bill_scans (id text NOT NULL, bill_id text, file_name text, file_data text, content_type text, uploaded_at text, uploaded_by text);
CREATE TABLE IF NOT EXISTS public.checklist_log (id text NOT NULL, item_id text, check_date text, data text NOT NULL, ts text);
CREATE TABLE IF NOT EXISTS public.print_jobs (event_key text NOT NULL, pc text, ip text, user_name text, printer text, document text, port text, pages integer, bytes bigint, printed_at text, received_at text);
CREATE TABLE IF NOT EXISTS public.chat_files (id text NOT NULL, file_name text, content_type text, size bigint, data bytea, uploaded_at text);
CREATE TABLE IF NOT EXISTS public.chat_messages (
  id bigint DEFAULT nextval('public.chat_messages_id_seq'::regclass) NOT NULL,
  sender text NOT NULL, receiver text NOT NULL, body text, file_id text, file_name text, file_size bigint, created_at text, read_at text);
CREATE TABLE IF NOT EXISTS public.deploy_tasks (
  id bigint DEFAULT nextval('public.deploy_tasks_id_seq'::regclass) NOT NULL,
  name text, os text, kind text, file_name text, file_size bigint, sha256 text, args text, agent_type text,
  target_mode text, target_value text, status text, created_by text, created_at text, expires_at text);
CREATE TABLE IF NOT EXISTS public.deploy_files (task_id bigint NOT NULL, seq integer NOT NULL, data bytea);
CREATE TABLE IF NOT EXISTS public.deploy_results (task_id bigint NOT NULL, host text NOT NULL, status text, exit_code integer, output text, started_at text, finished_at text);
CREATE TABLE IF NOT EXISTS public.deploy_hosts (host text NOT NULL, ip text, os text, version text, user_name text, last_seen text);

ALTER SEQUENCE public.users_id_seq OWNED BY public.users.id;
ALTER SEQUENCE public.chat_messages_id_seq OWNED BY public.chat_messages.id;
ALTER SEQUENCE public.deploy_tasks_id_seq OWNED BY public.deploy_tasks.id;

ALTER TABLE public.bill_scans ADD CONSTRAINT bill_scans_pkey PRIMARY KEY (id);
ALTER TABLE public.cartridge_issues ADD CONSTRAINT cartridge_issues_pkey PRIMARY KEY (id);
ALTER TABLE public.cartridges ADD CONSTRAINT cartridges_pkey PRIMARY KEY (id);
ALTER TABLE public.chat_files ADD CONSTRAINT chat_files_pkey PRIMARY KEY (id);
ALTER TABLE public.chat_messages ADD CONSTRAINT chat_messages_pkey PRIMARY KEY (id);
ALTER TABLE public.checklist_log ADD CONSTRAINT checklist_log_pkey PRIMARY KEY (id);
ALTER TABLE public.deploy_files ADD CONSTRAINT deploy_files_pkey PRIMARY KEY (task_id, seq);
ALTER TABLE public.deploy_hosts ADD CONSTRAINT deploy_hosts_pkey PRIMARY KEY (host);
ALTER TABLE public.deploy_results ADD CONSTRAINT deploy_results_pkey PRIMARY KEY (task_id, host);
ALTER TABLE public.deploy_tasks ADD CONSTRAINT deploy_tasks_pkey PRIMARY KEY (id);
ALTER TABLE public.employees ADD CONSTRAINT employees_pkey PRIMARY KEY (emp);
ALTER TABLE public.goals ADD CONSTRAINT goals_pkey PRIMARY KEY (id);
ALTER TABLE public.it_issue_scans ADD CONSTRAINT it_issue_scans_pkey PRIMARY KEY (id);
ALTER TABLE public.it_stock_issues ADD CONSTRAINT it_stock_issues_pkey PRIMARY KEY (id);
ALTER TABLE public.it_stock_items ADD CONSTRAINT it_stock_items_pkey PRIMARY KEY (id);
ALTER TABLE public.kv ADD CONSTRAINT kv_pkey PRIMARY KEY (k);
ALTER TABLE public.po_list ADD CONSTRAINT po_list_pkey PRIMARY KEY (po_number);
ALTER TABLE public.po_scans ADD CONSTRAINT po_scans_pkey PRIMARY KEY (id);
ALTER TABLE public.print_jobs ADD CONSTRAINT print_jobs_pkey PRIMARY KEY (event_key);
ALTER TABLE public.stock_issues ADD CONSTRAINT stock_issues_pkey PRIMARY KEY (id);
ALTER TABLE public.stock_items ADD CONSTRAINT stock_items_pkey PRIMARY KEY (id);
ALTER TABLE public.tickets ADD CONSTRAINT tickets_pkey PRIMARY KEY (ticket_id);
ALTER TABLE public.todos ADD CONSTRAINT todos_pkey PRIMARY KEY (id);
ALTER TABLE public.users ADD CONSTRAINT users_pkey PRIMARY KEY (id);
ALTER TABLE public.users ADD CONSTRAINT users_username_key UNIQUE (username);
ALTER TABLE public.vendors ADD CONSTRAINT vendors_pkey PRIMARY KEY (vendor_id);

CREATE INDEX chat_msg_pair ON public.chat_messages USING btree (sender, receiver, id);
CREATE INDEX chat_msg_recv ON public.chat_messages USING btree (receiver, read_at);
CREATE INDEX print_jobs_printed_at_idx ON public.print_jobs USING btree (printed_at);

-- Row Level Security ON for every table (blocks the public Supabase API; the web app connects as the postgres role and is not affected)
DO $$ DECLARE r record; BEGIN
  FOR r IN SELECT tablename FROM pg_tables WHERE schemaname='public' LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.tablename);
  END LOOP;
END $$;

""";
}
