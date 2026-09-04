using AMPMWeb.Data;
using AMPMWeb.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<DbService>();
builder.Services.AddSingleton<AuthService>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
app.Services.GetRequiredService<DbService>().Init();

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
app.Run();
