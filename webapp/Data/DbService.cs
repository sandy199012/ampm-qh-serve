using Npgsql;
using Newtonsoft.Json;

namespace AMPMWeb.Data;

public class DbService
{
    private readonly string _connStr;

    public DbService(IConfiguration config)
    {
        // Supabase PostgreSQL connection
        var connStr = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? config["DatabaseUrl"]
            ?? "Host=db.nczpdiyhtuegpznpjwth.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Admin@@1990##;SSL Mode=Require;Trust Server Certificate=true";
        _connStr = connStr;
    }

    public NpgsqlConnection GetConn()
    {
        var conn = new NpgsqlConnection(_connStr);
        conn.Open();
        return conn;
    }

    public void Init()
    {
        using var conn = GetConn();
        using var cmd = conn.CreateCommand();

        // Create all tables
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
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
            CREATE TABLE IF NOT EXISTS it_stock_items (id TEXT PRIMARY KEY, item_type TEXT, name TEXT NOT NULL, data TEXT NOT NULL, ts TEXT);
            CREATE TABLE IF NOT EXISTS it_stock_issues (id TEXT PRIMARY KEY, item_id TEXT, issue_no TEXT, data TEXT NOT NULL, ts TEXT);
        ";
        cmd.ExecuteNonQuery();

        // Ensure admin user
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username='sandy'";
        var count = (long)cmd.ExecuteScalar()!;
        if (count == 0)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("AMPM@Sandy2026");
            cmd.CommandText = "INSERT INTO users (username,password_hash,name,role,department,is_active,created_at) VALUES (@u,@h,@n,@r,@d,1,@t)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@u", "sandy");
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@n", "Sandeep Kumar Singh Kushwaha");
            cmd.Parameters.AddWithValue("@r", "superadmin");
            cmd.Parameters.AddWithValue("@d", "IT");
            cmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    // ── Generic Helpers ───────────────────────────────────────
    public List<T> Query<T>(string sql, object? param = null)
    {
        using var conn = GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, param);
        var list = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (typeof(T) == typeof(string))
                list.Add((T)(object)(reader.IsDBNull(0) ? "" : reader.GetString(0)));
            else if (typeof(T) == typeof(int))
                list.Add((T)(object)Convert.ToInt32(reader.GetValue(0)));
            else if (typeof(T) == typeof(long))
                list.Add((T)(object)Convert.ToInt64(reader.GetValue(0)));
            else
                list.Add((T)reader.GetValue(0));
        }
        return list;
    }

    public T? QueryFirst<T>(string sql, object? param = null)
        => Query<T>(sql, param).FirstOrDefault();

    public int Execute(string sql, object? param = null)
    {
        using var conn = GetConn();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, param);
        return cmd.ExecuteNonQuery();
    }

    void AddParams(NpgsqlCommand cmd, object? param)
    {
        if (param == null) return;
        foreach (var prop in param.GetType().GetProperties())
        {
            var val = prop.GetValue(param);
            cmd.Parameters.AddWithValue("@" + prop.Name, val ?? DBNull.Value);
        }
    }

    // ── Employees ─────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetEmployees()
    {
        var rows = Query<string>("SELECT data FROM employees ORDER BY emp");
        return rows.Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
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
        Execute("INSERT INTO tickets VALUES(@id,@data,@status,@ts) ON CONFLICT(ticket_id) DO UPDATE SET data=@data,status=@status,ts=@ts",
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

    public void KSet(string key, object value)
        => Execute("INSERT INTO kv (k,v) VALUES (@k,@v) ON CONFLICT (k) DO UPDATE SET v=@v",
            new { k = key, v = JsonConvert.SerializeObject(value) });

    // ── Assets ────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetAssets()
        => KGetObj<List<Dictionary<string,object?>>>("asset_stock") ?? new();

    // ── Budget ────────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetBudget()
        => KGetObj<List<Dictionary<string,object?>>>("budget") ?? new();

    public void SaveBudget(List<Dictionary<string,object?>> budget)
        => KSet("budget", budget);

    public List<Dictionary<string,object?>> GetBills()
        => KGetObj<List<Dictionary<string,object?>>>("bills") ?? new();

    public void SaveBills(List<Dictionary<string,object?>> bills)
        => KSet("bills", bills);

    // ── Software Licenses ────────────────────────────────────
    public List<Dictionary<string,object?>> GetLicenses()
        => KGetObj<List<Dictionary<string,object?>>>("licenses") ?? new();

    public void SaveLicenses(List<Dictionary<string,object?>> licenses)
        => KSet("licenses", licenses);

    // ── Vendors ───────────────────────────────────────────────
    public List<Dictionary<string,object?>> GetVendors()
    {
        var rows = Query<string>("SELECT data FROM vendors ORDER BY name");
        return rows.Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new()).ToList();
    }

    // ── Stats ─────────────────────────────────────────────────
    public DashboardStats GetStats()
    {
        var assets = GetAssets();
        int stockCount = 0;
        try { stockCount = QueryFirst<int>("SELECT COUNT(*) FROM it_stock_items"); } catch {}

        var today = DateTime.Today;
        int expiringLicenses = 0;
        try {
            expiringLicenses = GetLicenses().Count(l => {
                if (!DateTime.TryParse(l.GetValueOrDefault("renewalDate")?.ToString(), out var rd)) return false;
                int.TryParse(l.GetValueOrDefault("alertDays")?.ToString(), out var ad);
                int window = ad > 0 ? ad : 30;
                return (rd.Date - today).TotalDays <= window;
            });
        } catch {}

        int billsDue = 0;
        try {
            billsDue = GetBills().Count(b => {
                var st = b.GetValueOrDefault("status")?.ToString();
                return st == "Pending" || st == "Submitted" || st == "Overdue";
            });
        } catch {}

        return new DashboardStats
        {
            TotalEmployees   = QueryFirst<int>("SELECT COUNT(*) FROM employees"),
            TotalPOs         = QueryFirst<int>("SELECT COUNT(*) FROM po_list"),
            OpenTickets      = QueryFirst<int>("SELECT COUNT(*) FROM tickets WHERE status='Open'"),
            TotalVendors     = QueryFirst<int>("SELECT COUNT(*) FROM vendors"),
            TotalGoals       = QueryFirst<int>("SELECT COUNT(*) FROM goals"),
            TotalAssets      = assets.Count,
            TotalStockItems  = stockCount,
            ExpiringLicenses = expiringLicenses,
            BillsDue         = billsDue,
        };
    }

    public List<Dictionary<string,object?>> GetLowStockItems()
    {
        try {
            return Query<string>("SELECT data FROM it_stock_items")
                .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
                .Where(i => {
                    int.TryParse(i.GetValueOrDefault("totalQty")?.ToString(), out var t);
                    int.TryParse(i.GetValueOrDefault("issuedQty")?.ToString(), out var iss);
                    return (t - iss) <= 2;
                }).ToList();
        } catch { return new(); }
    }

    public int GetPendingGoalsCount()
    {
        try {
            return Query<string>("SELECT data FROM goals")
                .Select(r => JsonConvert.DeserializeObject<Dictionary<string,object?>>(r) ?? new())
                .Count(g => g.GetValueOrDefault("status")?.ToString() is "Not Started" or "In Progress");
        } catch { return 0; }
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
    public int ExpiringLicenses { get; set; }
    public int BillsDue { get; set; }
}
