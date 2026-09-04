using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace AMPMWeb.Data;

public class DbService
{
    private readonly string _connStr;

    public DbService(IConfiguration config)
    {
        // Render pe /tmp, local pe Data folder
        string dbPath = Environment.GetEnvironmentVariable("DB_PATH")
            ?? config["DatabasePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "ampm.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connStr = $"Data Source={dbPath}";
    }

    public SqliteConnection GetConn()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return conn;
    }

    public void Init()
    {
        using var conn = GetConn();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                name TEXT, role TEXT DEFAULT 'user',
                department TEXT, is_active INTEGER DEFAULT 1,
                created_at TEXT
            );
            CREATE TABLE IF NOT EXISTS employees (emp TEXT PRIMARY KEY, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS po_list (po_number TEXT PRIMARY KEY, data TEXT NOT NULL, vendor TEXT, total REAL, status TEXT, ts TEXT);
            CREATE TABLE IF NOT EXISTS tickets (ticket_id TEXT PRIMARY KEY, data TEXT NOT NULL, status TEXT, ts TEXT);
            CREATE TABLE IF NOT EXISTS kv (k TEXT PRIMARY KEY, v TEXT);
            CREATE TABLE IF NOT EXISTS vendors (vendor_id TEXT PRIMARY KEY, name TEXT NOT NULL, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS cartridges (id TEXT PRIMARY KEY, name TEXT NOT NULL, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS cartridge_issues (id TEXT PRIMARY KEY, cartridge_id TEXT, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS goals (id TEXT PRIMARY KEY, week_no INTEGER, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS stock_items (id TEXT PRIMARY KEY, item_type TEXT, name TEXT NOT NULL, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS stock_issues (id TEXT PRIMARY KEY, item_id TEXT, issue_no TEXT, data TEXT NOT NULL, ts TEXT);
        ");

        // Always ensure sandy user exists with correct password
        string hash = BCrypt.Net.BCrypt.HashPassword("AMPM@Sandy2026");
        var existing = conn.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM users WHERE username='sandy'");
        if (existing == 0)
        {
            conn.Execute("INSERT INTO users (username,password_hash,name,role,department,is_active,created_at) VALUES ('sandy',@h,'Sandeep Kumar Singh Kushwaha','superadmin','IT',1,@t)",
                new { h = hash, t = DateTime.Now.ToString("o") });
        }
        else
        {
            // Update hash in case it's corrupted
            conn.Execute("UPDATE users SET password_hash=@h WHERE username='sandy'", new { h = hash });
        }
    }

    // ── Generic query helpers ─────────────────────────────────
    public List<T> Query<T>(string sql, object? param = null)
    {
        using var conn = GetConn();
        return conn.Query<T>(sql, param).ToList();
    }

    public T? QueryFirst<T>(string sql, object? param = null)
    {
        using var conn = GetConn();
        return conn.QueryFirstOrDefault<T>(sql, param);
    }

    public int Execute(string sql, object? param = null)
    {
        using var conn = GetConn();
        return conn.Execute(sql, param);
    }

    // ── Employee ──────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetEmployees()
    {
        var rows = Query<(string emp, string data)>("SELECT emp, data FROM employees ORDER BY emp");
        return rows.Select(r => {
            var d = JsonConvert.DeserializeObject<Dictionary<string,object?>>(r.data) ?? new();
            d["emp"] = r.emp;
            return d;
        }).ToList();
    }

    // ── Tickets ───────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetTickets(string? status = null)
    {
        string sql = status == null
            ? "SELECT data FROM tickets ORDER BY ts DESC"
            : "SELECT data FROM tickets WHERE status=@s ORDER BY ts DESC";
        var rows = Query<string>(sql, new { s = status });
        return rows.Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
    }

    public void SaveTicket(Dictionary<string,object?> ticket)
    {
        string id = ticket.GetValueOrDefault("ticketId")?.ToString() ?? Guid.NewGuid().ToString("N")[..8];
        string status = ticket.GetValueOrDefault("status")?.ToString() ?? "Open";
        ticket["ticketId"] = id;
        string json = JsonConvert.SerializeObject(ticket);
        Execute("INSERT OR REPLACE INTO tickets VALUES(@id,@data,@status,@ts)",
            new { id, data=json, status, ts=DateTime.Now.ToString("o") });
    }

    // ── PO ────────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetPOs()
    {
        var rows = Query<string>("SELECT data FROM po_list ORDER BY ts DESC");
        return rows.Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
    }

    // ── KV Store ──────────────────────────────────────────────
    public string? KGet(string key)
        => QueryFirst<string>("SELECT v FROM kv WHERE k=@k", new { k = key });

    public T? KGetObj<T>(string key)
    {
        var v = KGet(key);
        return v == null ? default : JsonConvert.DeserializeObject<T>(v);
    }

    // ── Dashboard Stats ───────────────────────────────────────
    public DashboardStats GetStats()
    {
        using var conn = GetConn();
        var assets = KGetObj<List<Dictionary<string,object?>>>("asset_stock") ?? new();
        return new DashboardStats
        {
            TotalEmployees = conn.QueryFirst<int>("SELECT COUNT(*) FROM employees"),
            TotalPOs       = conn.QueryFirst<int>("SELECT COUNT(*) FROM po_list"),
            OpenTickets    = conn.QueryFirst<int>("SELECT COUNT(*) FROM tickets WHERE status='Open'"),
            TotalVendors   = conn.QueryFirst<int>("SELECT COUNT(*) FROM vendors"),
            TotalGoals     = conn.QueryFirst<int>("SELECT COUNT(*) FROM goals"),
            TotalAssets    = assets.Count,
            TotalStockItems= conn.QueryFirst<int>("SELECT COUNT(*) FROM it_stock_items") +
                             conn.QueryFirst<int>("SELECT COUNT(*) FROM stock_items"),
        };
    }

    public List<Dictionary<string,object?>> GetLowStockItems()
    {
        var items = Query<string>("SELECT data FROM it_stock_items")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .Where(i => {
                int.TryParse(i.GetValueOrDefault("totalQty")?.ToString(), out var t);
                int.TryParse(i.GetValueOrDefault("issuedQty")?.ToString(), out var iss);
                return (t - iss) <= 2;
            }).ToList();
        return items;
    }

    public int GetPendingGoalsCount()
    {
        var goals = Query<string>("SELECT data FROM goals")
            .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
            .Count(g => g.GetValueOrDefault("status")?.ToString() is "Not Started" or "In Progress");
        return goals;
    }

    // ── Assets ────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetAssets()
        => KGetObj<List<Dictionary<string,object?>>>("asset_stock") ?? new();

    // ── Budget ────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetBudget()
        => KGetObj<List<Dictionary<string,object?>>>("budget") ?? new();

    // ── Bills ─────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetBills()
        => KGetObj<List<Dictionary<string,object?>>>("bills") ?? new();

    // ── Vendors ───────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetVendors()
    {
        var rows = Query<string>("SELECT data FROM vendors ORDER BY name");
        return rows.Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
    }

    public string DbFilePath => _connStr.Replace("Data Source=", "");
public static class SqliteExtensions
{
    public static List<T> Query<T>(this SqliteConnection conn, string sql, object? param = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, param);
        var list = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (typeof(T) == typeof(string)) list.Add((T)(object)reader.GetString(0));
            else if (typeof(T) == typeof(int))  list.Add((T)(object)reader.GetInt32(0));
            else if (typeof(T).IsValueType || typeof(T) == typeof(string)) list.Add((T)reader.GetValue(0));
            else list.Add(MapToObject<T>(reader));
        }
        return list;
    }

    public static T QueryFirst<T>(this SqliteConnection conn, string sql, object? param = null)
        => conn.Query<T>(sql, param).FirstOrDefault()!;

    public static T? QueryFirstOrDefault<T>(this SqliteConnection conn, string sql, object? param = null)
        => conn.Query<T>(sql, param).FirstOrDefault();

    public static int Execute(this SqliteConnection conn, string sql, object? param = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, param);
        return cmd.ExecuteNonQuery();
    }

    static void AddParams(SqliteCommand cmd, object? param)
    {
        if (param == null) return;
        foreach (var prop in param.GetType().GetProperties())
            cmd.Parameters.AddWithValue("@" + prop.Name, prop.GetValue(param) ?? DBNull.Value);
    }

    static T MapToObject<T>(SqliteDataReader reader)
    {
        var obj = Activator.CreateInstance<T>();
        var props = typeof(T).GetProperties();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var prop = props.FirstOrDefault(p => p.Name.Equals(reader.GetName(i), StringComparison.OrdinalIgnoreCase));
            if (prop != null && !reader.IsDBNull(i))
                prop.SetValue(obj, Convert.ChangeType(reader.GetValue(i), prop.PropertyType));
        }
        return obj;
    }
}

public class DashboardStats
{
    public int TotalEmployees { get; set; }
    public int TotalPOs { get; set; }
    public int OpenTickets { get; set; }
    public int TotalVendors { get; set; }
    public int TotalGoals { get; set; }
    public int TotalAssets { get; set; }
    public int TotalStockItems { get; set; }
}
