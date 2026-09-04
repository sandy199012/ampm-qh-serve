using AMPMWeb.Data;
using AMPMWeb.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Ephemeral Data Protection - works on Render free tier (no persistent disk)
builder.Services.AddDataProtection()
    .UseEphemeralDataProtectionProvider();

builder.Services.AddSession(opt => {
    opt.IdleTimeout = TimeSpan.FromHours(8);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
    opt.Cookie.Name = "AMPM.Session";
});

builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

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
