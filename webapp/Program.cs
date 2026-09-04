using AMPMWeb.Data;
using AMPMWeb.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => {
    options.Filters.Add(new IgnoreAntiforgeryTokenAttribute());
});
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
var dbService = app.Services.GetRequiredService<DbService>();
var dbPath = Environment.GetEnvironmentVariable("DB_PATH")
    ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "ampm.db");
SeedDb.RestoreIfEmpty(dbPath);
dbService.Init();

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
app.Run();
