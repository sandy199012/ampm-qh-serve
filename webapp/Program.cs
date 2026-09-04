using AMPMWeb.Data;
using AMPMWeb.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Fix Data Protection for Render (no persistent disk on free tier)
builder.Services.AddDataProtection()
    .SetApplicationName("AMPMTool")
    .DisableAutomaticKeyGeneration();

// Antiforgery with fixed key
builder.Services.AddAntiforgery(options => {
    options.Cookie.Name = "AMPM.AF";
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSession(opt => {
    opt.IdleTimeout = TimeSpan.FromHours(8);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
    opt.Cookie.Name = "AMPM.Session";
    opt.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

// Render PORT support
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Init DB
var db = app.Services.GetRequiredService<DbService>();
db.Init();

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
