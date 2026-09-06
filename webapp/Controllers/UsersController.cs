using Microsoft.AspNetCore.Mvc;
using AMPMWeb.Data;
using AMPMWeb.Services;
using Newtonsoft.Json;

namespace AMPMWeb.Controllers;

// User accounts + per-module View/Approve permission management.
// Reachable only by admin/superadmin — enforced globally by ModulePermissionFilter.
public class UsersController : Controller
{
    private readonly DbService _db;
    private readonly AuthService _auth;
    public UsersController(DbService db, AuthService auth) { _db = db; _auth = auth; }

    public IActionResult Index()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Modules = AuthService.Modules;
        ViewBag.ModuleLabels = AuthService.ModuleLabels;
        var users = _db.GetUsers();
        var perms = users.ToDictionary(u => u.Id, u => ParsePerms(u.Permissions));
        ViewBag.Perms = perms;
        return View(users);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Modules = AuthService.Modules;
        ViewBag.ModuleLabels = AuthService.ModuleLabels;
        ViewBag.Perms = new Dictionary<string, ModulePermission>();
        return View();
    }

    [HttpPost]
    public IActionResult Create(IFormCollection form)
    {
        var username = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var name = form["name"].ToString().Trim();
        var role = form["role"].ToString().Trim();
        var department = form["department"].ToString().Trim();
        var empId = form["empId"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "Username aur password dono zaroori hain.";
            return RedirectToAction("Create");
        }
        if (_db.UsernameExists(username))
        {
            TempData["Error"] = "Yeh username pehle se maujood hai, dusra try karo.";
            return RedirectToAction("Create");
        }
        if (role != "admin") role = "user";

        var permsJson = JsonConvert.SerializeObject(BuildPermissionsFromForm(form));
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        _db.CreateUser(username, hash, name, role, department, permsJson, string.IsNullOrWhiteSpace(empId) ? null : empId);
        TempData["Success"] = $"User '{username}' create ho gaya.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        var row = _db.GetUserById(id);
        if (row == null) return NotFound();
        ViewBag.User = _auth.GetCurrentUser(HttpContext);
        ViewBag.Modules = AuthService.Modules;
        ViewBag.ModuleLabels = AuthService.ModuleLabels;
        ViewBag.Perms = ParsePerms(row.Permissions);
        return View(row);
    }

    [HttpPost]
    public IActionResult Edit(int id, IFormCollection form)
    {
        var row = _db.GetUserById(id);
        if (row == null) return NotFound();

        var name = form["name"].ToString().Trim();
        var department = form["department"].ToString().Trim();
        var role = form["role"].ToString().Trim();
        var isActive = form["isActive"] == "on" ? 1 : 0;
        var empId = form["empId"].ToString().Trim();

        // The founding superadmin account can't be demoted or locked out from this screen.
        if (row.Role == "superadmin") { role = "superadmin"; isActive = 1; }
        else if (role != "admin") role = "user";

        var permsJson = JsonConvert.SerializeObject(BuildPermissionsFromForm(form));
        string? newHash = null;
        var newPassword = form["password"].ToString();
        if (!string.IsNullOrWhiteSpace(newPassword))
            newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        _db.UpdateUser(id, name, role, department, permsJson, isActive, newHash, string.IsNullOrWhiteSpace(empId) ? null : empId);
        TempData["Success"] = "User update ho gaya.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult Delete(int id)
    {
        var row = _db.GetUserById(id);
        if (row == null) return NotFound();
        if (row.Role == "superadmin")
        {
            TempData["Error"] = "Superadmin account delete nahi ho sakta.";
            return RedirectToAction("Index");
        }
        var me = _auth.GetCurrentUser(HttpContext);
        if (me != null && me.Id == id)
        {
            TempData["Error"] = "Aap apna khud ka account delete nahi kar sakte.";
            return RedirectToAction("Index");
        }
        _db.DeleteUser(id);
        TempData["Success"] = "User delete ho gaya.";
        return RedirectToAction("Index");
    }

    static Dictionary<string, ModulePermission> ParsePerms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonConvert.DeserializeObject<Dictionary<string, ModulePermission>>(json) ?? new(); }
        catch { return new(); }
    }

    static Dictionary<string, ModulePermission> BuildPermissionsFromForm(IFormCollection form)
    {
        var perms = new Dictionary<string, ModulePermission>();
        foreach (var m in AuthService.Modules)
        {
            bool view = form[$"view_{m}"] == "on";
            bool approve = form[$"approve_{m}"] == "on";
            if (view || approve) perms[m] = new ModulePermission { View = view, Approve = approve };
        }
        return perms;
    }
}
