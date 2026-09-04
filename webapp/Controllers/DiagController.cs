using Microsoft.AspNetCore.Mvc;

namespace AMPMWeb.Controllers;

public class DiagController : Controller
{
    public IActionResult Index()
    {
        var info = new
        {
            time = DateTime.Now.ToString(),
            routes = "ok",
            controllers = new[]
            {
                "Account", "Home", "Employees", "Helpdesk",
                "Assets", "PurchaseOrders", "ITStore", "Goals", "Budget", "Vendors"
            }
        };
        return Json(info);
    }
}
