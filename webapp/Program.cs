using AMPMWeb.Data;
using AMPMWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSession(opt => {
    opt.IdleTimeout = TimeSpan.FromHours(8);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
});
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

// Render PORT env var support
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Init DB
var db = app.Services.GetRequiredService<DbService>();
db.Init();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

// No HTTPS redirect - Render handles SSL
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
