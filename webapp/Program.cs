using AMPMWeb.Data;
using AMPMWeb.Services;
using AMPMWeb.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<ModulePermissionFilter>();

builder.Services.AddControllersWithViews(o => {
    o.Filters.Add(new IgnoreAntiforgeryTokenAttribute());
    o.Filters.AddService<ModulePermissionFilter>();
}).AddRazorRuntimeCompilation();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
app.Services.GetRequiredService<DbService>().Init();

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
// Allow encoded slashes in route values
app.MapControllerRoute("podetails", "PurchaseOrders/Details/{*id}", 
    new { controller="PurchaseOrders", action="Details" });
app.Run();
