using AMPMWeb.Data;
using AMPMWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Disable antiforgery completely
builder.Services.AddControllersWithViews(o => 
    o.Filters.Add(new IgnoreAntiforgeryTokenAttribute()));

// Ephemeral data protection - no disk needed
builder.Services.AddDataProtection()
    .UseEphemeralDataProtectionProvider();

// Disable antiforgery service
builder.Services.AddAntiforgery(o => {
    o.SuppressXFrameOptionsHeader = true;
    o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;
});

builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

var dbPath = Environment.GetEnvironmentVariable("DB_PATH")
    ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "ampm.db");
SeedDb.RestoreIfEmpty(dbPath);
app.Services.GetRequiredService<DbService>().Init();

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
app.Run();
