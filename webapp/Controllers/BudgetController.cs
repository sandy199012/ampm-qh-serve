using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;

namespace AMPMWeb.Controllers;

public class BudgetController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public BudgetController(DbService db, AuthService auth) { _db=db; _auth=auth; }

    public IActionResult Index()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        var budget = _db.GetBudget();
        double totalBudget = budget.Sum(b => { double.TryParse(b.GetValueOrDefault("allocated")?.ToString(), out var v); return v; });
        double totalSpent  = budget.Sum(b => { double.TryParse(b.GetValueOrDefault("spent")?.ToString(), out var v); return v; });
        ViewBag.TotalBudget = totalBudget;
        ViewBag.TotalSpent  = totalSpent;
        ViewBag.Remaining   = totalBudget - totalSpent;
        ViewBag.Bills       = _db.GetBills();
        return View(budget);
    }
}
