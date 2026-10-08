using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

/// <summary>Data behind the dashboard charts (serialised to JSON in the view).</summary>
public class DashCharts
{
    public List<string> StatusLabels   { get; set; } = new();
    public List<int>    StatusValues   { get; set; } = new();
    public List<string> PriorityLabels { get; set; } = new();
    public List<int>    PriorityValues { get; set; } = new();
    public List<string> DayLabels      { get; set; } = new();
    public List<int>    DayValues      { get; set; } = new();
    public List<string> AssetLabels    { get; set; } = new();
    public List<int>    AssetValues    { get; set; } = new();
    public List<string> PoLabels       { get; set; } = new();
    public List<int>    PoValues       { get; set; } = new();
    public double       PoTotal        { get; set; }
    public int          CriticalOpen   { get; set; }
    public int          RaisedToday    { get; set; }
    public int          ResolvedCount  { get; set; }
}

public class HomeController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;

    public HomeController(DbService db, AuthService auth)
    { _db = db; _auth = auth; }

    public IActionResult Index()
    {
        if (!_auth.IsLoggedIn(HttpContext)) return RedirectToAction("Login","Account");
        var user = _auth.GetCurrentUser(HttpContext);
        if (user == null) return RedirectToAction("Login","Account");

        // Non-admin accounts (role "user") never see the admin dashboard —
        // they get the self-service "My Helpdesk" portal instead: their own
        // tickets and the ability to raise a new one, nothing global.
        if (!user.IsAdmin) return RedirectToAction("Index", "MyHelpdesk");

        var allTickets = _db.GetTickets();

        ViewBag.User          = user;
        ViewBag.Stats         = _db.GetStats();
        ViewBag.RecentTickets = allTickets.Take(8).ToList();
        ViewBag.LowStock      = _db.GetLowStockItems();
        ViewBag.PendingGoals  = _db.GetPendingGoalsCount();
        ViewBag.Charts        = BuildCharts(allTickets);
        return View();
    }

    // Chart data is best-effort: a problem in one source must never break the dashboard.
    private DashCharts BuildCharts(List<Dictionary<string,object?>> tickets)
    {
        var c = new DashCharts();
        string S(Dictionary<string,object?> d, string k) => d.GetValueOrDefault(k)?.ToString() ?? "";

        try
        {
            // Tickets by status
            foreach (var st in new[] { "Open", "In Progress", "Resolved", "Closed" })
            {
                c.StatusLabels.Add(st);
                c.StatusValues.Add(tickets.Count(t => S(t, "status") == st));
            }
            int otherSt = tickets.Count - c.StatusValues.Sum();
            if (otherSt > 0) { c.StatusLabels.Add("Other"); c.StatusValues.Add(otherSt); }
            c.ResolvedCount = tickets.Count(t => S(t, "status") is "Resolved" or "Closed");

            // Tickets by priority
            foreach (var pr in new[] { "Critical", "High", "Medium", "Low" })
            {
                c.PriorityLabels.Add(pr);
                c.PriorityValues.Add(tickets.Count(t => S(t, "priority") == pr));
            }
            c.CriticalOpen = tickets.Count(t => S(t, "priority") == "Critical"
                                             && S(t, "status") is not ("Resolved" or "Closed"));

            // Tickets raised, last 7 days (IST)
            var today = IstTime.Today;
            var perDay = new Dictionary<DateTime,int>();
            for (int i = 6; i >= 0; i--) perDay[today.AddDays(-i)] = 0;
            foreach (var t in tickets)
                if (DateTime.TryParse(S(t, "dateRaised"), out var d) && perDay.ContainsKey(d.Date))
                    perDay[d.Date]++;
            foreach (var kv in perDay)
            {
                c.DayLabels.Add(kv.Key.ToString("dd MMM"));
                c.DayValues.Add(kv.Value);
            }
            c.RaisedToday = perDay.GetValueOrDefault(today);
        }
        catch { }

        try
        {
            // Assets by type: top 6 + "Other"
            var groups = _db.GetAssets()
                .GroupBy(a => { var t = S(a, "assetType").Trim(); return t == "" ? "Unspecified" : t; })
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).ThenBy(g => g.Name).ToList();
            foreach (var g in groups.Take(6)) { c.AssetLabels.Add(g.Name); c.AssetValues.Add(g.Count); }
            int rest = groups.Skip(6).Sum(g => g.Count);
            if (rest > 0) { c.AssetLabels.Add("Other"); c.AssetValues.Add(rest); }
        }
        catch { }

        try
        {
            // Purchase orders by status + total value
            var pos = _db.GetPOs();
            foreach (var g in pos.GroupBy(p => { var s = S(p, "status"); return s == "" ? "Draft" : s; })
                                 .OrderByDescending(g => g.Count()))
            { c.PoLabels.Add(g.Key); c.PoValues.Add(g.Count()); }
            foreach (var p in pos)
                if (double.TryParse(S(p, "grandTotal"), System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var gt))
                    c.PoTotal += gt;
        }
        catch { }

        return c;
    }

    public IActionResult AccessDenied()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        return View();
    }
}
